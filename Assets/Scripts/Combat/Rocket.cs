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
    // Extra damage for hitting someone with the rocket ITSELF, paid on top of the blast they
    // are standing in. 0 = splash only, which is what every other explosive here wants.
    public float directDamage = 0f;

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
        {
            DirectHit(hit.collider);
            // Detonate at the sphere's CENTRE on contact, not on the surface itself: a
            // blast sitting exactly on a wall would fail its own line-of-sight traces
            // (they'd all start inside that wall) and shield everyone from everything.
            Boom(hit.point + hit.normal * castRadius);
        }
        else
            transform.position += dir * step;
    }

    // Damage for hitting the target with the rocket itself, paid before the blast that follows.
    // A "direct hit" was otherwise worth nothing on its own — the blast centre lands OUTSIDE
    // the victim's capsule, so the best shot in the game scored 0.87 of the splash and a shot
    // that missed by a metre scored 0.6. This is the difference between those two.
    //
    // NEVER the shooter. The travel mask does not exclude the player layer (hitscan needs it),
    // so a rocket fired at your own feet can sweep into your own capsule — and a rocket jump
    // that cost 40 extra would be a different, much worse ability.
    void DirectHit(Collider c)
    {
        if (directDamage <= 0f || c == null) return;
        if (owner != null && c.transform.IsChildOf(owner.transform)) return;

        var hp = c.GetComponentInParent<IDamageable>();
        if (hp == null) return;

        // Kill credit before the damage, exactly as Explosion does it: whichever of the two
        // finishes them, the feed has to name the shooter rather than say "X died".
        if (owner != null)
        {
            var victimHealth = hp as PlayerHealth;
            var ownerNob = owner.GetComponent<FishNet.Object.NetworkObject>();
            if (victimHealth != null && ownerNob != null)
                victimHealth.RecordServerAttacker(ownerNob, KillKind.Normal);
        }
        // Scaled by Momentum like every other outgoing number. On a pure client this write is
        // authority-refused, the same as the blast's — the server's copy of this rocket is the
        // one that counts.
        hp.Damage(directDamage * damageScale);
    }

    void Boom(Vector3 p)
    {
        Explosion.Detonate(p, blastRadius, blastForce, selfForce, damage, owner,
            travelMask, selfDamageScale, damageScale);
        Destroy(gameObject);
    }
}
