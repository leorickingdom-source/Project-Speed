using UnityEngine;

// Floating arena pickup. Overlap-polls for the player (cast-based, like JumpPad —
// no Rigidbody, collider is a trigger so it never blocks the motor), grants a
// timed power-up, then hides and respawns on a cooldown so the spot is contested.
[RequireComponent(typeof(Collider))]
public class PowerupPickup : MonoBehaviour
{
    [Header("Grant")]
    public PowerupType type = PowerupType.Grapple;
    public float duration = 25f;          // how long the power-up lasts once taken
    public float respawnCooldown = 20f;   // dead time before it returns

    [Tooltip("Layers that can pick this up (leave as Everything to accept the player).")]
    public LayerMask playerMask = ~0;

    [Header("Visual")]
    [Tooltip("Mesh to spin/bob. Defaults to first child, else this transform.")]
    public Transform visual;
    public float spinSpeed = 90f;         // deg/sec
    public float bobHeight = 0.35f;
    public float bobSpeed = 2f;

    Collider area;
    Renderer[] renderers;
    bool available = true;
    float readyAt;
    float baseY;
    float bobT;

    void Awake()
    {
        area = GetComponent<Collider>();
        area.isTrigger = true; // motor ignores triggers -> pickup never blocks movement
        if (visual == null) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
        renderers = GetComponentsInChildren<Renderer>();
        baseY = visual.localPosition.y;
    }

    void Update()
    {
        if (available)
        {
            bobT += Time.deltaTime * bobSpeed;
            visual.localRotation *= Quaternion.Euler(0f, spinSpeed * Time.deltaTime, 0f);
            Vector3 lp = visual.localPosition;
            lp.y = baseY + Mathf.Sin(bobT) * bobHeight;
            visual.localPosition = lp;
        }
        else if (Time.time >= readyAt)
        {
            SetAvailable(true);
        }
    }

    void FixedUpdate()
    {
        if (!available) return;
        Bounds b = area.bounds;
        Collider[] hits = Physics.OverlapBox(b.center, b.extents, transform.rotation,
            playerMask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var receiver = h.GetComponentInParent<PowerupReceiver>();
            if (receiver != null)
            {
                receiver.Grant(type, duration);
                readyAt = Time.time + respawnCooldown;
                SetAvailable(false);
                return;
            }
        }
    }

    void SetAvailable(bool on)
    {
        available = on;
        if (renderers != null)
            foreach (var r in renderers) r.enabled = on;
    }
}
