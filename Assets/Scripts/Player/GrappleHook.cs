using UnityEngine;

// Grapple - BASELINE for every player: no pickup, no loadout slot. It is the traversal verb
// the game is built around, so gating it would mean some players simply are not playing the
// movement game (cf. Warsow's dash, Titanfall's wall-run).
//
// PULL model (Pathfinder / Apex). The rope ADDS acceleration toward the anchor and never
// replaces the velocity you already had. That one property is the whole feel:
//
//   * aim straight at a surface  -> the pull dominates and you get reeled in
//   * carry lateral speed        -> the same pull acts as centripetal force and you CURVE
//                                   around the anchor, which is the grapple-swing
//   * air-strafe still works     -> you steer the arc the whole time
//   * release                    -> everything you built is yours, nothing is cancelled
//
// Two earlier models are worth remembering, because each failed for an instructive reason.
// The first was a winch that drove velocity onto the rope line with MoveTowards: forcing
// velocity to POINT at the anchor leaves nothing tangential, so it could never arc. The
// second was a rigid pendulum that constrained distance: correct physics, but our maps are
// wide rather than tall, and a pendulum needs height to convert into speed - so the arcs were
// short and it fought every wall it touched. Adding force instead of dictating velocity gives
// both behaviours out of one rule and needs no rope length in the reconcile state.
//
// Bounded by TIME, not by a cooldown: attachTime seconds per hook, then it lets go. Apex uses
// a long cooldown because a grapple with no limit is a fly-anywhere button; a duration cap
// does the same job without introducing the game's first ability timer.
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
    [Tooltip("Pull acceleration toward the anchor (m/s^2), ADDED to your velocity rather than " +
             "replacing it. This is the entire model: with lateral speed it acts as centripetal " +
             "force and curves you around the anchor; aimed straight at a wall it simply reels " +
             "you in. ONE strength, no modifier key — the reel briefly had its own button, but " +
             "the pull IS the grapple, and asking for two held inputs to use one verb was the " +
             "chord problem again in a different hat.")]
    public float pullAccel = 55f;
    [Tooltip("Ceiling on how fast you may CLOSE on the anchor. Without it a long grapple " +
             "accelerates into the wall for its whole flight and ends in a splat. Tangential " +
             "speed is never capped by this - only the component pointing at the anchor.")]
    public float maxClosingSpeed = 28f;

    [Tooltip("Speed at which the pull starts weakening. MUST sit above everything you can " +
             "reach without the rope — ground 9, slide 16, bhop 16.2 — or the grapple is in " +
             "falloff during normal play and feels limp. It was briefly 8, i.e. BELOW run " +
             "speed, and the result was exactly that: 90% pull while walking, 20% after a " +
             "bhop. 16 means the pull is at full strength for every speed you can build " +
             "yourself, and only tapers past it.")]
    public float pullFalloffStart = 16f;
    [Tooltip("Speed at which the pull has faded to nothing. THIS is what stops the grapple " +
             "flattening the whole speed economy. " +
             "Measured before this existed: one hook from a DEAD STOP peaked near 50 m/s, " +
             "against a slide ceiling of 16 and a bhop ceiling of 16.2 — and arriving at 24 " +
             "instead of standing still improved the peak by 7%. In other words the pull " +
             "overwhelmed whatever momentum you brought, so chaining hooks and building speed " +
             "beforehand were both worth almost nothing. That is the exact opposite of 'more " +
             "movement -> more speed'. " +
             "Diminishing returns rather than a hard cap, which is the same rule the dash " +
             "already uses: the grapple can always save you when slow, and above this it stops " +
             "adding, so how fast you LEAVE a swing is decided by how fast you entered it.")]
    public float pullFalloffEnd = 30f;
    [Tooltip("Speed added along your current heading when you RELEASE a hook you were moving " +
             "on. The slingshot, made explicit: without it, letting go is a non-event and the " +
             "only way to leave a swing is to run the clock out. With it, release timing is a " +
             "skill you can practise.")]
    public float releaseBoost = 5f;
    [Tooltip("How much of the release boost is allowed to go UPWARD. At the top of an arc your " +
             "heading is straight up, so an unbiased boost put its whole magnitude into " +
             "altitude and fired you above everything in the map with nothing left to hook. " +
             "Damping only the vertical share turns a swing into DISTANCE, which is what these " +
             "wide maps actually want, while still letting you gain some height.")]
    [Range(0f, 1f)] public float releaseBoostUpScale = 0.35f;
    [Tooltip("Minimum speed before a release is boosted at all, so letting go while nearly " +
             "still is not a free jump.")]
    public float releaseBoostMinSpeed = 6f;
    [Tooltip("Seconds a single hook lasts before it lets go on its own. This is the balance " +
             "lever in place of a cooldown: long enough for a full swing and a slingshot, " +
             "short enough that the rope is not a way to live in the air. 2.5 -> 1.5 once " +
             "holding the button re-hooks automatically: short frequent hooks read as a rhythm " +
             "you play, where one long one was just a ride you sat through.")]
    public float attachTime = 1.8f;
    [Tooltip("Auto-release when this close to the anchor, so you launch past instead of splatting.")]
    public float arriveDistance = 2.2f;
    [Tooltip("Seconds before a HELD button may hook again after the previous hook ended. The " +
             "grapple used to need a full release-and-press to re-fire, so once a hook expired " +
             "in mid-air you were holding a dead button until you thought to let go — which is " +
             "precisely when you are trying to catch the floor. Holding now re-hooks on its " +
             "own; this delay is only here so the instant a hook ends you do not immediately " +
             "grab the surface you were about to fly past.")]
    public float refireDelay = 0.12f;

    [Tooltip("Ceiling on how fast you may CLOSE on a hooked PLAYER, replacing maxClosingSpeed " +
             "for actor hooks. Separate because reeling in on a person is not the same act as " +
             "reeling in on a wall: at the map's 28 the rope was a free assassination — hook, " +
             "get dragged in at crash speed, and the 3.5m one-hit knife did the rest, with the " +
             "victim given neither time nor a mistake to punish. 16 is the slide ceiling, so " +
             "the rope alone can no longer catch someone who is already moving; closing on " +
             "them costs speed you brought yourself. Tangential speed is still uncapped, so " +
             "the tether-and-swing around a target is untouched. 0 = use maxClosingSpeed.")]
    public float actorMaxClosingSpeed = 16f;
    [Tooltip("How much of releaseBoost you keep when letting go of a hook that was on a " +
             "PLAYER. 0 by default: the slingshot exists so a well-timed release converts a " +
             "swing into distance, and firing that same boost INTO the person you are about to " +
             "stab is the cheap version of the same skill. Map hooks are unaffected.")]
    [Range(0f, 1f)] public float actorReleaseBoostScale = 0f;

    [Tooltip("Does swinging a melee weapon drop the rope? The hook buys you the approach and " +
             "the swing is then taken on foot, which is the price of hook-into-knife: you " +
             "arrive committed, with no rope to leave on if you miss. Costs the slingshot too " +
             "— a melee detach never pays the release boost.")]
    public bool meleeDropsHook = true;
    [Tooltip("Seconds the rope stays unavailable after a swing. refireDelay's 0.12 is far too " +
             "short to read as a commitment — a held button would have you airborne again " +
             "before the knife finished its 0.75s cycle. 0.6 leaves you standing in the fight " +
             "you started for about as long as it takes to be answered.")]
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

    // 0..1 of the hook's life remaining. The one number the cues are built from, so the rope,
    // the HUD and the warning tone can never disagree about how long you have.
    public float TimeLeft01 => attachTime > 0f ? Mathf.Clamp01(TimeLeft / attachTime) : 0f;

    // Snapshot / restore for reconciliation — the motor folds these into MotorState so a
    // corrected client replays with the rope in exactly the state the server had.
    public void GetNetState(out bool attached, out Vector3 anchor, out bool held, out float timeLeft)
    {
        attached = Attached;
        anchor = Anchor;
        held = wasHeld;
        timeLeft = TimeLeft;
    }

    public void SetNetState(bool attached, Vector3 anchor, bool held, float timeLeft)
    {
        Attached = attached;
        Anchor = anchor;
        wasHeld = held;
        TimeLeft = timeLeft;
        // A reconcile that says "not attached" also ends any target ride. Leaving the target
        // set would let the next tick re-derive an anchor for a hook the server says is over.
        if (!attached) { anchorTarget = null; anchorHealth = null; }
    }

    LineRenderer line;
    bool wasHeld;
    float refireAt;   // earliest time a held button may hook again
    const float center = 1f;          // pull reference = feet + up*center (capsule middle)

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
        // NOTE: the player layer is deliberately NOT stripped from grappleMask any more. It
        // used to be — "never grapple ourselves" — but that excluded the whole LAYER, which is
        // every other player too, so hooking a person was impossible by construction. Self is
        // now excluded per-hit by transform root instead, the same fix WeaponController's
        // hitscan already uses for the same reason.
        SetupLine();
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
        bool grappleHeld, bool blocked, bool meleePressed)
    {
        bool held = grappleHeld;

        if (hookLockLeft > 0f) hookLockLeft -= dt;

        // A swing COMMITS. Detached without the release boost on purpose: the slingshot is the
        // reward for timing a release, not something a knife hands you on the way in.
        if (meleeDropsHook && meleePressed)
        {
            hookLockLeft = meleeHookLockout;
            if (Attached) Detach(dt);
        }

        if (!held && wasHeld)
        {
            // Deliberate release is rewarded, and timing it is the skill. The vertical share
            // is damped (see releaseBoostUpScale) so a well-timed release reads as a slingshot
            // across the map rather than a launch into empty sky.
            // Scaled to nothing off an actor hook by default — see actorReleaseBoostScale.
            // Read before Detach, which is what clears anchorTarget.
            float boostScale = anchorTarget != null ? actorReleaseBoostScale : 1f;
            if (Attached && boostScale > 0f && velocity.magnitude >= releaseBoostMinSpeed)
            {
                Vector3 boost = velocity.normalized * (releaseBoost * boostScale);
                boost.y *= releaseBoostUpScale;
                velocity += boost;
            }
            Detach(dt);
        }
        else if (held)
        {
            // Hold to keep hooking. A press attaches immediately; continuing to hold re-hooks
            // as soon as the refire delay has passed, which is what makes chaining possible —
            // fly, and the rope takes the next surface that comes into range without you
            // having to re-tap in mid-air.
            if (!Attached && hookLockLeft <= 0f && (!wasHeld || Time.time >= refireAt))
                TryAttach(aimOrigin, aimDir);
        }
        wasHeld = held;

        if (!Attached) return;

        // The hook has a lifetime. Running out releases exactly like letting go does: whatever
        // you built is kept, nothing is cancelled.
        TimeLeft -= dt;
        if (TimeLeft <= 0f) { Detach(dt); return; }

        // A hook on a living target RIDES them: the anchor is re-derived from where they are
        // now, not from where they were when the rope landed. That one line is the whole
        // difference between hooking a person and hooking the air they just vacated — a
        // runner now drags you along their own escape, and the pull's existing falloff means
        // they still gain on you if they were faster to begin with.
        if (anchorTarget != null)
        {
            // Dead or despawned mid-swing. Dropping the rope is the honest outcome: holding
            // the last known point leaves you flying at a corpse, and holding a destroyed
            // transform is an NRE one tick later.
            if (!anchorTarget.gameObject.activeInHierarchy
                || (anchorHealth != null && !anchorHealth.Alive)) { Detach(dt); return; }
            Anchor = anchorTarget.position + anchorOffset;
        }

        Vector3 toAnchor = Anchor - PullPoint;
        float dist = toAnchor.magnitude;
        if (dist <= arriveDistance) { Detach(dt); return; }       // arrived -> launch past
        Vector3 dir = toAnchor / dist;   // points TOWARD the anchor

        // Nothing at all while the motor is jammed against geometry. A pull that keeps adding
        // velocity into a surface banks force for the instant you slide free, which is the
        // bounce the rigid version had; there is no reason to repeat it here.
        if (blocked) return;

        // Diminishing returns with speed. Full pull while slow, nothing once you are already
        // fast — so the rope is a way to GET moving and to steer, never a way to out-earn the
        // movement you did yourself. Uses total speed rather than the closing component on
        // purpose: it was uncapped tangential speed that ran away.
        float speed = velocity.magnitude;
        float falloff = 1f - Mathf.Clamp01(
            (speed - pullFalloffStart) / Mathf.Max(0.01f, pullFalloffEnd - pullFalloffStart));

        // ADD, never assign. Your existing velocity is untouched, so the tangential part of it
        // survives and this becomes centripetal force: the curve is emergent, not scripted.
        Vector3 pull = dir * (pullAccel * falloff) * dt;

        // Cap only the CLOSING component. Left alone, a long grapple accelerates at the anchor
        // for its whole flight and arrives at a speed that reads as a crash; meanwhile capping
        // total speed would defeat the point, because the speed you are here for is sideways.
        // A person gets a tighter ceiling than a wall does (actorMaxClosingSpeed): the wall is
        // not trying to survive you.
        float closeCap = anchorTarget != null && actorMaxClosingSpeed > 0f
                       ? actorMaxClosingSpeed : maxClosingSpeed;
        float closing = Vector3.Dot(velocity, dir);
        if (closing >= closeCap) pull -= dir * Vector3.Dot(pull, dir);
        else if (closing + Vector3.Dot(pull, dir) > closeCap)
            pull = dir * (closeCap - closing);

        velocity += pull;
    }

    // Single exit point, so every way a hook can end also arms the refire delay. Missing one
    // would let a held button re-grab the same surface on the very next tick.
    void Detach(float dt)
    {
        Attached = false;
        anchorTarget = null;      // or the next hook keeps riding the last person you held
        anchorHealth = null;
        refireAt = Time.time + refireDelay;
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
        TimeLeft = attachTime;
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
