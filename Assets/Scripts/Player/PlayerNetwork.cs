using FishNet.Managing.Predicting;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

// Networked driver for PlayerMotor — client-side prediction + server reconciliation.
//
// The whole design of the motor was aimed at this: Step(cmd, dt) is a pure function with no
// hidden inputs, and MotorState captures its complete mutable state. So this class is thin —
// it feeds commands into Step() on the network tick and restores MotorState when the server
// corrects us. All the movement rules stay in PlayerMotor, untouched and shared by both sides.
//
// Flow per tick:
//   OnTick     -> owner samples input -> PerformReplicate -> motor.Step()
//   OnPostTick -> CreateReconcile -> PerformReconcile -> motor.SetState() on mispredict
//
// Inherits TickNetworkBehaviour (not plain NetworkBehaviour) for the SetTickCallbacks path,
// matching FishNet's own CharacterController prediction demo.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerNetwork : TickNetworkBehaviour
{
    // The replicated input. Wraps InputCmd rather than duplicating its fields, so there is
    // exactly one definition of "one tick of intent" and it cannot drift from the sim's.
    public struct MoveData : IReplicateData
    {
        public InputCmd Cmd;

        public MoveData(InputCmd cmd)
        {
            Cmd = cmd;
            _tick = 0;
        }

        // Set by FishNet at runtime; never assign manually.
        private uint _tick;

        public void Dispose() { } // InputCmd is all value types — nothing to clean up
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    // The authoritative state a mispredicting client gets snapped back to.
    public struct ReconcileData : IReconcileData
    {
        public MotorState State;

        public ReconcileData(MotorState state)
        {
            State = state;
            _tick = 0;
        }

        private uint _tick;

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    PlayerMotor motor;
    InputReader input;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        input = GetComponent<InputReader>();
        SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
    }

    // Hand the sim over to the network tick only once networking is actually running, and
    // give it back on stop — so the scene still plays offline off FixedUpdate.
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (motor != null) motor.ExternallyDriven = true;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (motor != null) motor.ExternallyDriven = false;
    }

    // Every client instantiates every player, including other people's. Only the owner may
    // drive input, steer the view, or render a camera — otherwise a remote player's object
    // would fight you for the mouse and the screen.
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner) return;

        DisableIfPresent(GetComponent<InputReader>());
        DisableIfPresent(GetComponent<MouseLook>());
        DisableIfPresent(GetComponent<WeaponController>());
        DisableIfPresent(GetComponent<SpeedHud>());
        DisableIfPresent(GetComponent<PassivePicker>());
        DisableIfPresent(GetComponent<SpeedFeel>());

        // Remote players keep their body but must not render or listen.
        var cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.gameObject.SetActive(false);
    }

    static void DisableIfPresent(MonoBehaviour mb)
    {
        if (mb != null) mb.enabled = false;
    }

    protected override void TimeManager_OnTick() => PerformReplicate(BuildMoveData());

    protected override void TimeManager_OnPostTick() => CreateReconcile();

    // Only the controller builds real input. Everyone else sends default, which FishNet
    // fills in from the owner's replicated data (or predicts).
    MoveData BuildMoveData()
    {
        if (!IsOwner || input == null) return default;
        return new MoveData(input.Sample());
    }

    // Both server and client build this; the client uses its copy as a fallback when a
    // server packet is dropped.
    public override void CreateReconcile()
    {
        if (motor == null) return;
        PerformReconcile(new ReconcileData(motor.GetState()));
    }

    [Replicate]
    void PerformReplicate(MoveData md, ReplicateState state = ReplicateState.Invalid,
        Channel channel = Channel.Unreliable)
    {
        if (motor == null) return;

        // TickDelta, never Time.fixedDeltaTime — during a reconcile this replays several
        // ticks inside one frame, so the sim's dt must come from the tick, not the frame.
        // NOTE: this makes the NetworkManager's TickRate the sim rate. The movement is tuned
        // at 100Hz (see GameTick), so TickRate must be 100 or the feel changes.
        motor.Step(md.Cmd, (float)base.TimeManager.TickDelta);
    }

    [Reconcile]
    void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        // Snap the whole sim back to the server's truth. FishNet then replays every input
        // since that tick through PerformReplicate above, which is why MotorState has to be
        // complete — any field missing here is a silent desync.
        if (motor != null) motor.SetState(rd.State);
    }
}
