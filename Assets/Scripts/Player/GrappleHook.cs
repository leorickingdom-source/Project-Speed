using UnityEngine;

// Grapple reel — BASELINE for every player: right mouse always works, no pickup and no
// loadout slot. It is the traversal verb the game is built around, so gating it would
// mean some players simply are not playing the movement game (cf. Warsow's dash,
// Titanfall's wall-run).
// On a hit it anchors and REELS the player straight
// toward the anchor (Titanfall-style yank): it drives your velocity onto the rope
// line so the reel is firm and immediate, not a saggy swing. Auto-releases just before
// the anchor so you launch past instead of splatting; releasing early keeps momentum.
//
// Integrates with PlayerMotor via ApplyTo(): the motor calls this each fixed tick
// AFTER accel/gravity and BEFORE the move, so the motor stays the single mover.
[RequireComponent(typeof(LineRenderer))]
public class GrappleHook : MonoBehaviour
{
    [Header("Refs")]
    public Transform aim;             // camera transform (ray origin + direction)

    [Header("Grapple")]
    public LayerMask grappleMask = ~0;
    [Tooltip("Max anchor distance. 35 after playtest (was 60): 60 reached two-thirds of the " +
             "Arena, so the grapple was a cross-map travel ticket — any wall, from anywhere. " +
             "35 keeps it a local tool: you must already be near the structure you want to " +
             "ride, so positioning still matters before the rope trivialises it.")]
    public float maxRange = 35f;
    [Tooltip("How hard you're yanked toward the anchor (m/s^2). Higher = snappier pull.")]
    public float pullAccel = 55f;
    [Tooltip("Max reel-in speed toward the anchor (m/s).")]
    public float maxPullSpeed = 32f;
    [Tooltip("Auto-release when this close to the anchor, so you launch past instead of splatting.")]
    public float arriveDistance = 2.5f;

    [Header("Visual")]
    public float ropeWidth = 0.06f;
    public Color ropeColor = new Color(0.2f, 0.9f, 1f);

    public bool Attached { get; private set; }
    public Vector3 Anchor { get; private set; }

    // Snapshot / restore for reconciliation — the motor folds these into MotorState so a
    // corrected client replays with the rope in exactly the state the server had.
    public void GetNetState(out bool attached, out Vector3 anchor, out bool held)
    {
        attached = Attached;
        anchor = Anchor;
        held = wasHeld;
    }

    public void SetNetState(bool attached, Vector3 anchor, bool held)
    {
        Attached = attached;
        Anchor = anchor;
        wasHeld = held;
    }

    LineRenderer line;
    bool wasHeld;
    const float center = 1f;          // pull reference = feet + up*center (capsule middle)

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        if (aim == null)
        {
            var c = GetComponentInChildren<Camera>();
            if (c != null) aim = c.transform;
        }
        grappleMask &= ~(1 << gameObject.layer); // never grapple ourselves
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
    public void ApplyTo(ref Vector3 velocity, Vector3 aimOrigin, Vector3 aimDir, float dt,
        bool grappleHeld)
    {
        bool held = grappleHeld;
        if (held && !wasHeld) TryAttach(aimOrigin, aimDir);
        else if (!held && wasHeld) Attached = false; // release -> momentum preserved
        wasHeld = held;

        if (!Attached) return;

        Vector3 toAnchor = Anchor - PullPoint;
        float dist = toAnchor.magnitude;
        if (dist <= arriveDistance) { Attached = false; return; } // arrived -> launch past
        Vector3 dir = toAnchor / dist;

        // Reel straight in: drive velocity toward (dir * maxPullSpeed), overriding
        // gravity and drift so it's a firm, immediate yank rather than a saggy arc.
        // pullAccel is how fast velocity snaps onto that reel vector (m/s per second).
        velocity = Vector3.MoveTowards(velocity, dir * maxPullSpeed, pullAccel * dt);
    }

    void TryAttach(Vector3 origin, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRange,
                grappleMask, QueryTriggerInteraction.Ignore))
        {
            Attached = true;
            Anchor = hit.point;
        }
    }

    void LateUpdate()
    {
        if (Attached)
        {
            line.enabled = true;
            line.SetPosition(0, RopeStart);
            line.SetPosition(1, Anchor);
        }
        else if (line.enabled)
        {
            line.enabled = false;
        }
    }
}
