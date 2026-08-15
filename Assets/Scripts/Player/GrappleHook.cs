using UnityEngine;

// Grapple - BASELINE for every player: no pickup, no loadout slot. It is the traversal verb
// the game is built around, so gating it would mean some players simply are not playing the
// movement game (cf. Warsow's dash, Titanfall's wall-run).
//
// ROPE model. The rope is INEXTENSIBLE and it REELS: it has a length, that length only ever
// shrinks, and the component of your velocity along it is the winch's to decide, not yours.
// Everything perpendicular to the rope is untouched, which is where the swing comes from:
//
//   * aim straight at a surface  -> you are hauled in at the reel rate, steadily
//   * carry lateral speed        -> the constraint turns it into an arc around the anchor
//   * air-strafe still works     -> you pump and steer the arc the whole time (see
//                                   PlayerMotor.AccelerateOnRope)
//   * release                    -> everything you built is yours, nothing is cancelled
//
// This replaced a PULL model (add acceleration toward the anchor, taper it off with speed)
// that playtest called "more a spring than a grapple", and it was: a force proportional to
// nothing but distance-direction, with no length to hold you, stretches and rebounds. It also
// had no rope LENGTH at all, so the gap to the anchor grew freely whenever you outran the
// pull — a rope that gets longer is not a rope.
//
// Two older models are worth remembering. The first was a winch that drove velocity onto the
// rope line with MoveTowards: forcing ALL of velocity to point at the anchor leaves nothing
// tangential, so it could never arc. The second was a rigid pendulum with a fixed length:
// correct physics, but our maps are wide rather than tall, and a pendulum needs height to
// convert into speed, so the arcs were short. The reel is what fixes that here — the rope
// shortening is a source of progress the pendulum did not have, so a swing across flat
// ground still gets you somewhere.
//
// Bounded BOTH ways: attachTime seconds per hook, then a cooldown before the next one. The
// duration cap alone (the old rule) stopped a single hook from being a flight, but nothing
// stopped the NEXT hook starting a tick later — playtest chained hook-jump-hook and left the
// map. A cooldown is the only thing that bounds hooks-per-second.
//
// Integrates with PlayerMotor via ApplyTo(): the motor calls this each fixed tick AFTER
// accel/gravity and BEFORE the move, so the motor stays the single mover. This shapes
// velocity only and never touches the transform.
[RequireComponent(typeof(LineRenderer))]
public class GrappleHook : MonoBehaviour
{
    [Header("Refs")]
    public Transform aim;             // camera transform (ray origin + direction)

