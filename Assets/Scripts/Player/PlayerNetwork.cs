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

    // OFFLINE path. OnStartClient only fires for a spawned network object, so a player dropped
    // straight into a scene — which is how SampleScene is set up and how most editor testing
    // actually happens — never built a body at all. Everything visual about a player lived
    // behind a connection.
    //
    // Hidden by default for the same reason as the networked path: it is YOUR body, and you do
    // not see your own. Set showOwnBody on the component to look at it without a second client.
    void Start()
    {
        // Asked through NetworkObject rather than IsSpawned: IsSpawned dereferences a cache
        // FishNet only fills in during initialization, so on the very object this guard exists
        // to catch — a scene-placed player that never spawned — reading it throws.
        if (NetPresence.IsSpawned(this)) return;
        var body = transform.Find("Body");
        var rend = body != null ? body.GetComponent<Renderer>() : null;
        var humanoid = PlayerBody.Attach(transform, PlayerColors.For(0), showOwnBody, hitboxes: false);
        if (humanoid != null && rend != null) rend.enabled = false;
        if (humanoid != null && GetComponent<ThirdPersonView>() == null)
            gameObject.AddComponent<ThirdPersonView>();
    }

    [Tooltip("Offline testing only: render your OWN humanoid. You are inside it, so expect to " +
             "see it from within — enough to confirm the model loads, tints and animates " +
             "without standing up a second client.")]
    public bool showOwnBody;

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
                // Colour by OwnerId, which FishNet assigns and syncs — so every client derives
                // the same colour for the same player with no extra networking.
                var c = PlayerColors.For(OwnerId);
                // Tinted even when the capsule is never drawn: CorpseFx copies its material to
                // colour the body that falls over, so leaving it untinted would make every
                // corpse the wrong player.
                var m = rend.material;              // instance, per player
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                m.color = c;

                // The humanoid replaces the capsule as the VISUAL only. The capsule collider
                // stays exactly where it was, so hitscan, the headshot band and the crouch
                // stance all still test against the shape PlayerMotor has always driven.
                var humanoid = PlayerBody.Attach(transform, c, !IsOwner, hitboxes: !IsOwner);
                if (humanoid != null)
                {
                    // No head cap on a humanoid. The cap is sized from the capsule's bounds —
                    // a 1m-wide disc — which read as a head zone on a featureless pill and
                    // reads as nothing on a body that already has a head. The band itself is
                    // unchanged: measured against this rig it runs from the shoulder line up,
                    // which is what a player expects "headshot" to mean anyway.
                    rend.enabled = false;
                }
                else
                {
                    // Art pack not installed — the capsule is still the body, exactly as before.
                    rend.enabled = !IsOwner;
                    // Dark cap over the headshot band, so "aim for the head" is a place you can
                    // SEE rather than a rule you memorise. Sized from the same headFraction the
                    // damage code uses — if the band is retuned, the paint moves with it.
                    if (!IsOwner) AddHeadCap(body, c);
                }
            }
        }

        if (IsOwner)
        {
            // F9 pulls the camera back so you can watch your own animation. Owner-only, and a
            // view change only — see ThirdPersonView for why shots still leave the eye. Gated
            // on the body existing, since there would be nothing behind you to look at.
            if (GetComponent<PlayerBody>() != null && GetComponent<ThirdPersonView>() == null)
                gameObject.AddComponent<ThirdPersonView>();

            // Apply the weapon picked on the connect screen. Owner-only: it's a local choice,
            // and remote players never render your tracers anyway.
            var wc = GetComponent<WeaponController>();
            if (wc != null) wc.SetLockedWeapon(LoadoutChoice.SelectedSlot);
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

    // Delegates to the shared helper so players and bots stay identical — see HeadCapVisual.
    void AddHeadCap(Transform body, Color bodyColor)
    {
        var wc = GetComponent<WeaponController>();
        HeadCapVisual.Attach(body, wc != null ? wc.headFraction : 0.28f, bodyColor);
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
    public void ReportHit(FishNet.Object.NetworkObject victim, float damage, KillKind kind)
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

        // Recorded BEFORE the damage lands, so if this hit kills them PlayerHealth.Die already
        // knows who to credit. Die is the one place the kill feed is announced from.
        hp.RecordServerAttacker(base.NetworkObject, kind);

        // Point the victim back at us. Sent from the victim's own PlayerNetwork so the TargetRpc
        // reaches their client, with our position as the source.
        //
        // Sent BEFORE the damage is applied, on purpose: if this hit kills, the health SyncVar
        // races this RPC to the victim's client, and the death camera reads whichever attacker
        // record it has when the death lands. Queuing the report first makes the common case
        // arrive in the right order; DeathCam.Retarget covers the rest.
        var victimNet = victim.GetComponent<PlayerNetwork>();
        if (victimNet != null && victim.Owner != null)
            victimNet.ShowDamageFrom(victim.Owner, transform.position, base.NetworkObject);

        hp.Damage(damage);

        // Kill credit, the kill cue and the win check all moved to PlayerHealth.Die: the
        // attacker is recorded above BEFORE the damage, so Die knows the killer for EVERY
        // path — this RPC, rocket splash, whatever comes next — instead of only for hitscan.
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

    // A client's rocket is only a visual on its own machine — every Damage/impulse write it
    // makes is authority-refused. The server spawns the REAL one here (its writes stick,
    // including the knockback that reconcile then delivers to the victims), and relays a
    // visual to everyone who is neither the shooter (already has one) nor the server.
    [FishNet.Object.ServerRpc]
    public void ReportRocket(Vector3 origin, Vector3 dir, int weaponIndex, float damageScale)
    {
        var wc = GetComponent<WeaponController>();
        if (wc == null) return;
        wc.SpawnRocket(origin, dir, weaponIndex, damageScale);
        BroadcastRocket(origin, dir, weaponIndex, damageScale);
    }

    [FishNet.Object.ObserversRpc(ExcludeOwner = true)]
    void BroadcastRocket(Vector3 origin, Vector3 dir, int weaponIndex, float damageScale)
    {
        if (IsServerStarted) return; // the host machine already runs the authoritative copy
        var wc = GetComponent<WeaponController>();
        if (wc != null) wc.SpawnRocket(origin, dir, weaponIndex, damageScale);
    }

    // Rocket pickup collected — the server decides who got it (MatchManager), but ammo lives
    // client-side on the owner's WeaponController, so the grant has to travel to them.
    [FishNet.Object.TargetRpc]
    public void GrantRockets(FishNet.Connection.NetworkConnection conn, int rockets)
    {
        var wc = GetComponent<WeaponController>();
        if (wc != null) wc.GiveRocket(rockets);
    }

    // Lets PlayerHealth.Die (server-side) send the kill cue to whoever killed its player.
    public void NotifyKillConfirmed() => ConfirmKill(Owner);

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
