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
    public float maxRange = 60f;
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
    public void ApplyTo(ref Vector3 velocity, Vector3 pos, float dt, bool grappleHeld)
    {
        if (aim == null) return;

        bool held = grappleHeld;
        if (held && !wasHeld) TryAttach();
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

    void TryAttach()
    {
        if (Physics.Raycast(aim.position, aim.forward, out RaycastHit hit, maxRange,
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
