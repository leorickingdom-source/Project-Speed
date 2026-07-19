using UnityEngine;

// Manually-swept projectile (reliable for fast travel — no tunneling). On impact
// it triggers an Explosion. Its travel mask excludes the shooter so you can fire
// at your own feet and ride the splash (rocket jump).
public class Rocket : MonoBehaviour
{
    public float speed = 38f;
    public float castRadius = 0.15f;
    public float life = 6f;
    public float blastRadius = 5f;
    public float blastForce = 16f;
    public float selfForce = 22f;   // your own rocket-jump kick
    public float selfDamageScale = 0.5f; // fraction of the blast you take (Quake halves it)
    public float damageScale = 1f;  // momentum passive multiplier, sampled at launch
    public float damage = 80f;

    Vector3 dir;
    LayerMask travelMask;
    GameObject owner;

    public void Launch(Vector3 direction, LayerMask travelHitMask, GameObject shooter)
    {
        dir = direction.normalized;
        travelMask = travelHitMask;
        owner = shooter;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        life -= dt;
        if (life <= 0f) { Boom(transform.position); return; }

        float step = speed * dt;
        if (Physics.SphereCast(transform.position, castRadius, dir, out RaycastHit hit,
                step, travelMask, QueryTriggerInteraction.Ignore))
            // Detonate at the sphere's CENTRE on contact, not on the surface itself: a
            // blast sitting exactly on a wall would fail its own line-of-sight traces
            // (they'd all start inside that wall) and shield everyone from everything.
            Boom(hit.point + hit.normal * castRadius);
        else
            transform.position += dir * step;
    }

    void Boom(Vector3 p)
    {
        Explosion.Detonate(p, blastRadius, blastForce, selfForce, damage, owner,
            travelMask, selfDamageScale, damageScale);
        Destroy(gameObject);
    }
}
