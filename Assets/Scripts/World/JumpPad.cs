using UnityEngine;

// Polling launch pad: when the player overlaps the pad volume, fling them up (and
// optionally along the pad's +Z). No Rigidbody/trigger needed — matches the
// cast-based controller. Put on a flat box; its collider bounds define the pad area.
[RequireComponent(typeof(Collider))]
public class JumpPad : MonoBehaviour
{
    public float upForce = 20f;
    public float forwardForce = 0f;      // along pad's +Z (tilt the pad to aim a launch)
    public LayerMask playerMask = ~0;
    public float cooldown = 0.25f;

    Collider area;
    float next;

    void Awake() { area = GetComponent<Collider>(); }

    void FixedUpdate()
    {
        if (Time.time < next) return;
        Bounds b = area.bounds;
        Collider[] hits = Physics.OverlapBox(b.center, b.extents, transform.rotation,
            playerMask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var pm = h.GetComponentInParent<PlayerMotor>();
            if (pm != null)
            {
                pm.PadBoost(upForce, transform.forward * forwardForce);
                next = Time.time + cooldown;
                return;
            }
        }
    }
}
