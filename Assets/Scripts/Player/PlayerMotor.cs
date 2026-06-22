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

    public float Speed => new Vector2(velocity.x, velocity.z).magnitude;

    CapsuleCollider col;
    GrappleHook grapple;

    void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        grapple = GetComponent<GrappleHook>();
        col.radius = radius;
        col.height = height;
        col.center = Vector3.up * (height * 0.5f);
        if (input == null) input = GetComponent<InputReader>();
        if (yaw == null) yaw = transform;
        // Exclude our own layer so ground/wall casts never hit the player capsule.
        groundMask &= ~(1 << gameObject.layer);
        flow = 1f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        GroundCheck();
        UpdateFlow(dt);

        Vector3 wish = WishDir();
        float maxSpeed = groundSpeed * (useFlow ? flow : 1f);

        if (grounded)
        {
            ApplyFriction(dt);
            Accelerate(wish, maxSpeed, groundAccel, dt);
            if (!TryJump()) velocity.y = -2f; // press down to stay glued
        }
        else
        {
            float ws = useAirCap ? Mathf.Min(maxSpeed, airCapSpeed) : maxSpeed;
            Accelerate(wish, ws, airAccel, dt);
            velocity.y -= gravity * dt;
        }

        // Grapple shapes velocity after accel/gravity, before we move (motor = sole mover).
        if (grapple != null) grapple.ApplyTo(ref velocity, transform.position, dt);

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

    Vector3 WishDir()
    {
        Vector2 mv = input != null ? input.Move : Vector2.zero;
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

    bool TryJump()
    {
        bool want = autoBhop
            ? (input != null && input.JumpHeld)
            : (input != null && input.ConsumeJump());
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
