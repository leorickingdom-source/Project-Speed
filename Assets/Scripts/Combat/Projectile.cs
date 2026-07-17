using UnityEngine;

// Simple dodgeable projectile for AI (bots). Manually swept with a SphereCast so it
// can't tunnel at speed; deals direct damage to the first IDamageable it hits, then
// despawns. No splash — clean and readable so the player can strafe/slide/grapple
// around it. Real-player weapons are hitscan (WeaponController); this is AI only.
public class Projectile : MonoBehaviour
{
    public float speed = 22f;
    public float life = 5f;
    public float damage = 15f;
    public float castRadius = 0.2f;

    Vector3 dir;
    LayerMask mask = ~0;
    GameObject owner;

    public void Launch(Vector3 direction, float dmg, LayerMask hitMask, GameObject shooter)
    {
        dir = direction.normalized;
        damage = dmg;
        mask = hitMask;
        owner = shooter;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        life -= dt;
        if (life <= 0f) { Destroy(gameObject); return; }

        float step = speed * dt;
        if (Physics.SphereCast(transform.position, castRadius, dir, out RaycastHit hit,
                step, mask, QueryTriggerInteraction.Ignore)
            && hit.collider.gameObject != owner)
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null) dmg.Damage(damage);
            Destroy(gameObject);
            return;
        }
        transform.position += dir * step;
    }
}