    [Header("Grapple")]
    public LayerMask grappleMask = ~0;
    [Tooltip("Max anchor distance.")]
    public float maxRange = 55f;
    [Tooltip("Target closing speed of the reel (m/s). AUTOMATIC — there is no reel button to " +
             "hold, because the rope is the verb and asking for a second held input to make it " +
             "do its job was the chord problem in a different hat. Sits above run speed (9) so a " +
             "hook is always progress, and below the slide ceiling (16) so it cannot out-earn " +
             "movement you did yourself.")]
    public float reelSpeed = 14f;
    [Tooltip("How hard the winch may pull to REACH reelSpeed (m/s^2). This is the fix for " +
             "playtest's 'slingshotting too much', and the reason matters. Setting closing speed " +
             "to the reel rate outright — as the first version did — is an energy pump on a " +
             "swing: your radial velocity rotates into a TANGENTIAL one as you travel around the " +
             "anchor, and then the next tick re-injects a full reel's worth of radial on top of " +
             "it. Sideways speed compounds every tick and you leave the swing far faster than " +
             "you entered it. Accelerating instead bounds what one tick can add, so a swing " +
             "gains speed the way a pendulum does rather than the way a rocket does.")]
    public float reelAccel = 60f;
    [Tooltip("Distance over which the reel eases off to nothing as you approach the anchor. " +
             "Without it the winch is at full strength when the rope is metres long, and a rope " +
             "that short whips you around the anchor faster the closer you get (the swing rate " +
             "is speed/length) — playtest: 'keep reeling at the endpoint and it swings very " +
             "weirdly'. Measured from arriveDistance outward.")]
    public float reelEaseDistance = 5f;
    [Tooltip("Swing rate, in radians per second, at which the rope simply lets go. The other " +
             "half of the endpoint fix: once you are whipping around an anchor rather than " +
             "swinging past it, no tuning of the pull makes the next second read as anything " +
             "but a glitch, and the honest answer is that the rope has done its job. 3 rad/s is " +
             "roughly a half-turn a second — far above a normal swing (a 20m rope at 25 m/s is " +
             "1.25) and unmistakable when it happens.")]
    public float maxSwingRate = 3f;
    [Tooltip("Reel multiplier while JUMP is held — 'pull yourself in'. One held button on a " +
             "key you already have under your thumb, and it is the only speed control the rope " +
             "gives you, so the choice is legible: swing wide, or haul in.")]
    public float fastReelScale = 1.7f;
    [Tooltip("Reel rate on a hooked PLAYER, replacing reelSpeed for actor hooks. Reeling in on " +
             "a person is not the same act as reeling in on a wall: at the map's rate the rope " +
             "was a free assassination — hook, get dragged in at crash speed, and the 3.5m " +
             "one-hit knife did the rest, with the victim given neither time nor a mistake to " +
             "punish. 8 is under run speed, so the rope alone can no longer catch someone who " +
             "is moving; closing on them costs speed you brought yourself. Tangential speed is " +
             "untouched either way, so the tether-and-swing around a target still works.")]
    public float actorReelSpeed = 8f;
    [Tooltip("Speed added along your current heading when you RELEASE a hook you were moving " +
             "on. 5 -> 2 -> 0 across playtests, and 0 is where it belongs while the complaint " +
             "is that releases fling too far: under a constraint rope the arc ITSELF is the " +
             "slingshot, so an explicit bonus on top was paying twice for the same skill. Left " +
             "as a knob rather than deleted — if releases end up feeling like nothing once the " +
             "energy pump is gone, 1 to 2 is the range to try.")]
    public float releaseBoost = 0f;
    [Tooltip("How much of the release boost is allowed to go UPWARD. At the top of an arc your " +
             "heading is straight up, so an unbiased boost put its whole magnitude into " +
             "altitude and fired you above everything in the map with nothing left to hook. " +
             "Damping only the vertical share turns a swing into DISTANCE, which is what these " +
             "wide maps actually want, while still letting you gain some height.")]
    [Range(0f, 1f)] public float releaseBoostUpScale = 0.2f;
    [Tooltip("Minimum speed before a release is boosted at all, so letting go while nearly " +
             "still is not a free jump.")]
    public float releaseBoostMinSpeed = 6f;
    [Tooltip("Seconds a single hook lasts before it lets go on its own. This is the balance " +
             "lever in place of a cooldown: long enough for a full swing and a slingshot, " +
             "short enough that the rope is not a way to live in the air. 2.5 -> 1.5 once " +
             "holding the button re-hooks automatically: short frequent hooks read as a rhythm " +
             "you play, where one long one was just a ride you sat through.")]
    public float attachTime = 1.8f;
    [Tooltip("Auto-release when this close to the anchor, so you launch past instead of " +
             "splatting. 2.2 -> 3: the last metre of a reel is where the rope is shortest and " +
             "therefore where a swing is fastest and least readable, and nothing good happens " +
             "in it.")]
    public float arriveDistance = 3f;
    [Tooltip("Seconds the rope is unavailable after ANY hook ends, however it ended. The old " +
             "0.12s refire delay only stopped the rope re-grabbing on the very next tick, " +
             "which meant hooks-per-second was bounded by nothing: playtest chained " +
             "hook-jump-hook and crossed maps in one breath. 2s is long enough that a hook is " +
             "a decision about WHERE, not a button you hold down. Counts down in dt rather " +
             "than against Time.time, so a reconciling client replays it exactly.")]
    public float cooldown = 2f;

