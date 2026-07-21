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
            if (rend != null)
            {
                rend.enabled = !IsOwner;
                // Colour by OwnerId, which FishNet assigns and syncs — so every client derives
                // the same colour for the same player with no extra networking.
                var c = PlayerColors.For(OwnerId);
                var m = rend.material;              // instance, per player
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                m.color = c;
            }
        }

        if (IsOwner)
        {
            // Apply the weapon picked on the connect screen. Owner-only: it's a local choice,
            // and remote players never render your tracers anyway.
            var wc = GetComponent<WeaponController>();
            if (wc != null) wc.SetLockedWeapon(LoadoutChoice.WeaponIndex);
            // The F1-F7 runtime picker is an offline testing tool. Both loadouts are locked
            // once networked, and leaving it visible would suggest otherwise.
            DisableIfPresent(GetComponent<PassivePicker>());
            // Saved sensitivity / FOV / volume only exist once this player's components do.
            GameSettings.Load();
            GameSettings.Apply();
            return;
        }

        DisableIfPresent(GetComponent<InputReader>());
        DisableIfPresent(GetComponent<MouseLook>());
        DisableIfPresent(GetComponent<WeaponController>());
        DisableIfPresent(GetComponent<SpeedHud>());
        DisableIfPresent(GetComponent<PassivePicker>());
        DisableIfPresent(GetComponent<SpeedFeel>());
        DisableIfPresent(GetComponent<HitFeedback>()); // your markers, not theirs
        DisableIfPresent(GetComponent<DeathCam>());    // their death must not move your view

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
        if (hp == null)
        {
            // Not a player — a bot, or anything else networked and damageable. Bots became
            // NetworkObjects so their health is server-owned too, and without this branch every
            // shot at one was silently dropped here and they were unkillable in a hosted match.
            ApplyToNonPlayer(victim, damage);
            return;
        }

        bool wasAlive = hp.Alive;
        // Recorded BEFORE the damage lands, so if this hit kills them PlayerHealth.Die already
        // knows who to credit. Die is the one place the kill feed is announced from.
        hp.RecordServerAttacker(base.NetworkObject);
        hp.Damage(damage);

        // Point the victim back at us. Sent from the victim's own PlayerNetwork so the TargetRpc
        // reaches their client, with our position as the source.
        var victimNet = victim.GetComponent<PlayerNetwork>();
        if (victimNet != null && victim.Owner != null)
            victimNet.ShowDamageFrom(victim.Owner, transform.position, base.NetworkObject);

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

    // Bots and other server-owned targets. No score, no damage-direction wedge — they have no
    // owner to send one to — but the kill cue still comes back, because a bot dying is exactly
    // as worth confirming to the shooter as a player dying.
    void ApplyToNonPlayer(FishNet.Object.NetworkObject victim, float damage)
    {
        var target = victim.GetComponent<IDamageable>();
        if (target == null) return;

        var botHealth = victim.GetComponent<Health>();
        bool wasAlive = botHealth == null || botHealth.Alive;

        target.Damage(damage);

        if (wasAlive && botHealth != null && !botHealth.Alive)
        {
            ConfirmKill(Owner);
            // Bots die through Health, not PlayerHealth, so they never reach the announce site
            // in PlayerHealth.Die. Announced here instead — a practice session against bots is
            // exactly where a feed earns its keep.
            KillFeed.Announce(base.NetworkObject, victim);
        }
    }

    // Gunfire is the one sound that cannot be derived locally: WeaponController is disabled on
    // non-owners, so nothing about a remote player's state reveals that they shot. Footsteps,
    // jumps and landings all come free from replicated movement, so only this needs a message.
    [FishNet.Object.ServerRpc]
    public void ReportFire(int weaponIndex)
    {
        BroadcastFire(weaponIndex);
    }

    // ExcludeOwner: the shooter already played it locally the instant they fired, with no
    // round-trip. Replaying it here would double it up a few hundred ms late.
    [FishNet.Object.ObserversRpc(ExcludeOwner = true)]
    void BroadcastFire(int weaponIndex)
    {
        var a = GetComponent<PlayerAudio>();
        if (a != null) a.PlayFire(weaponIndex);
    }

    // Shot lines for everyone else. Without this an enemy could fire at you from across the
    // map with nothing at all on screen — WeaponController is disabled on non-owners, so its
    // tracers never existed for observers.
    [FishNet.Object.ServerRpc]
    public void ReportTracer(Vector3 from, Vector3 to)
    {
        BroadcastTracer(from, to);
    }

    [FishNet.Object.ObserversRpc(ExcludeOwner = true)]
    void BroadcastTracer(Vector3 from, Vector3 to)
    {
        var tr = GetComponent<TracerRenderer>();
        if (tr != null) tr.Show(from, to, new Color(1f, 0.85f, 0.5f, 1f), 0.06f);
    }

    // Tells the VICTIM which direction the shot came from. Being hit with no idea where from
    // is the worst case in a fast game — you cannot even choose which way to break.
    [FishNet.Object.TargetRpc]
    void ShowDamageFrom(FishNet.Connection.NetworkConnection conn, Vector3 worldPos,
        FishNet.Object.NetworkObject attacker)
    {
        var fb = GetComponent<HitFeedback>();
        if (fb != null) fb.ShowDamageFrom(worldPos);

        // Same message, second consumer: the death camera needs to know who to turn towards,
        // and this already carries exactly that. A separate "you were killed by" RPC would be
        // the same fact crossing the wire twice.
        //
        // The attacker reference rides along so the death screen can NAME them. Resolved here
        // rather than sent as a string: the name already lives on their PlayerIdentity, and a
        // copied string would go stale the moment they renamed.
        var hp = GetComponent<PlayerHealth>();
        if (hp == null) return;

        string attackerName = null;
        Transform attackerTransform = null;
        if (attacker != null)
        {
            var id = attacker.GetComponent<PlayerIdentity>();
            attackerName = id != null ? id.Name : attacker.gameObject.name;
            attackerTransform = attacker.transform; // live, so the death camera can follow them
        }
        hp.RecordAttacker(worldPos, attackerName, attackerTransform);
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
