using UnityEngine;

// Dodgeable travelling shot — bot attacks, and the player's bow / throwing knives.
// Manually swept with a SphereCast so it can't tunnel at speed; deals direct damage to
// the first IDamageable it hits, then despawns. No splash (that's Rocket).
//
// Velocity-based rather than direction-plus-speed so `gravity` can arc it. gravity 0 is
// a flat shot, which is what the bot attacks and the bow both use: realistic arrows drop,
// but leading a target that dashes and grapples at 18+ m/s is already hard enough without
// solving a ballistic arc on top of it.
public class Projectile : MonoBehaviour
{
    public float speed = 22f;
    public float gravity = 0f;      // 0 = flat. Arc is a per-weapon choice.
    public float life = 5f;
    public float damage = 15f;
    public float castRadius = 0.2f;

    [Header("Headshots")]
    public float headMultiplier = 2f;
    [Range(0f, 1f)] public float headFraction = 0.28f;

    [Header("Passives")]
    public float damageScale = 1f;  // Momentum / Highground / Camper, sampled at launch

    Vector3 vel;
    LayerMask mask = ~0;
    GameObject owner;

    public void Launch(Vector3 direction, float dmg, LayerMask hitMask, GameObject shooter)
    {
        vel = direction.normalized * speed;
        damage = dmg;
        mask = hitMask;
        owner = shooter;
        if (vel.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(vel);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        life -= dt;
        if (life <= 0f) { Destroy(gameObject); return; }

        vel.y -= gravity * dt;

        float step = vel.magnitude * dt;
        if (step <= 0f) return;
        Vector3 dir = vel / vel.magnitude;

        if (Physics.SphereCast(transform.position, castRadius, dir, out RaycastHit hit,
                step, mask, QueryTriggerInteraction.Ignore)
            && hit.collider.gameObject != owner)
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null)
            {
                bool head = Headshot.IsHead(hit.collider, hit.point, headFraction);
                dmg.Damage(damage * (head ? headMultiplier : 1f) * damageScale);
            }
            Destroy(gameObject);
            return;
        }

        transform.position += dir * step;
        transform.rotation = Quaternion.LookRotation(dir); // nose follows the arc
    }
}