    [Header("Hookweaver passive")]
    // The movement pick, in a set where wall jump, Momentum and the grapple itself are all
    // baseline and DoubleJump was the only choice that touched movement at all. Built entirely
    // out of the two numbers already tuned above rather than a new verb: more rope, sooner.
    [Tooltip("attachTime while Hookweaver is equipped. 2.6 against the baseline 1.8 is roughly " +
             "one more swing per hook — enough to chain across a gap that otherwise needs the " +
             "floor in between.")]
    public float hookweaverAttachTime = 2.6f;
    [Tooltip("cooldown while Hookweaver is equipped. 1.2 against the baseline 2 is one extra " +
             "hook roughly every three, which is what the pick is FOR — more rope, sooner — " +
             "without handing back the unbounded chaining the cooldown exists to stop.")]
    public float hookweaverCooldown = 1.2f;

    // Resolved once in Awake and again whenever the loadout changes, never asked per tick:
    // PassiveLoadout raises Changed for exactly this, and a per-tick Has() inside the sim
    // would be a hash lookup on the hot path for a value that changes at most once a match.
    bool hasHookweaver;
    PassiveLoadout passives;

    float AttachTime => hasHookweaver ? hookweaverAttachTime : attachTime;
    float Cooldown => hasHookweaver ? hookweaverCooldown : cooldown;

    [Tooltip("How much of releaseBoost you keep when letting go of a hook that was on a " +
             "PLAYER. 0 by default: the slingshot exists so a well-timed release converts a " +
             "swing into distance, and firing that same boost INTO the person you are about to " +
             "stab is the cheap version of the same skill. Map hooks are unaffected.")]
    [Range(0f, 1f)] public float actorReleaseBoostScale = 0f;

    [Tooltip("Does a melee swing drop the rope? OFF since melee became universal. The rule " +
             "existed to price hook-into-knife, where a 28 m/s approach met a ONE-HIT weapon " +
             "and the victim got no mistake to punish. A quick melee is a 70-damage tap that " +
             "kills nobody from full health, so there is no execute left to tax — and taxing " +
             "it anyway would mean every panic swing on a rope cost you the swing. Kept as a " +
             "switch because the day melee gets heavier, this is the first lever to pull.")]
    public bool meleeDropsHook = false;
    [Tooltip("Seconds the rope stays unavailable after a swing, on top of the normal cooldown " +
             "that a detach already starts. Kept as its own number because a melee detach must " +
             "commit you even if the cooldown were tuned down to nothing: you arrive with a " +
             "knife and no rope to leave on, which is the price of hook-into-knife.")]
    public float meleeHookLockout = 0.6f;

    [Tooltip("May the rope take hold of a PLAYER or a bot? On, the anchor rides them: hook a " +
             "runner and you are pulled along their escape rather than to the spot they left, " +
             "which is what turns the grapple from pure traversal into a way to close. It also " +
             "gives the knife loadout its opening — 28 m/s of closing speed into a 3.5m " +
             "one-hit-kill — and gives everyone else a reason to break line of sight rather " +
             "than simply outrun the rope. Off, the rope passes THROUGH actors entirely rather " +
             "than sticking a dead anchor in the air where someone used to be.")]
    public bool canHookActors = true;

    [Header("Visual")]
    public float ropeWidth = 0.06f;
    public Color ropeColor = new Color(0.2f, 0.9f, 1f);
    [Tooltip("Colour the rope turns as the hook runs out. The rope is already in the corner of " +
             "your eye while you swing, so it is the cheapest place to put the timer — no HUD " +
             "element to look away for.")]
    public Color ropeExpiringColor = new Color(1f, 0.35f, 0.25f);
    [Tooltip("Fraction of the hook's life over which the rope shifts to the expiring colour and " +
             "starts to thin. 0.45 means the warning begins a little before halfway, which is " +
             "roughly when you must decide whether to commit to the swing or bail.")]
    [Range(0.05f, 1f)] public float ropeWarnFraction = 0.45f;

    public bool Attached { get; private set; }
    public Vector3 Anchor { get; private set; }
    // Seconds of hook left. Part of the SIMULATION state: it counts down every attached tick,
    // so a reconciling client that did not restore it would let go at the wrong moment.
    // Carried in MotorState, which is why the field there is reused rather than removed.
    public float TimeLeft { get; private set; }

