using UnityEngine;

// Grapple swing. Right mouse fires from the crosshair; on a hit it anchors and
// applies a pendulum rope constraint to the motor's velocity. Releasing keeps all
// momentum (slingshot). Scroll reels in / out.
//
// Integrates with PlayerMotor via ApplyTo(): the motor calls this each fixed tick
// AFTER accel/gravity and BEFORE the move, so the motor stays the single mover and
// grapple never fights it for the transform.
[RequireComponent(typeof(LineRenderer))]
public class GrappleHook : MonoBehaviour
{
    [Header("Refs")]
    public InputReader input;
    public Transform aim;             // camera transform (ray origin + direction)

    [Header("Grapple")]
    public LayerMask grappleMask = ~0;
    public float maxRange = 60f;
    public float minRope = 3f;
    public float reelSpeed = 18f;     // rope length change per second while scrolling
    [Tooltip("How hard the taut rope corrects back to its length (0..1 of the overshoot per tick).")]
    [Range(0f, 1f)] public float pullCorrection = 0.5f;
    [Tooltip("Constant inward tug for a powered swing. 0 = pure pendulum.")]
    public float swingAssist = 0f;

    [Header("Visual")]
    public float ropeWidth = 0.06f;
    public Color ropeColor = new Color(0.2f, 0.9f, 1f);

    public bool Attached { get; private set; }
    public Vector3 Anchor { get; private set; }
    public float RopeLength { get; private set; }

    LineRenderer line;
    bool wasHeld;
    const float center = 1f;          // swing point = feet + up*center (capsule middle)

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        if (input == null) input = GetComponent<InputReader>();
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

    Vector3 SwingPoint => transform.position + Vector3.up * center;
    Vector3 RopeStart => aim != null ? aim.position - aim.up * 0.25f : SwingPoint;

    // Called by PlayerMotor each fixed tick (after accel/gravity, before the move).
    public void ApplyTo(ref Vector3 velocity, Vector3 pos, float dt)
    {
        if (input == null || aim == null) return;

        bool held = input.GrappleHeld;
        if (held && !wasHeld) TryAttach();
        else if (!held && wasHeld) Attached = false; // release -> momentum preserved
        wasHeld = held;

        if (!Attached) return;

        // Reel in (scroll up) / out (scroll down) — direction only, notch size ignored.
        float scroll = input.Scroll.y;
        if (Mathf.Abs(scroll) > 0.01f)
            RopeLength = Mathf.Clamp(RopeLength - Mathf.Sign(scroll) * reelSpeed * dt,
                minRope, maxRange);

        Vector3 toAnchor = Anchor - SwingPoint;
        float dist = toAnchor.magnitude;
        if (dist < 0.001f) return;
        Vector3 dir = toAnchor / dist;

        if (dist > RopeLength)
        {
            // Remove velocity pointing away from the anchor; keep tangential -> swing.
            float radialOut = Vector3.Dot(velocity, -dir);
            if (radialOut > 0f) velocity += dir * radialOut;
            // Pull back toward the rope length so it behaves taut.
            float overshoot = dist - RopeLength;
            velocity += dir * (overshoot / dt) * pullCorrection;
        }

        if (swingAssist > 0f) velocity += dir * (swingAssist * dt);
    }

    void TryAttach()
    {
        if (Physics.Raycast(aim.position, aim.forward, out RaycastHit hit, maxRange,
                grappleMask, QueryTriggerInteraction.Ignore))
        {
            Attached = true;
            Anchor = hit.point;
            RopeLength = Mathf.Max(minRope, Vector3.Distance(SwingPoint, Anchor));
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
