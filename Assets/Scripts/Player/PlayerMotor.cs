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
    public Transform yaw;             // direction reference (defaults to this)

    [Header("Capsule")]
    public float radius = 0.5f;
    public float height = 2f;
    public LayerMask groundMask = ~0; // self is skipped explicitly
    public float skin = 0.02f;
    public int maxSlides = 5;

    [Header("Ground move")]
    public float groundSpeed = 9f;
    public float groundAccel = 14f;
    public float friction = 8f;
    public float stopSpeed = 2f;
    public float slopeLimit = 55f;
    [Tooltip("How far below the feet we still count as standing on ground (also snap distance).")]
    public float groundProbe = 0.35f;

    [Header("Air move (strafe-jump)")]
    public float airAccel = 2f;
    public bool useAirCap = false;
    public float airCapSpeed = 1.2f;

    [Header("Jump / gravity")]
    public float gravity = 22f;
    public float jumpForce = 8f;
    public bool autoBhop = true;

    [Header("Crouch / slide")]
    public float standHeight = 2f;
    public float crouchHeight = 1f;
    public float crouchSpeed = 4f;         // crouch-walk speed (ignores flow)
    public float stanceLerp = 14f;         // how fast the capsule/eye resize
    public Transform head;                 // camera to lower (auto = child camera)
    public float standEyeHeight = 1.6f;    // camera local Y standing (captured in Awake)
    [Tooltip("Min horizontal speed to start a slide when you tap crouch.")]
    public float slideEnterSpeed = 8f;
    public float slideBoost = 1.15f;       // momentum kick on slide entry
    public float slideFriction = 2f;       // low friction while sliding (vs friction)
    public float slideTurnRate = 4f;       // slide steer speed (rad/s), rotates velocity, never adds speed
    public float slideSlopeAccel = 18f;    // downhill acceleration while sliding
    public float slideMinSpeed = 5f;       // slide ends below this speed (hysteresis vs enter)

    [Header("Flow (accessible momentum)")]
    [Tooltip("Keep moving and top speed climbs toward groundSpeed*flowMax; stop and it bleeds. " +
             "This is the 'more movement = more speed' feel without needing Quake strafe skill.")]
    public bool useFlow = true;
    public float flowMax = 2.5f;          // top speed = groundSpeed * this
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

    // Freeze flag set by PlayerHealth on death/respawn. Skips the sim WITHOUT toggling
    // component.enabled — an auto-property is not serialized, so a death state can never
    // leak into a saved scene (which once shipped a build where the player couldn't move).
    public bool Frozen { get; set; }

    // True while the grapple is reeling — used to drop ground-glue so it can lift you.
    bool Grappling => grapple != null && grapple.Attached;

    CapsuleCollider col;
    GrappleHook grapple;

    void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        grapple = GetComponent<GrappleHook>();
        height = standHeight;
        col.radius = radius;
        UpdateCapsule();
        if (input == null) input = GetComponent<InputReader>();
        if (yaw == null) yaw = transform;
        if (head == null)
        {
            var camT = GetComponentInChildren<Camera>();
            if (camT != null) head = camT.transform;
        }
        if (head != null) standEyeHeight = head.localPosition.y;
        // Exclude our own layer so ground/wall casts never hit the player capsule.
        groundMask &= ~(1 << gameObject.layer);
        flow = 1f;
    }

    void FixedUpdate()
    {
        if (Frozen) return; // dead / respawning — skip the sim (see PlayerHealth)
        // Local play: build the tick command from live input and step the sim.
        Step(input != null ? input.Sample() : InputCmd.None, Time.fixedDeltaTime);
    }

    // Deterministic movement step — the ONLY input is `cmd`, so this can be replayed
    // for client-side prediction / reconciliation later without changing the feel.
    // (Facing still comes from `yaw`/transform; look angles join the command when
    // networking lands.)
    public void Step(InputCmd cmd, float dt)
    {
        GroundCheck();
        UpdateStance(cmd, dt);
        UpdateFlow(dt);

        Vector3 wish = WishDir(cmd);

        if (grounded)
        {
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
                float cap = crouching ? crouchSpeed : groundSpeed * (useFlow ? flow : 1f);
                ApplyFriction(dt);
                Accelerate(wish, cap, groundAccel, dt);
                if (!TryJump(cmd) && !Grappling) velocity.y = -2f; // glued down, unless grapple lifts us
            }
        }
        else
        {
            float maxSpeed = groundSpeed * (useFlow ? flow : 1f);
            float ws = useAirCap ? Mathf.Min(maxSpeed, airCapSpeed) : maxSpeed;
            Accelerate(wish, ws, airAccel, dt);
            velocity.y -= gravity * dt;
        }

        // Grapple shapes velocity after accel/gravity, before we move (motor = sole mover).
        if (grapple != null) grapple.ApplyTo(ref velocity, transform.position, dt, cmd.grapple);

        Vector3 pos = CollideAndSlide(transform.position, velocity * dt);
        Depenetrate(ref pos);
        Depenetrate(ref pos);
        transform.position = pos;
    }

    // Accessible momentum: keep moving and top speed climbs; slow on ground and it bleeds.
    void UpdateFlow(float dt)
    {
        if (Speed > flowMoveThreshold) flow += flowGainPerSec * dt;
        else if (grounded) flow -= flowDecayPerSec * dt;
        flow = Mathf.Clamp(flow, 1f, flowMax);
    }

    // Crouch / slide state machine. Hold (or tap) crouch while moving fast on the
    // ground to slide (keeps momentum, low friction, ducks under low ceilings);
    // crouch-walk when slow. You can only stand back up when there is headroom.
    void UpdateStance(InputCmd cmd, float dt)
    {
        bool crouchHeld = cmd.crouch;

        // Hold OR tap to slide: you're sliding whenever crouching, grounded and
        // still fast. Speed hysteresis (enter at slideEnterSpeed, exit at the lower
        // slideMinSpeed) stops flicker and re-boosting every tick.
        if (sliding)
        {
            if (!crouchHeld || !grounded || Speed < slideMinSpeed)
                sliding = false;
        }
        else if (crouchHeld && grounded && Speed >= slideEnterSpeed)
        {
            sliding = true;
            velocity.x *= slideBoost; // one-time momentum kick on entry
            velocity.z *= slideBoost;
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

    void UpdateCapsule()
    {
        col.height = height;
        col.center = Vector3.up * (height * 0.5f);
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
        Vector3 crown = transform.position + Vector3.up * (height - radius);
        return !(Physics.SphereCast(crown, radius - 0.02f, Vector3.up, out RaycastHit hit,
                     delta + 0.02f, groundMask, QueryTriggerInteraction.Ignore)
                 && hit.collider != col);
    }

    // External knockback (explosions, future rocket jump). Adds straight to velocity.
    public void AddImpulse(Vector3 impulse)
    {
        velocity += impulse;
        if (impulse.y > 0.1f) grounded = false;
    }

    // Jump pad: guarantees at least `up` vertical launch, adds horizontal carry.
    public void PadBoost(float up, Vector3 horizontal)
    {
        velocity.x += horizontal.x;
        velocity.z += horizontal.z;
        velocity.y = Mathf.Max(velocity.y, up);
        grounded = false;
    }

    Vector3 WishDir(InputCmd cmd)
    {
        Vector2 mv = cmd.move;
        Vector3 dir = yaw.right * mv.x + yaw.forward * mv.y;
        dir.y = 0f;
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
        return true;
    }

    void GroundCheck()
    {
        // Clearly rising (just jumped) = airborne; skip friction so bhop keeps speed.
        if (velocity.y > 1f) { grounded = false; return; }

        // Start the probe above the feet so it never begins overlapping the floor
        // (casts ignore initial overlaps, which would falsely report "no ground").
        Vector3 origin = transform.position + Vector3.up * (radius + 0.1f);
        float castDist = 0.1f + groundProbe;
        if (Physics.SphereCast(origin, radius * 0.85f, Vector3.down, out RaycastHit hit,
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

            if (Physics.CapsuleCast(p1, p2, radius, dir, out RaycastHit hit,
                    dist + skin, groundMask, QueryTriggerInteraction.Ignore)
                && hit.collider != col)
            {
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
        Collider[] overlaps = Physics.OverlapCapsule(p1, p2, radius, groundMask,
            QueryTriggerInteraction.Ignore);
        foreach (var other in overlaps)
        {
            if (other == col) continue;
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
        float h = Mathf.Max(0f, height * 0.5f - radius);
        p1 = c + Vector3.up * h;
        p2 = c - Vector3.up * h;
    }
}