    // Current rope length. SIMULATION state: the constraint is built from it, it only ever
    // shrinks, and a client that replayed without it would be constrained to a different
    // sphere than the server and drift every tick of a swing.
    public float RopeLength { get; private set; }

    // Seconds until the rope may be fired again. Sim state for the same reason TimeLeft is:
    // it decides whether a replayed press attaches at all.
    public float CooldownLeft { get; private set; }

    // 0..1 of the hook's life remaining. The one number the cues are built from, so the rope,
    // the HUD and the warning tone can never disagree about how long you have.
    public float TimeLeft01 => AttachTime > 0f ? Mathf.Clamp01(TimeLeft / AttachTime) : 0f;

    // 0..1 of the cooldown still to wait. Drives the HUD's recharge bar.
    public float Cooldown01 => Cooldown > 0f ? Mathf.Clamp01(CooldownLeft / Cooldown) : 0f;

    // Snapshot / restore for reconciliation — the motor folds these into MotorState so a
    // corrected client replays with the rope in exactly the state the server had.
    public void GetNetState(out bool attached, out Vector3 anchor, out bool held, out float timeLeft,
        out float ropeLength, out float cooldownLeft)
    {
        attached = Attached;
        anchor = Anchor;
        held = wasHeld;
        timeLeft = TimeLeft;
        ropeLength = RopeLength;
        cooldownLeft = CooldownLeft;
    }

    public void SetNetState(bool attached, Vector3 anchor, bool held, float timeLeft,
        float ropeLength, float cooldownLeft)
    {
        Attached = attached;
        Anchor = anchor;
        wasHeld = held;
        TimeLeft = timeLeft;
        RopeLength = ropeLength;
        CooldownLeft = cooldownLeft;
        // A reconcile that says "not attached" also ends any target ride. Leaving the target
        // set would let the next tick re-derive an anchor for a hook the server says is over.
        if (!attached) { anchorTarget = null; anchorHealth = null; }
    }

    LineRenderer line;
    bool wasHeld;
    const float center = 1f;          // rope reference = feet + up*center (capsule middle)

    // Set when the hook took hold of a living target instead of the map. The anchor is then
    // recomputed every tick from their position, so the rope rides them.
    //
    // Deliberately NOT in MotorState. The reconcile snapshot carries the world Anchor, which
    // is what it always carried, and both sides re-derive the target from their own raycast
    // exactly as they already re-derive a surface hit — the server's raycast comes from the
    // same InputCmd, so it lands on the same person. A target REFERENCE cannot be replicated
    // as a Vector3 anyway, and adding an object id to MotorState to carry it would put a
    // network lookup inside the movement sim for a difference that reconcile already erases.
    Transform anchorTarget;
    // World-space, captured at attach and held fixed: the anchor rides the target's POSITION
    // but not their rotation. Storing it in their local space instead means a fast yaw flick
    // whips the rope around them, and a spin becomes a way to sling whoever hooked you.
    Vector3 anchorOffset;
    // Present only when the target is a player: bots and practice targets have Health, which
    // has no liveness to ask about. Used to drop the rope the moment they die.
    PlayerHealth anchorHealth;
    readonly RaycastHit[] hits = new RaycastHit[12];

    // Counts down in dt rather than comparing Time.time, so it survives a replay: the melee
    // bit lives in InputCmd, so a reconciling client re-derives this lock from the same input
    // the server used. It is not in MotorState — a correction can leave it up to a fraction of
    // a second out of step, which costs at most one denied or allowed hook during the snap.
    float hookLockLeft;

