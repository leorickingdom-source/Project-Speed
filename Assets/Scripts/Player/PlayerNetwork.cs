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

        // The body capsule spans the whole player, so it encloses the first-person camera.
        // Hide it for the owner (you never see your own body) and show it for everyone else —
        // without this, remote players render nothing at all and appear invisible.
        var body = transform.Find("Body");
        if (body != null)
        {
            var rend = body.GetComponent<Renderer>();
            if (rend != null) rend.enabled = !IsOwner;
        }

        if (IsOwner)
        {
            // Apply the weapon picked on the connect screen. Owner-only: it's a local choice,
            // and remote players never render your tracers anyway.
            var wc = GetComponent<WeaponController>();
            if (wc != null) wc.SetLockedWeapon(LoadoutChoice.WeaponIndex);
            return;
        }

        DisableIfPresent(GetComponent<InputReader>());
        DisableIfPresent(GetComponent<MouseLook>());
        DisableIfPresent(GetComponent<WeaponController>());
        DisableIfPresent(GetComponent<SpeedHud>());
        DisableIfPresent(GetComponent<PassivePicker>());
        DisableIfPresent(GetComponent<SpeedFeel>());
        DisableIfPresent(GetComponent<HitFeedback>()); // your markers, not theirs

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

    // Damage is applied on the SERVER only. The shooting client detects its own hit and
    // reports it here; the server applies it to the victim's PlayerHealth, whose SyncVar
    // then replicates the new health to everyone.
    //
    // This trusts the client's hit claim — standard for a prototype, and the honest
    // alternative (server-side raycast with lag compensation) is a much larger job. Swap
    // this out before anything competitive.
    [FishNet.Object.ServerRpc]
    public void ReportHit(FishNet.Object.NetworkObject victim, float damage)
    {
        if (victim == null || damage <= 0f) return;
        var hp = victim.GetComponent<PlayerHealth>();
        if (hp == null) return;

        bool wasAlive = hp.Alive;
        hp.Damage(damage);

        // Only the server knows whether that killed them — health is server-owned — so the
        // kill cue has to come back to the shooter rather than being guessed locally.
        if (wasAlive && !hp.Alive)
        {
            ConfirmKill(Owner);
            // Credit the kill here rather than in PlayerHealth: only this path knows WHO did
            // it. Deaths are counted in PlayerHealth so pit falls and out-of-bounds count too.
            var mine = GetComponent<PlayerScore>();
            if (mine != null) mine.AddKill();

            var match = FindAnyObjectByType<MatchManager>();
            if (match != null) match.CheckForWinner();
        }
    }

    [FishNet.Object.TargetRpc]
    void ConfirmKill(FishNet.Connection.NetworkConnection conn)
    {
        var fb = GetComponent<HitFeedback>();
        if (fb != null) fb.ShowKill();
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
