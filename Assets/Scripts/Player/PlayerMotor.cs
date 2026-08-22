using UnityEngine;

// Quake/CPMA-style momentum controller.
//   * Single source of truth = `velocity`.
//   * Custom collide-and-slide (NO CharacterController / Rigidbody) so momentum
//     is preserved on walls and ramps — the whole point of the movement feel.
//   * Runs on the fixed tick (see GameTick).
// Air model defaults to VQ3: full wishspeed in air, and overspeed emerges from
// strafing off-axis from your velocity (the classic strafe-jump). Flip
// useAirCap for a Source-style capped feel.
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Refs")]
    public InputReader input;

    [Header("Capsule")]
    [Tooltip("BASE capsule radius. Read the Radius property for the effective value — the " +
             "Featherweight passive narrows it.")]
    public float radius = 0.5f;
    [Tooltip("Capsule radius while the Featherweight passive is equipped. 0.4 vs 0.5 is a 20% " +
             "narrower silhouette, so hitscan misses more often. HEIGHT is deliberately left " +
             "alone: shrinking it would drag the camera down with it (standEyeHeight derives " +
             "from it) and change how you fit the slide tunnel — much bigger change, same benefit.")]
    public float featherweightRadius = 0.4f;
    public float height = 2f;
    public LayerMask groundMask = ~0; // self is skipped explicitly
    public float skin = 0.02f;
    public int maxSlides = 5;

    [Header("Ground move")]
    public float groundSpeed = 9f;
    public float groundAccel = 18f;
    [Tooltip("Ground friction rate. 8 -> 11 after playtest called the ground 'very slidy': at 8 " +
             "the speed you carry off a rope or a pad survives most of a corridor, so landing " +
             "never felt like arriving. The air is still frictionless, so bunnyhopping keeps " +
             "everything — this only taxes the player who stopped hopping.")]
    public float friction = 11f;
    [Tooltip("Friction is computed against AT LEAST this speed, so slow movement decays at a " +
             "flat rate instead of an exponential one that never quite reaches zero. 2 -> 4: " +
             "the last few m/s were the slidy part, because that is exactly where friction " +
             "proportional to speed has almost nothing left to bite on.")]
    public float stopSpeed = 4f;
    public float slopeLimit = 55f;
    [Tooltip("How far below the feet we still count as standing on ground (also snap distance).")]
    public float groundProbe = 0.35f;

    [Header("Air move (strafe-jump)")]
    public float airAccel = 2f;
    [Tooltip("Air acceleration while a grapple is attached. Much higher than the normal air " +
             "value, and deliberately separate from it: 2 is the Quake-ish number that makes " +
             "strafe-jumping a skill, but on a rope it left you with no authority over the arc " +
             "at all — the swing was whatever physics decided at the moment you attached, which " +
             "is what read as 'stiff'. Raising only this lets you SHAPE a swing without " +
             "touching how the air feels the rest of the time.")]
    public float grappleAirAccel = 7f;
    public bool useAirCap = false;
    public float airCapSpeed = 1.2f;
    [Tooltip("Air STEERING, in radians per second, for speeds above the air cap. Quake's " +
             "PM_Accelerate can add nothing once you are already at the cap along your wish " +
             "direction, so a fast player has literally zero authority — playtest: 'too fast, " +
             "cant drift'. This rotates horizontal velocity toward your input WITHOUT changing " +
             "its magnitude, so it buys control and never speed: strafe-jumping is untouched, " +
             "and nobody can turn it into an engine. The rate is scaled by cap/speed, so 16 m/s " +
             "steers freely and 40 m/s is a wide committed arc — going faster still costs you " +
             "the ability to change your mind, which is the trade the speed is worth having.")]
    public float airSteerRate = 2f;

    [Header("Dash passive")]
    [Tooltip("Speed the dash brings you UP TO — a floor, not an additive kick. From a walk " +
             "(9) it is a huge gain; already at 16 it is nearly nothing. Deliberately a " +
             "catch-up tool rather than a snowball one, so it cannot compound with movement " +
             "skill. Above this speed a dash is a free instant REDIRECT and adds nothing. " +
             "21 after two playtest rounds (18 -> 19 -> 21) asking for more physical punch; " +
             "the HUD gauge ceiling moved to 23 to keep the dash off the peg.")]
    public float dashSpeed = 21f;
    [Tooltip("Seconds between dashes. Fixed rather than refreshed on landing: landing-reset " +
             "would hand a bhopping expert several times the dashes of a new player, stacking " +
             "a resource gap on top of an execution gap. 1.5 -> 1.1 -> 0.9 across playtests: " +
             "at 0.9 the dash is nearly a rhythm tool — still not free (a missed dash is " +
             "still a second of exposure), but frequent enough to be part of normal movement.")]
    public float dashCooldown = 0.9f;
    [Tooltip("Seconds of friction immunity after a dash. Without it, ground friction bleeds an " +
             "18 m/s dash back to groundSpeed in ~87ms (ln(2)/friction) and a ground dash is " +
             "imperceptible — only air dashes survive, because air has no friction. " +
             "0.45 -> 0.25 with the friction rise: half a second of NO ground friction was " +
             "itself a large part of what playtest read as sliding, and 0.25 is still four " +
             "times what it takes for the dash to be felt.")]
    public float dashGrace = 0.25f;
    [Tooltip("Guaranteed speed GAINED by a dash even when already above dashSpeed. The pure " +
             "floor meant a dash at speed changed nothing — physically true to the no-" +
             "snowball rule, but it made the button feel broken (playtest: 'no difference on " +
             "forward dash'). +5 (was +3, playtest asked for more) is a real lunge at any " +
             "speed; friction and the cooldown still bleed most of it back between dashes, " +
             "so chaining dashes drifts upward slowly rather than compounding.")]
    public float dashGain = 5f;

    [Header("Jump / gravity")]
    [Tooltip("22 -> 28 after playtest called flight 'floaty'. Hang time is what floaty MEANS: " +
             "at 22 a jump spends 0.72s in the air and a 20 m/s pad launch 1.8s, most of it " +
             "with nothing to do. jumpForce/airJumpForce/wallJumpUp were all raised alongside " +
             "it so every height in the game is unchanged (h = v^2/2g) — this makes the same " +
             "arcs happen FASTER, it does not lower the ceiling.")]
    public float gravity = 28f;
    [Tooltip("Gravity multiplier while a grapple is attached. 1 = no exception. It was 0.7 " +
             "when the rope was a force that had to out-pull gravity to climb; the rope is now " +
             "a constraint that simply forbids moving away from the anchor, so it does not need " +
             "the help — and playtest's 'weird sometimes' was partly this: gravity silently " +
             "changing strength depending on whether a rope happened to be attached.")]
    [Range(0.1f, 1f)] public float grappleGravityScale = 1f;
    public float jumpForce = 9f;
    public bool autoBhop = true;
    [Tooltip("Air jumps allowed by the DoubleJump passive, refunded on landing.")]
    public int airJumpCount = 1;
    [Tooltip("Upward speed of an air jump. Set flat rather than added to current velocity so " +
             "it rescues a fall predictably instead of doing nothing when you're dropping fast. " +
             "10 after two playtest rounds (7.5 -> 8.5 -> 10), then 11.3 to hold that same " +
             "height at the raised gravity — visibly ABOVE the ground jump, so the second jump " +
             "reads as the feature it costs a passive slot to have.")]
    public float airJumpForce = 11.3f;
    [Header("Wall jump (baseline, everyone has it)")]
    [Tooltip("Upward speed of a wall kick. Slightly under the ground jump: a wall is a route, " +
             "not a better staircase. 7.5 -> 8.5 keeps the old height at the raised gravity.")]
    public float wallJumpUp = 8.5f;
    [Tooltip("Speed floor pushed away from the wall. High enough that the kick CLEARS the " +
             "surface — a wall jump that leaves you scraping the same wall is a stall.")]
    public float wallJumpPush = 11f;
    [Tooltip("How far from the capsule a wall counts as kickable.")]
    public float wallJumpReach = 1.1f;
    [Tooltip("Kicks allowed per airtime, refunded on landing. Two is a corner: in and out.")]
    public int wallJumpCount = 2;

    [Tooltip("Horizontal speed ADDED by an air jump on top of the redirect. The dash's " +
             "dashGain, applied to the double jump for the same reason: a redirect that " +
             "keeps magnitude is invisible when you jump straight ahead. Small enough that " +
             "one air jump per airtime cannot become an engine.")]
    public float airJumpGain = 2f;

    [Header("Crouch / slide")]
    public float standHeight = 2f;
    public float crouchHeight = 1f;
    public float crouchSpeed = 4f;         // crouch-walk speed (ignores flow)
    public float stanceLerp = 14f;         // how fast the capsule/eye resize
    public Transform head;                 // camera to lower (auto = child camera)
    public float standEyeHeight = 1.6f;    // camera local Y standing (captured in Awake)
    [Tooltip("The visible body, scaled to match the crouch so other players see the stance " +
             "they are shooting at. Auto = the child named \"Body\".")]
    public Transform body;                 // visual capsule (auto = child "Body")
    Vector3 bodyStandScale = Vector3.one;  // captured in Awake, so the Inspector stays the truth
    Vector3 bodyStandPos;
    [Tooltip("Min horizontal speed to start a slide when you tap crouch.")]
    public float slideEnterSpeed = 6f;
    public float slideBoost = 1.35f;       // momentum kick on slide entry
    [Tooltip("Ceiling the entry kick can lift you to. Downhill slope accel can still carry " +
             "you past it — that speed is earned off the map, not off tapping crouch.")]
    public float slideMaxSpeed = 16f;
    [Tooltip("Seconds before the entry kick can fire again. Without it, tapping crouch re-enters " +
             "the slide every few ticks and slideBoost compounds into unbounded speed.")]
    public float slideCooldown = 0.4f;
    public float slideFriction = 1f;       // low friction while sliding (vs friction)
    public float slideTurnRate = 4f;       // slide steer speed (rad/s), rotates velocity, never adds speed
    public float slideSlopeAccel = 18f;    // downhill acceleration while sliding
    public float slideMinSpeed = 3f;       // slide ends below this speed (hysteresis vs enter)

    [Header("Jump pads")]
    [Tooltip("Seconds before a pad may launch THIS player again. Per-player and part of the " +
             "sim state, unlike the pad-side timer it replaces, which was shared by everyone " +
             "who stepped on it.")]
    public float padCooldown = 0.25f;

    [Header("Flow (accessible momentum)")]
    [Tooltip("Keep moving and your AIR top speed climbs toward groundSpeed*flowMax; stop and it " +
             "bleeds. This is the 'more movement = more speed' feel without needing Quake strafe skill.")]
    public bool useFlow = true;
    [Tooltip("Let flow raise the GROUND cap too. Off (Quake-like): the ground is hard-capped at " +
             "groundSpeed, so everything above it is speed you hold in the air and lose to friction " +
             "the moment you stop hopping — that asymmetry is what makes bunnyhopping worth doing. " +
             "On: you reach groundSpeed*flowMax just by running, and hopping stops mattering.")]
    public bool flowRaisesGroundCap = false;
    public float flowMax = 1.8f;          // air top speed = groundSpeed * this
    [Tooltip("flowMax while the Slipstream passive is equipped. 2.05 against 1.8 is 18.5 m/s " +
             "instead of 16.2. NOTE this is mobility ONLY, not damage: MomentumDamage saturates " +
             "at fullBonusSpeed 16, which the base ceiling of 16.2 already clears, so every " +
             "metre per second above it is worth nothing in a fight. That is a gap in the speed " +
             "-> power loop rather than a property of this passive — the grapple's 28 and a " +
             "rocket jump's launch hit the same wall. Small on purpose either way: flow has to " +
             "be EARNED by moving, so this raises the roof, not the floor.")]
    public float slipstreamFlowMax = 2.05f;
    public float flowGainPerSec = 0.45f;  // how fast it builds while moving
    public float flowDecayPerSec = 1.5f;  // how fast it bleeds when slow on ground
    public float flowMoveThreshold = 4f;  // speed above which flow builds
    public float flow = 1f;               // live multiplier (debug-visible)

    [Header("Read-only debug")]
    public Vector3 velocity;
    public bool grounded;
    public Vector3 groundNormal = Vector3.up;
    public bool crouching;
    public bool sliding;

    public float Speed => new Vector2(velocity.x, velocity.z).magnitude;

    // Effective capsule radius: base, or featherweightRadius when that passive is equipped.
    // Resolved once in Awake into an AUTO-PROPERTY rather than assigned back over `radius`,
    // because `radius` is serialized — writing a passive's value into it at runtime could
    // leak into a saved scene. Same trap that made the death freeze a runtime flag instead
    // of toggling component.enabled.
    public float Radius { get; private set; }

    // HUD readouts for the mobility passives.
    public bool HasDash => hasDash;
    public float DashCooldownLeft => dashCooldownLeft;
    public bool HasDoubleJump => hasDoubleJump;
    public int AirJumpsLeft => Mathf.Max(0, airJumpCount - airJumpsUsed);

    // Target speeds fed to PM_Accelerate. Flow always raises the air ceiling; it only
    // raises the ground ceiling if you opt in (see flowRaisesGroundCap).
    float AirWishSpeed => groundSpeed * (useFlow ? flow : 1f);
    float GroundWishSpeed => groundSpeed * (useFlow && flowRaisesGroundCap ? flow : 1f);

    // Freeze flag set by PlayerHealth on death/respawn. Skips the sim WITHOUT toggling
    // component.enabled — an auto-property is not serialized, so a death state can never
    // leak into a saved scene (which once shipped a build where the player couldn't move).
    public bool Frozen { get; set; }

    // Set by PlayerNetwork while networking is running: the FishNet tick calls Step() from
    // inside [Replicate], so FixedUpdate must NOT also step or the sim advances twice per
    // tick. Auto-property (never serialized) so it can't leak into a saved scene, and it
    // clears on OnStopNetwork so offline play falls back to FixedUpdate.
    public bool ExternallyDriven { get; set; }

    // True while the grapple is reeling — used to drop ground-glue so it can lift you.
    //
    // ANY attached hook drops the glue, at any angle, deliberately: an angle gate was tried on
    // 2026-07-27 (lift only above ~11 degrees) to stop the landing-sound stutter and REVERTED
    // the same day — peeling off the floor from a shallow rope is the Apex feel this grapple
    // is built on, and gating it made the rope feel like it only worked when aimed upward.
    // The stutter it caused is handled where it belongs, in PlayerAudio's edge filters.
    bool Grappling => grapple != null && grapple.Attached;

    // Seconds of airtime that ended with the most recent landing. Audio reads it to tell a real
    // landing from the flicker a rope or a ramp produces; the sim never does, so it is
    // deliberately not in MotorState.
    public float LastAirTime { get; private set; }

    // Did the last move get stopped by geometry? The grapple needs to know: a rope that keeps
    // hauling on a player pinned against a wall stores up velocity that fires the instant they
    // slide free. Set during CollideAndSlide, read by ApplyTo on the following tick.
    public bool Blocked { get; private set; }

    CapsuleCollider col;
    GrappleHook grapple;
    PassiveLoadout passives;    // optional — null means base radius / no dash
    float slideBoostCooldown;   // counts down on dt so Step() stays replayable
    float dashCooldownLeft;     // ditto — dt, never Time.time
    float dashGraceLeft;        // friction-immunity window after a dash
    int airJumpsUsed;           // reconciled — see MotorState
    float airTime;              // seconds continuously airborne — reconciled, see MotorState
    int wallJumpsUsed;          // reconciled — see MotorState
    Vector3 lastWallNormal;     // ditto: which wall the last kick came off
    float padCooldownLeft;      // ditto — jump pads are detected inside the sim now
    bool hasDash;               // resolved once in Awake
    bool hasDoubleJump;
    bool hasSlipstream;         // ditto — raises the flow ceiling, see UpdateFlow

    // How long you must be continuously airborne before the AIR-ONLY verbs (space-dash,
    // double jump) unlock. Exists because GroundCheck calls a climb "airborne": walking up a
    // ramp puts velocity.y over its rising threshold, so single ticks read as air mid-walk —
    // and a buffered jump press on such a tick became a phantom dash (playtest: "walking
    // along the ramp triggers dash"). Six ticks of real airtime is imperceptible on a
    // deliberate air move and impossible for ramp flicker, which regrounds every tick.
    const float AirVerbDelay = 0.06f;

    void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        grapple = GetComponent<GrappleHook>();
        height = standHeight;
        passives = GetComponent<PassiveLoadout>();
        if (passives != null) passives.Changed += ApplyPassives;
        ApplyPassives();
        if (input == null) input = GetComponent<InputReader>();
        if (head == null)
        {
            var camT = GetComponentInChildren<Camera>();
            if (camT != null) head = camT.transform;
        }
        if (head != null) standEyeHeight = head.localPosition.y;
        if (body == null) body = transform.Find("Body");
        if (body != null)
        {
            bodyStandScale = body.localScale;
            bodyStandPos = body.localPosition;
        }
        // Exclude our own layer so ground/wall casts never hit the player capsule.
        groundMask &= ~(1 << gameObject.layer);
        flow = 1f;
    }

    void FixedUpdate()
    {
        if (ExternallyDriven) return; // the network tick steps us instead (see PlayerNetwork)
        // Local play: build the tick command from live input and step the sim.
        Step(input != null ? input.Sample() : InputCmd.None, Time.fixedDeltaTime);
    }

    // Snapshot / restore the full mutable sim state. Reconciliation captures the server's
    // authoritative MotorState, SetState()s it, then replays buffered commands through Step().
    // Nothing calls these until the network layer exists — pure additions, no behavior change.
    public MotorState GetState()
    {
        var s = new MotorState
        {
            position = transform.position,
            velocity = velocity,
            grounded = grounded,
            groundNormal = groundNormal,
            crouching = crouching,
            sliding = sliding,
            height = height,
            flow = flow,
            slideBoostCooldown = slideBoostCooldown,
            dashCooldownLeft = dashCooldownLeft,
            dashGraceLeft = dashGraceLeft,
            airJumpsUsed = airJumpsUsed,
            airTime = airTime,
            wallJumpsUsed = wallJumpsUsed,
            lastWallNormal = lastWallNormal,
            padCooldownLeft = padCooldownLeft,
        };
        if (grapple != null)
            grapple.GetNetState(out s.grappleAttached, out s.grappleAnchor, out s.grappleHeld,
                out s.grappleTimeLeft, out s.grappleRopeLength, out s.grappleCooldownLeft);
        return s;
    }

    public void SetState(MotorState s)
    {
        transform.position = s.position;
        velocity = s.velocity;
        grounded = s.grounded;
        groundNormal = s.groundNormal;
        crouching = s.crouching;
        sliding = s.sliding;
        height = s.height;
        flow = s.flow;
        slideBoostCooldown = s.slideBoostCooldown;
        dashCooldownLeft = s.dashCooldownLeft;
        dashGraceLeft = s.dashGraceLeft;
        airJumpsUsed = s.airJumpsUsed;
        airTime = s.airTime;
        wallJumpsUsed = s.wallJumpsUsed;
        lastWallNormal = s.lastWallNormal;
        padCooldownLeft = s.padCooldownLeft;
        if (grapple != null)
            grapple.SetNetState(s.grappleAttached, s.grappleAnchor, s.grappleHeld, s.grappleTimeLeft,
                s.grappleRopeLength, s.grappleCooldownLeft);
        UpdateCapsule(); // collider must match the restored height before the next cast
    }

    // Deterministic movement step — the ONLY inputs are `cmd` (facing included) and `dt`, so
    // given the same command it produces the same result on client and server. That's what
    // makes client-side prediction / reconciliation possible: no hidden inputs, no transform
    // reads, no Time.time.
    public void Step(InputCmd cmd, float dt)
    {
        // Dead / respawning. Checked HERE rather than only in FixedUpdate, because the
        // networked path calls Step() directly from [Replicate] — with the check only in
        // FixedUpdate, a dead player kept simulating and could fly around while frozen.
        if (Frozen) return;

        // Ticked at the top so the grounded branch below reads it in the same tick it was set.
        dashGraceLeft = Mathf.Max(0f, dashGraceLeft - dt);

        GroundCheck();
        // Captured BEFORE any jump, because TryJump clears `grounded`. The air-dash binding
        // below needs to know where you were when the tick started, or a normal ground jump
        // would immediately read as an air dash.
        bool groundedAtTickStart = grounded;
        UpdateStance(cmd, dt);
        UpdateFlow(dt);

        Vector3 wish = WishDir(cmd);

        if (grounded)
        {
            airJumpsUsed = 0; // refunded by touching ground
            wallJumpsUsed = 0;
            // How long the airtime that just ended was. Audio-only: PlayerAudio uses it to tell
            // a real landing from the centimetre-high flicker a rope or a ramp produces, which
            // it cannot ask about after the fact because the next line erases it.
            if (airTime > 0f) LastAirTime = airTime;
            airTime = 0f;     // ramp flicker regrounds every tick, so this never accumulates
            if (sliding)
            {
                // Keep momentum: low friction, speed-preserving steer, downhill accel.
                ApplySlideFriction(dt);
                SlideSteer(wish, dt);
                AddSlopeAccel(dt);
                if (!TryJump(cmd) && !Grappling) velocity.y = -2f;
            }
            else
            {
                float cap = crouching ? crouchSpeed : GroundWishSpeed;
                // Skip friction during the dash grace window, or the dash is gone in ~87ms.
                // Accelerate can't slow you either (add <= 0 above cap), so the burst carries.
                if (dashGraceLeft <= 0f) ApplyFriction(dt);
                Accelerate(wish, cap, groundAccel, dt);
                if (!TryJump(cmd) && !Grappling)
                {
                    // Glue PERPENDICULAR to the surface, not straight down. A straight-down
                    // press on a ramp gets projected along the slope by CollideAndSlide,
                    // which re-injected a downhill drift every tick — a player standing
                    // still on a ramp slowly slid off it (playtest). Pressing along
                    // -groundNormal projects to exactly zero lateral motion, so standing
                    // is standing; on flat ground the two are identical.
                    Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
                    velocity = h - groundNormal * 2f;
                }
            }
        }
        else
        {
            airTime += dt; // before the air verbs, so a deliberate jump unlocks them on time
            TryAirJump(cmd, wish);
            TryWallJump(cmd, wish);
            float ws = useAirCap ? Mathf.Min(AirWishSpeed, airCapSpeed) : AirWishSpeed;
            if (Grappling) AccelerateOnRope(wish, ws, grappleAirAccel, dt);
            else { Accelerate(wish, ws, airAccel, dt); AirSteer(wish, ws, dt); }
            velocity.y -= gravity * (Grappling ? grappleGravityScale : 1f) * dt;
        }

        // Dash lands AFTER accel/gravity so it reads as instant and full-strength; friction
        // and accel resume next tick. Grapple still gets the last word below.
        TryDash(cmd, wish, dt, groundedAtTickStart);

        // Grapple shapes velocity after accel/gravity, before we move (motor = sole mover).
        // The aim ray is rebuilt from the COMMAND, not the camera transform, so the server
        // reproduces exactly the ray the client fired. Origin comes off `height`, which is
        // already reconciled, so a crouched grapple lines up on both sides too.
        if (grapple != null)
        {
            Vector3 eye = transform.position + Vector3.up * (standEyeHeight - (standHeight - height));
            Vector3 aimDir = Quaternion.Euler(cmd.pitch, cmd.yaw, 0f) * Vector3.forward;
            // jumpHeld doubles as "reel me in" while a rope is attached. It is the same button
            // that jumps on the ground and spends an air jump in the air, and it never has to
            // choose: those two need a PRESS, this one a HOLD.
            grapple.ApplyTo(ref velocity, eye, aimDir, dt, cmd.grapple, Blocked, cmd.meleePressed,
                cmd.jumpHeld);
        }

        // Last, so a launch survives everything else this tick and is the velocity we move on.
        CheckJumpPad(dt);

        Blocked = false;
        Vector3 pos = CollideAndSlide(transform.position, velocity * dt);
        Depenetrate(ref pos);
        Depenetrate(ref pos);
        // The rope is inextensible, so it gets the last word on WHERE the move ended — then
        // one more depenetration, because pulling onto the rope sphere can push into geometry.
        if (grapple != null && grapple.ConstrainPosition(ref pos)) Depenetrate(ref pos);
        transform.position = pos;
    }

    // Jump pads, detected from INSIDE the sim.
    //
    // They used to push on the player from their own FixedUpdate, which broke three ways at
    // once. It was invisible to prediction — the server launched you on its tick, the client
    // did not, and reconciliation snapped the launch away, so a pad fired or did not at random
    // online. It polled a single overlap per tick, so at 25 m/s (0.5m of travel per tick) a
    // thin pad was simply not there to be touched. And it built its overlap box from
    // `bounds.extents`, which is already world-axis-aligned, while ALSO passing the pad's
    // rotation — so every rotated pad in Arena tested the wrong volume.
    //
    // A swept capsule against the same static geometry the motor already trusts fixes all
    // three: deterministic (same cast on both machines), continuous (it is a sweep, not a
    // point sample), and rotation-correct (there is no box).
    void CheckJumpPad(float dt)
    {
        padCooldownLeft = Mathf.Max(0f, padCooldownLeft - dt);
        if (padCooldownLeft > 0f) return;

        // Raised the same way GroundCheck raises its probe: a cast that starts already
        // overlapping the pad reports nothing at all, which is exactly the case of standing
        // on one.
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        JumpPad pad = PadCast(origin, Vector3.down, 0.1f + groundProbe);

        // And along this tick's motion, which is what catches the pad you would otherwise
        // cross in a single step.
        if (pad == null)
        {
            Vector3 motion = velocity * dt;
            float d = motion.magnitude;
            if (d > 0.01f) pad = PadCast(origin, motion / d, d + skin);
        }

        if (pad == null) return;
        PadBoost(pad.upForce, pad.Launch);
        padCooldownLeft = padCooldown;
    }

    JumpPad PadCast(Vector3 origin, Vector3 dir, float dist)
    {
        GetCapsule(origin, out Vector3 p1, out Vector3 p2);
        if (!Physics.CapsuleCast(p1, p2, Radius, dir, out RaycastHit hit, dist, groundMask,
                QueryTriggerInteraction.Ignore)) return null;
        if (hit.collider == col) return null;
        return hit.collider.GetComponentInParent<JumpPad>();
    }

    // Accessible momentum: keep moving and top speed climbs; slow on ground and it bleeds.
    void UpdateFlow(float dt)
    {
        if (Speed > flowMoveThreshold) flow += flowGainPerSec * dt;
        else if (grounded) flow -= flowDecayPerSec * dt;
        // Ceiling resolved from the passive, like Radius is — never asked per tick from
        // PassiveLoadout, so the sim stays free of lookups and both machines agree.
        flow = Mathf.Clamp(flow, 1f, hasSlipstream ? slipstreamFlowMax : flowMax);
    }

    // Crouch / slide state machine. Hold (or tap) crouch while moving fast on the
    // ground to slide (keeps momentum, low friction, ducks under low ceilings);
    // crouch-walk when slow. You can only stand back up when there is headroom.
    void UpdateStance(InputCmd cmd, float dt)
    {
        bool crouchHeld = cmd.crouch;
        slideBoostCooldown = Mathf.Max(0f, slideBoostCooldown - dt);

        // Hold OR tap to slide: you're sliding whenever crouching, grounded and
        // still fast. Speed hysteresis (enter at slideEnterSpeed, exit at the lower
        // slideMinSpeed) stops flicker.
        //
        // ENTERING is always allowed — gating it would make crouch feel unresponsive —
        // but the momentum kick is rate-limited by slideCooldown. Hysteresis alone only
        // stopped re-boosting on consecutive TICKS; tapping crouch a few times a second
        // still re-entered and re-multiplied slideBoost, compounding speed without bound.
        if (sliding)
        {
            if (!crouchHeld || !grounded || Speed < slideMinSpeed)
                sliding = false;
        }
        else if (crouchHeld && grounded && Speed >= slideEnterSpeed)
        {
            sliding = true;
            if (slideBoostCooldown <= 0f) ApplySlideBoost();
        }

        bool wantLow = sliding || crouchHeld;
        float target = wantLow ? crouchHeight : standHeight;
        if (!wantLow && !HasHeadroom(standHeight)) target = crouchHeight; // ceiling above
        crouching = target < standHeight - 0.01f;

        // Resize the capsule. On the ground the feet stay put and the crown ducks; in
        // the AIR, crouching tucks the feet UP (head held fixed) so you can crouch-jump
        // onto ledges a normal jump can't clear — your feet land where your center was.
        float prevHeight = height;
        height = Mathf.MoveTowards(height, target, stanceLerp * dt);
        if (!grounded) transform.position += Vector3.up * (prevHeight - height);
        UpdateCapsule();
        if (head != null)
        {
            Vector3 lp = head.localPosition;
            lp.y = standEyeHeight - (standHeight - height);
            head.localPosition = lp;
        }
    }

    // Dash passive: burst in your INPUT direction (facing-relative), ground or air, on a
    // flat cooldown.
    //
    // A speed FLOOR plus a small guaranteed surge. Mathf.Max(Speed + dashGain, dashSpeed):
    // a walking player still gains a lot, and a fast one now gains exactly dashGain — enough
    // to FEEL, too little to compound (everything between dashes bleeds more than it grants;
    // see the dashGain tooltip). Above the floor it stays primarily an instant REDIRECT.
    void TryDash(InputCmd cmd, Vector3 wish, float dt, bool groundedAtTickStart)
    {
        dashCooldownLeft = Mathf.Max(0f, dashCooldownLeft - dt);
        if (!hasDash || dashCooldownLeft > 0f) return;

        // Shift dashes anywhere. Space ALSO dashes, but only after REAL airtime — on the
        // ground it has to stay jump, and "the ground" must include ramp ticks that
        // GroundCheck mislabels as air (see AirVerbDelay), or walking up a ramp turns a
        // buffered jump into a phantom dash. Dash and DoubleJump are mutually exclusive
        // picks, so Space never has to choose between them: it means "use my mobility perk".
        bool wants = cmd.dashPressed
                     || (!groundedAtTickStart && cmd.jumpPressed && airTime >= AirVerbDelay);
        if (!wants) return;

        // No movement input = dash where you look, so the button never silently does nothing.
        Vector3 dir = wish.sqrMagnitude > 1e-4f ? wish : Quaternion.Euler(0f, cmd.yaw, 0f) * Vector3.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();

        float speed = Mathf.Max(Speed + dashGain, dashSpeed);
        velocity.x = dir.x * speed;
        velocity.z = dir.z * speed;   // vertical untouched — a dash never fights gravity
        dashCooldownLeft = dashCooldown;
        dashGraceLeft = dashGrace;
    }

    // One-time momentum kick on slide entry, clamped to slideMaxSpeed. Never slows you:
    // at or above the ceiling the kick simply doesn't apply, and no cooldown is spent.
    // The early return also covers speed == 0, so the divide below is always safe.
    void ApplySlideBoost()
    {
        float speed = Speed;
        float target = Mathf.Min(speed * slideBoost, slideMaxSpeed);
        if (target <= speed) return;

        float scale = target / speed;
        velocity.x *= scale;
        velocity.z *= scale;
        slideBoostCooldown = slideCooldown;
    }

    // Resolve the passives this motor CACHES (radius, dash) — the others are read live by
    // their own components. Runs once in Awake and again whenever the loadout changes, so the
    // in-game picker can swap Featherweight/Dash live instead of only at scene load.
    void ApplyPassives()
    {
        Radius = (passives != null && passives.Has(PassiveType.Featherweight))
            ? featherweightRadius : radius;
        hasDash = passives != null && passives.Has(PassiveType.Dash);
        hasDoubleJump = passives != null && passives.Has(PassiveType.DoubleJump);
        hasSlipstream = passives != null && passives.Has(PassiveType.Slipstream);
        col.radius = Radius;
        UpdateCapsule();
    }

    void OnDestroy()
    {
        if (passives != null) passives.Changed -= ApplyPassives;
    }

    void UpdateCapsule()
    {
        col.height = height;
        col.center = Vector3.up * (height * 0.5f);
        UpdateBodyVisual();
    }

    // The visible body has to crouch with the collider. It did not, and the mismatch was a
    // shooter-grade bug rather than a cosmetic one: a crouched player still LOOKED two metres
    // tall while their capsule was one, so shots aimed at the visible chest passed through
    // empty air, and because Headshot.IsHead scores off COLLIDER bounds the head band sat at
    // the visible belt line. The dark head cap made it worse by staying where it was painted
    // (HeadCapVisual sizes it once at spawn), so the one marking that says "aim here" was
    // pointing at a body part that could not be hit.
    //
    // Scaling the parent is enough for the cap too: it is parented to Body, so it rides the
    // top of whatever height Body currently is.
    void UpdateBodyVisual()
    {
        if (body == null) return;
        // Unity's capsule mesh is 2 units tall at scale 1, and standHeight is the height that
        // scale was authored for — so the ratio is the scale, whatever those two are set to.
        float k = standHeight > 0.01f ? height / standHeight : 1f;
        Vector3 s = bodyStandScale;
        s.y = bodyStandScale.y * k;
        body.localScale = s;
        // Pivot is the capsule's middle, so the centre tracks half the height and the FEET
        // stay on the floor. Anchoring the top instead would sink the model into the ground.
        body.localPosition = new Vector3(bodyStandPos.x, height * 0.5f, bodyStandPos.z);
    }

    // Low friction so a slide glides; no stopSpeed floor.
    void ApplySlideFriction(float dt)
    {
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        if (speed < 0.01f) return;
        float drop = speed * slideFriction * dt;
        float scale = Mathf.Max(speed - drop, 0f) / speed;
        velocity.x *= scale;
        velocity.z *= scale;
    }

    // Steer a slide by ROTATING horizontal velocity toward wishDir, magnitude
    // unchanged — turning redirects momentum instead of pumping speed. Only friction
    // (and downhill slope) change how fast you're going.
    void SlideSteer(Vector3 wish, float dt)
    {
        if (wish.sqrMagnitude < 1e-4f) return;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float sp = flat.magnitude;
        if (sp < 0.01f) return;
        Vector3 cur = flat / sp;
        Vector3 w = new Vector3(wish.x, 0f, wish.z).normalized;
        Vector3 nd = Vector3.RotateTowards(cur, w, slideTurnRate * dt, 0f);
        velocity.x = nd.x * sp;
        velocity.z = nd.z * sp;
    }

    // Downhill pull while sliding: zero on flat ground, grows with slope steepness.
    void AddSlopeAccel(float dt)
    {
        Vector3 slope = Vector3.ProjectOnPlane(Vector3.down, groundNormal); // |slope| = sin(angle)
        velocity.x += slope.x * slideSlopeAccel * dt;
        velocity.z += slope.z * slideSlopeAccel * dt;
    }

    // Is there room to grow back to targetHeight? Casts up from the crown only, so
    // standing next to a wall never counts as "blocked" — only a real ceiling does.
    bool HasHeadroom(float targetHeight)
    {
        float delta = targetHeight - height;
        if (delta <= 0.001f) return true;
        Vector3 crown = transform.position + Vector3.up * (height - Radius);
        return !(Physics.SphereCast(crown, Radius - 0.02f, Vector3.up, out RaycastHit hit,
                     delta + 0.02f, groundMask, QueryTriggerInteraction.Ignore)
                 && hit.collider != col);
    }

    // External knockback (explosions, future rocket jump). Adds straight to velocity.
    public void AddImpulse(Vector3 impulse)
    {
        velocity += impulse;
        if (impulse.y > 0.1f) grounded = false;
    }

    // Jump pad: guarantees at least `up` vertical launch, adds horizontal carry. Called from
    // CheckJumpPad inside Step, so it is predicted and replayed like every other verb — the
    // pad no longer reaches in from outside the sim (see JumpPad).
    public void PadBoost(float up, Vector3 horizontal)
    {
        velocity.x += horizontal.x;
        velocity.z += horizontal.z;
        velocity.y = Mathf.Max(velocity.y, up);
        grounded = false;
    }

    Vector3 WishDir(InputCmd cmd)
    {
        // Facing comes from the COMMAND, not the transform. Quaternion.Euler(0,yaw,0) * (mx,0,my)
        // equals the old body.right*mx + body.forward*my exactly, because the body only yaws —
        // so local feel is unchanged while the server can now reproduce the direction.
        Vector3 dir = Quaternion.Euler(0f, cmd.yaw, 0f) * new Vector3(cmd.move.x, 0f, cmd.move.y);
        return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
    }

    // Quake PM_Accelerate. Overspeed appears when wishDir is off-axis from velocity.
    void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishDir == Vector3.zero) return;
        float current = velocity.x * wishDir.x + velocity.z * wishDir.z;
        float add = wishSpeed - current;
        if (add <= 0f) return;
        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, add);
        velocity.x += wishDir.x * accelSpeed;
        velocity.z += wishDir.z * accelSpeed;
    }

    // Steering authority ABOVE the air cap, where Accelerate has none to give.
    //
    // PM_Accelerate adds nothing once your speed along the wish direction already exceeds the
    // wish speed (add <= 0), which is correct for building speed and terrible to fly with: at
    // 30 m/s off a pad or a rope release, the whole keyboard did nothing and the only way to
    // change direction was to wait — playtest, exactly: "too fast cant drift".
    //
    // So: rotate horizontal velocity toward the input, magnitude untouched. This is SlideSteer
    // in the air, and it has the same guarantee — it cannot add a single m/s, so it cannot be
    // farmed and strafe-jumping (which is Accelerate's off-axis behaviour, below the cap and
    // untouched here) is exactly as it was.
    //
    // Rate falls off as wishSpeed/speed: at the cap you turn freely, at double it you turn at
    // half rate. Going faster costs manoeuvrability, which is the trade that makes committing
    // to a line feel like a decision.
    void AirSteer(Vector3 wish, float wishSpeed, float dt)
    {
        if (airSteerRate <= 0f || wish.sqrMagnitude < 1e-4f) return;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float sp = flat.magnitude;
        if (sp <= wishSpeed || sp < 0.01f) return;   // below the cap, Accelerate already steers
        Vector3 w = new Vector3(wish.x, 0f, wish.z);
        if (w.sqrMagnitude < 1e-4f) return;
        w.Normalize();
        Vector3 nd = Vector3.RotateTowards(flat / sp, w, airSteerRate * (wishSpeed / sp) * dt, 0f);
        velocity.x = nd.x * sp;
        velocity.z = nd.z * sp;
    }

    // Air control while a rope is attached, in the rope's frame instead of the world's.
    //
    // The bug this fixes: holding W on a rope made you SLOWER, which is the opposite of what
    // every player expects and the opposite of what letting go of the keyboard did. Plain
    // Accelerate pushes along your horizontal facing, so aiming at an anchor above you and
    // holding W poured speed straight down the rope, where the grapple reads it as "I want to
    // arrive" — it spent the input on closing distance and then let go early. Under the rope
    // constraint the radial part is not even yours to add: it is overwritten by the reel rate
    // every tick, so pressing toward the anchor is now merely wasted rather than harmful.
    //
    // So: strip the radial component out of the wish direction. What is left is tangential —
    // the direction a pendulum is pumped in — which is exactly the speed a slingshot converts
    // into distance on release. Pressing straight at the anchor now does NOTHING rather than
    // something harmful, which is the honest outcome: the rope owns how fast you close on it.
    //
    // 3D on purpose, unlike Accelerate: the tangent of a rope you are hanging under is mostly
    // horizontal, but the tangent of one you are swinging past is not, and flattening it to
    // x/z would quietly delete the vertical half of every arc.
    void AccelerateOnRope(Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishDir == Vector3.zero) return;

        Vector3 toAnchor = grapple.Anchor - (transform.position + Vector3.up * (height * 0.5f));
        if (toAnchor.sqrMagnitude < 0.01f) return;
        Vector3 rope = toAnchor.normalized;

        Vector3 tangent = wishDir - rope * Vector3.Dot(wishDir, rope);
        // Nearly parallel to the rope: the player is asking to be reeled in or pushed away,
        // and neither is theirs to ask for. Bail rather than normalising noise into a
        // direction, which would make the arc jitter when you aim near the anchor.
        if (tangent.sqrMagnitude < 1e-4f) return;
        tangent.Normalize();

        float current = Vector3.Dot(velocity, tangent);
        float add = wishSpeed - current;
        if (add <= 0f) return;
        velocity += tangent * Mathf.Min(accel * wishSpeed * dt, add);
    }

    // Ground friction only — none in air, so bunnyhopping keeps speed.
    void ApplyFriction(float dt)
    {
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        if (speed < 0.01f) { velocity.x = 0f; velocity.z = 0f; return; }
        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * friction * dt;
        float scale = Mathf.Max(speed - drop, 0f) / speed;
        velocity.x *= scale;
        velocity.z *= scale;
    }

    bool TryJump(InputCmd cmd)
    {
        bool want = autoBhop ? cmd.jumpHeld : cmd.jumpPressed;
        if (!want) return false;
        velocity.y = jumpForce;
        grounded = false;
        airJumpsUsed = 0; // leaving the ground under your own power refreshes air jumps
        return true;
    }

    // DoubleJump passive. Uses jumpPressed (the edge) and NOT jumpHeld, unlike the ground
    // jump: with autoBhop on, a held jump button would spend the air jump the instant you
    // left the ground. This way it only fires on a deliberate second press.
    //
    // Sets velocity.y flat rather than adding, so it reliably rescues a fall — adding to a
    // large negative velocity would feel like the jump did nothing.
    //
    // Also REDIRECTS: horizontal momentum swings to your input direction, magnitude kept.
    // Added after playtest ("make double jump better") — pure height was the passive's whole
    // payoff while Dash offered a redirect AND a speed floor. Turning the second jump into a
    // mid-air direction change makes it an outplay tool (juke a shotgun rush, cut around a
    // corner) without granting a single m/s of free speed — same no-snowball rule as the dash.
    // Deterministic (reads only cmd + state), so prediction/reconcile are unaffected.
    void TryAirJump(InputCmd cmd, Vector3 wish)
    {
        if (!hasDoubleJump || !cmd.jumpPressed) return;
        if (airJumpsUsed >= airJumpCount) return;
        // Same ramp-flicker guard as the space-dash — a jump press while "airborne" for one
        // mislabelled climb tick must not spend the air jump (see AirVerbDelay).
        if (airTime < AirVerbDelay) return;
        velocity.y = airJumpForce;

        // Redirect + surge: momentum swings to the input direction AND gains airJumpGain —
        // without the gain, a straight-ahead air jump kept the exact same horizontal velocity
        // and read as nothing (the same invisibility the dash's pure floor had). No input
        // still surges along the current travel direction, so the button always does a thing.
        Vector3 horiz = new Vector3(velocity.x, 0f, velocity.z);
        float mag = horiz.magnitude;
        Vector3 dir = wish.sqrMagnitude > 0.01f ? wish.normalized
                    : mag > 0.5f ? horiz / mag : Vector3.zero;
        if (dir != Vector3.zero)
        {
            float speed = mag + airJumpGain;
            velocity.x = dir.x * speed;
            velocity.z = dir.z * speed;
        }

        airJumpsUsed++;
    }

    // Wall jump. BASELINE, not a passive — the same reasoning the grapple is free (see
    // PassiveType): a traversal verb that shapes how you read every wall in the arena should
    // not cost the one loadout slot, or half the lobby is playing a different movement game.
    // As a pick it also competed with DoubleJump for the same "air recovery" job; as a
    // baseline it composes with it instead.
    //
    // Kick off a wall in mid-air, in the direction you are pushing.
    //
    // The one rule that keeps it from being a ladder: consecutive kicks must come off
    // MEANINGFULLY DIFFERENT surfaces. Without it a player pins themselves to one wall and
    // climbs it forever, which turns every boundary in the map into a staircase and makes the
    // arena's verticality meaningless. Alternating surfaces makes it a corner move — in and
    // out of a gap, around a pillar — which is a route, not an elevator.
    //
    // Deterministic (reads only cmd + state + world geometry), so prediction and reconcile
    // handle it like everything else in Step.
    void TryWallJump(InputCmd cmd, Vector3 wish)
    {
        if (!cmd.jumpPressed) return;
        if (airTime < AirVerbDelay) return;            // same ramp-flicker guard as the air jump
        if (wallJumpsUsed >= wallJumpCount) return;

        // Probe where you are PUSHING first, then the sides and behind — brushing past a wall
        // while strafing should still offer the kick, or the move only works when you aim at
        // a surface you are trying to leave.
        Vector3 face = wish.sqrMagnitude > 1e-4f
            ? wish.normalized
            : Quaternion.Euler(0f, cmd.yaw, 0f) * Vector3.forward;
        Vector3 origin = transform.position + Vector3.up * (height * 0.5f);

        if (!FindWall(origin, face, out Vector3 n)
            && !FindWall(origin, Quaternion.Euler(0f, 90f, 0f) * face, out n)
            && !FindWall(origin, Quaternion.Euler(0f, -90f, 0f) * face, out n)
            && !FindWall(origin, -face, out n))
            return;

        if (wallJumpsUsed > 0 && Vector3.Dot(n, lastWallNormal) > 0.7f) return; // same wall

        velocity.y = wallJumpUp;

        // Away from the wall, biased toward your input so you steer the exit rather than
        // being fired straight back the way you came. Speed is a FLOOR, like the dash: it
        // rescues a slow approach without rewarding one.
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = Mathf.Max(flat.magnitude, wallJumpPush);
        Vector3 outDir = n + face * 0.35f;
        outDir.y = 0f;
        if (outDir.sqrMagnitude < 1e-4f) { outDir = n; outDir.y = 0f; }
        if (outDir.sqrMagnitude < 1e-4f) return;
        outDir.Normalize();

        velocity.x = outDir.x * speed;
        velocity.z = outDir.z * speed;

        lastWallNormal = n;
        wallJumpsUsed++;
    }

    // A wall is anything steeper than the slope limit — i.e. exactly the surfaces GroundCheck
    // refuses to stand on, so the two can never disagree about what counts as a wall.
    bool FindWall(Vector3 origin, Vector3 dir, out Vector3 normal)
    {
        normal = Vector3.zero;
        if (dir.sqrMagnitude < 1e-4f) return false;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return false;
        dir.Normalize();

        if (Physics.SphereCast(origin, Radius * 0.6f, dir, out RaycastHit hit, wallJumpReach,
                groundMask, QueryTriggerInteraction.Ignore)
            && hit.collider != col
            && Vector3.Angle(hit.normal, Vector3.up) > slopeLimit)
        {
            normal = hit.normal;
            return true;
        }
        return false;
    }

    void GroundCheck()
    {
        // Clearly rising (just jumped) = airborne; skip friction so bhop keeps speed.
        if (velocity.y > 1f) { grounded = false; return; }

        // Start the probe above the feet so it never begins overlapping the floor
        // (casts ignore initial overlaps, which would falsely report "no ground").
        Vector3 origin = transform.position + Vector3.up * (Radius + 0.1f);
        float castDist = 0.1f + groundProbe;
        if (Physics.SphereCast(origin, Radius * 0.85f, Vector3.down, out RaycastHit hit,
                castDist, groundMask, QueryTriggerInteraction.Ignore)
            && hit.collider != col
            && Vector3.Angle(hit.normal, Vector3.up) <= slopeLimit)
        {
            grounded = true;
            groundNormal = hit.normal;
            return;
        }
        grounded = false;
    }

    Vector3 CollideAndSlide(Vector3 pos, Vector3 motion)
    {
        for (int i = 0; i < maxSlides && motion.sqrMagnitude > 1e-8f; i++)
        {
            float dist = motion.magnitude;
            Vector3 dir = motion / dist;
            GetCapsule(pos, out Vector3 p1, out Vector3 p2);

            if (Physics.CapsuleCast(p1, p2, Radius, dir, out RaycastHit hit,
                    dist + skin, groundMask, QueryTriggerInteraction.Ignore)
                && hit.collider != col)
            {
                Blocked = true;   // read by the grapple; see the note on its swing constraint
                float travel = Mathf.Max(hit.distance - skin, 0f);
                pos += dir * travel;
                Vector3 leftover = motion - dir * travel;
                motion = Vector3.ProjectOnPlane(leftover, hit.normal);
                velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
            }
            else
            {
                pos += motion;
                break;
            }
        }
        return pos;
    }

    // Push out of any overlap (fixes ground sink, wall interpenetration).
    void Depenetrate(ref Vector3 pos)
    {
        GetCapsule(pos, out Vector3 p1, out Vector3 p2);
        Collider[] overlaps = Physics.OverlapCapsule(p1, p2, Radius, groundMask,
            QueryTriggerInteraction.Ignore);
        foreach (var other in overlaps)
        {
            if (other == col) continue;
            // Anything else hanging off this player is ours too, and pushing away from our own
            // body is a self-propelling engine. Skipping only `col` was enough while the capsule
            // was the sole collider we owned; the animated hitboxes are children of the body
            // model, so the first frame they switched on the motor started shoving itself
            // sideways out of its own arms — a player drifting back-left with no input.
            if (other.transform.root == transform.root) continue;
            if (Physics.ComputePenetration(col, pos, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float depth))
            {
                pos += dir * depth;
                velocity = Vector3.ProjectOnPlane(velocity, dir);
            }
        }
    }

    void GetCapsule(Vector3 pos, out Vector3 p1, out Vector3 p2)
    {
        Vector3 c = pos + Vector3.up * (height * 0.5f);
        float h = Mathf.Max(0f, height * 0.5f - Radius);
        p1 = c + Vector3.up * h;
        p2 = c - Vector3.up * h;
    }
}