    // True while the rope is on a player or bot rather than the map. Local knowledge — see
    // anchorTarget — so it is only ever right on the machine that fired the hook.
    public bool HookedActor => anchorTarget != null;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        if (aim == null)
        {
            var c = GetComponentInChildren<Camera>();
            if (c != null) aim = c.transform;
        }
        passives = GetComponent<PassiveLoadout>();
        if (passives != null) passives.Changed += ApplyPassives;
        ApplyPassives();
        // NOTE: the player layer is deliberately NOT stripped from grappleMask any more. It
        // used to be — "never grapple ourselves" — but that excluded the whole LAYER, which is
        // every other player too, so hooking a person was impossible by construction. Self is
        // now excluded per-hit by transform root instead, the same fix WeaponController's
        // hitscan already uses for the same reason.
        SetupLine();
    }

    void OnDestroy()
    {
        if (passives != null) passives.Changed -= ApplyPassives;
    }

    // Mirrors PlayerMotor.ApplyPassives: the synced pick is the source of truth, and both
    // machines resolve the same booleans from it, so a hook lasts the same length on the
    // server as it does on the client that fired it.
    void ApplyPassives()
    {
        hasHookweaver = passives != null && passives.Has(PassiveType.Hookweaver);
    }

    void SetupLine()
    {
        line.positionCount = 2;
        line.widthMultiplier = ropeWidth;
        line.useWorldSpace = true;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ropeColor);
        mat.color = ropeColor;
        line.material = mat;
        line.startColor = line.endColor = ropeColor;
        line.enabled = false;
    }

    Vector3 PullPoint => transform.position + Vector3.up * center;
    Vector3 RopeStart => aim != null ? aim.position - aim.up * 0.25f : PullPoint;

    // Called by PlayerMotor each fixed tick (after accel/gravity, before the move).
    //
    // aimOrigin/aimDir are passed in and derived from the INPUT COMMAND, never from the camera
    // transform. That distinction is the whole reason this is reliable over a network: the
    // server disables MouseLook on non-owned players, so their camera never rotates there. A
    // transform-based raycast made the server aim somewhere else entirely, fail to hit, and
    // then reconcile a "not attached" state back — the grapple tearing off at random.
    // `blocked` is the motor telling us the previous move was stopped by geometry.
    public void ApplyTo(ref Vector3 velocity, Vector3 aimOrigin, Vector3 aimDir, float dt,
        bool grappleHeld, bool blocked, bool meleePressed, bool reelHeld)
    {
        bool held = grappleHeld;

        if (hookLockLeft > 0f) hookLockLeft -= dt;
        if (CooldownLeft > 0f) CooldownLeft = Mathf.Max(0f, CooldownLeft - dt);

        // A swing COMMITS. Detached without the release boost on purpose: the slingshot is the
        // reward for timing a release, not something a knife hands you on the way in.
        if (meleeDropsHook && meleePressed)
        {
            hookLockLeft = meleeHookLockout;
            if (Attached) Detach();
        }

        // RELEASING A LIVE HOOK — and only a live one. The Attached guard is the whole point:
        // Detach() starts the cooldown, so letting go of a button that is not holding anything
        // charged you a full 2s for nothing. Two ways that bit, both reported as "the cooldown
        // resets": a hook that expired on its own started a cooldown, and then releasing the
        // button a second later started a SECOND one; and a press that simply missed — aimed
        // past the map, out of range — locked the rope as though it had swung.
        if (!held && wasHeld && Attached)
        {
            // Deliberate release is rewarded, and timing it is the skill. The vertical share
            // is damped (see releaseBoostUpScale) so a well-timed release reads as a slingshot
            // across the map rather than a launch into empty sky.
            // Scaled to nothing off an actor hook by default — see actorReleaseBoostScale.
            // Read before Detach, which is what clears anchorTarget.
            float boostScale = anchorTarget != null ? actorReleaseBoostScale : 1f;
            if (boostScale > 0f && velocity.magnitude >= releaseBoostMinSpeed)
            {
                Vector3 boost = velocity.normalized * (releaseBoost * boostScale);
                boost.y *= releaseBoostUpScale;
                velocity += boost;
            }
            Detach();
        }
        else if (held)
        {
            // Hold to keep hooking. A press attaches immediately; continuing to hold re-hooks
            // the moment the cooldown clears, so catching the next surface never depends on
            // re-tapping at exactly the right instant in mid-air — the timing that matters is
            // the cooldown, and that one is visible on the HUD.
            if (!Attached && hookLockLeft <= 0f && CooldownLeft <= 0f)
                TryAttach(aimOrigin, aimDir);
        }
        wasHeld = held;

        if (!Attached) return;

        // The hook has a lifetime. Running out releases exactly like letting go does: whatever
        // you built is kept, nothing is cancelled.
        TimeLeft -= dt;
        if (TimeLeft <= 0f) { Detach(); return; }

        // A hook on a living target RIDES them: the anchor is re-derived from where they are
        // now, not from where they were when the rope landed. That one line is the whole
        // difference between hooking a person and hooking the air they just vacated — a
        // runner now drags you along their own escape, and their slower reel rate means they
        // still gain on you if they were faster to begin with.
        if (anchorTarget != null)
        {
            // Dead or despawned mid-swing. Dropping the rope is the honest outcome: holding
            // the last known point leaves you flying at a corpse, and holding a destroyed
            // transform is an NRE one tick later.
            if (!anchorTarget.gameObject.activeInHierarchy
                || (anchorHealth != null && !anchorHealth.Alive)) { Detach(); return; }
            Anchor = anchorTarget.position + anchorOffset;
        }

        Vector3 toAnchor = Anchor - PullPoint;
        float dist = toAnchor.magnitude;
        if (dist <= arriveDistance) { Detach(); return; }         // arrived -> launch past
        Vector3 dir = toAnchor / dist;   // points TOWARD the anchor

        // Nothing at all while the motor is jammed against geometry — and the rope does not
        // shorten either. A constraint that keeps winching on a player pinned against a wall
        // stores up a correction that fires the instant they slide free.
        if (blocked) return;

        // The rope only ever gets SHORTER, and it tracks where you actually are: any slack you
        // swing into is kept, never given back. Note it is NOT driven down by the reel rate —
        // the reel acts on velocity, and letting the length run ahead of the player would make
        // ConstrainPosition drag them along by the transform, which reads as a stutter rather
        // than a pull.
        RopeLength = Mathf.Min(RopeLength, dist);

        // TENSION. An inextensible rope cannot let you move away from the anchor, so the
        // outward part of your velocity is removed. This only ever takes energy out, which is
        // what a rope does — the swing is what remains once the outward half is gone.
        float radial = Vector3.Dot(velocity, dir);   // + = closing on the anchor
        if (radial < 0f) { velocity -= dir * radial; radial = 0f; }

        // Whipping rather than swinging: let go. Past a few rad/s the arc is faster than the
        // camera can narrate and every frame of it looks like a bug.
        Vector3 tangential = velocity - dir * radial;
        if (tangential.magnitude > maxSwingRate * dist) { Detach(); return; }

        // REEL. An acceleration toward the reel speed, never an assignment to it — see
        // reelAccel for why the difference is the whole slingshot problem — and eased to
        // nothing over the last few metres, where a short rope turns any leftover speed into
        // a whip (see reelEaseDistance). Nothing happens if you are already closing faster
        // than the winch: a hook fired while you fly at the anchor must not brake you.
        float reel = (anchorTarget != null ? actorReelSpeed : reelSpeed)
                     * (reelHeld ? fastReelScale : 1f)
                     * Mathf.Clamp01((dist - arriveDistance) / Mathf.Max(0.01f, reelEaseDistance));
        if (radial < reel) velocity += dir * Mathf.Min(reelAccel * dt, reel - radial);
    }

    // Hold the player on (or inside) the rope sphere after the motor has moved them. The
    // velocity constraint above keeps the distance right in open air, but a swing is a chord
    // across an arc and collisions can stop the move outright, so without this the rope creeps
    // longer over a long swing — the exact failure the length is here to prevent.
    // Returns whether it actually moved the position, so the motor knows to depenetrate again.
    public bool ConstrainPosition(ref Vector3 pos)
    {
        if (!Attached) return false;
        Vector3 to = Anchor - (pos + Vector3.up * center);
        float d = to.magnitude;
        if (d <= RopeLength || d < 0.001f) return false;
        pos += to / d * (d - RopeLength);
        return true;
    }

    // Everything a life must not inherit from the last one. Called by PlayerHealth both when
    // you die and when you respawn.
    //
    // On DEATH, because a hook survives dying otherwise: the motor is frozen, so nothing ticks
    // the rope down, and the corpse goes on rendering a line to an anchor across the map.
    // On RESPAWN, because the stale rope is worse than cosmetic — the length is still the one
    // you had when you died, the anchor is wherever you died, and the first tick of the new
    // life sees a 10m rope with a 60m gap and drags you back across the map to close it.
    //
    // NOT routed through Detach(), deliberately: that starts the cooldown, and a new life is
    // not a swing you just took. You spawn with the rope ready.
    public void ResetForRespawn()
    {
        Attached = false;
        anchorTarget = null;
        anchorHealth = null;
        RopeLength = 0f;
        TimeLeft = 0f;
        CooldownLeft = 0f;
        hookLockLeft = 0f;
        wasHeld = false;
        if (line != null) line.enabled = false;
    }

    // Single exit point, so every way a hook can end also starts the cooldown. Missing one
    // would leave a hole a held button could chain through.
    void Detach()
    {
        Attached = false;
        anchorTarget = null;      // or the next hook keeps riding the last person you held
        anchorHealth = null;
        CooldownLeft = Cooldown;
    }

    void TryAttach(Vector3 origin, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;

        // RaycastAll rather than Raycast: the ray starts inside our own capsule, so the first
        // thing it can hit is us. Taking the nearest hit that is not our own root also lets an
        // actor hit be SKIPPED when canHookActors is off, so the rope reaches the wall behind
        // them instead of refusing to fire.
        int n = Physics.RaycastNonAlloc(origin, dir, hits, maxRange, grappleMask,
                                        QueryTriggerInteraction.Ignore);
        Transform self = transform.root;
        float bestDist = float.MaxValue;
        int best = -1;
        Transform bestActor = null;

        for (int i = 0; i < n; i++)
        {
            Transform root = hits[i].collider.transform.root;
            if (root == self) continue;                       // our own capsule
            if (hits[i].distance >= bestDist) continue;

            // Anything with hit points is a thing that can move and can die, which is exactly
            // the set that needs a riding anchor: players, bots, the practice targets.
            var actor = hits[i].collider.GetComponentInParent<IDamageable>();
            Transform actorTf = actor is Component c ? c.transform : null;
            if (actorTf != null && !canHookActors) continue;  // rope passes through them

            bestDist = hits[i].distance;
            best = i;
            bestActor = actorTf;
        }

        if (best < 0) return;

        Attached = true;
        Anchor = hits[best].point;
        anchorTarget = bestActor;
        anchorOffset = bestActor != null ? hits[best].point - bestActor.position : Vector3.zero;
        anchorHealth = bestActor != null ? bestActor.GetComponentInParent<PlayerHealth>() : null;
        TimeLeft = AttachTime;
        // Measured from the PULL POINT, not from the aim ray's origin: the ray starts at the
        // eye and the constraint acts on the capsule's middle, and seeding the length with the
        // eye's distance would put the player a head-height outside their own rope on tick one.
        RopeLength = (Anchor - PullPoint).magnitude;
    }

    void LateUpdate()
    {
        if (Attached)
        {
            line.enabled = true;
            line.SetPosition(0, RopeStart);
            // Drawn from the target's CURRENT position, not the anchor the fixed tick left
            // behind: rendering runs between physics ticks, and a rope that lags a tick behind
            // a sprinting target visibly detaches from them and snaps back every frame.
            line.SetPosition(1, anchorTarget != null ? anchorTarget.position + anchorOffset
                                                     : Anchor);

            // Fray as it runs out: colour shifts toward the warning tint and the rope thins.
            // Two channels rather than one so it still reads for a colour-blind player — the
            // same reasoning as the armour pickup's size difference.
            float t = 1f - Mathf.Clamp01(TimeLeft01 / Mathf.Max(0.01f, ropeWarnFraction));
            Color c = Color.Lerp(ropeColor, ropeExpiringColor, t);
            line.startColor = line.endColor = c;
            if (line.material.HasProperty("_BaseColor")) line.material.SetColor("_BaseColor", c);
            line.material.color = c;
            line.widthMultiplier = ropeWidth * Mathf.Lerp(1f, 0.4f, t);
        }
        else if (line.enabled)
        {
            line.enabled = false;
        }
    }
}
