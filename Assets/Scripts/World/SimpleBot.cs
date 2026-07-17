using UnityEngine;

// Dumb ground chaser: walks toward the player on flat ground. Shootable via its
// Health (respawns). Placeholder enemy for the movement playground — no nav mesh,
// no collision avoidance yet.
[RequireComponent(typeof(Health))]
public class SimpleBot : MonoBehaviour
{
    public float moveSpeed = 4.5f;
    public float stopDistance = 2f;
    public float turnSpeed = 8f;

    [Header("Melee")]
    public float touchRange = 2.2f;
    public float damagePerSec = 12f;

    [Header("Ranged (dodgeable projectiles)")]
    public float fireRange = 30f;
    public float fireCooldown = 1.6f;
    public float projectileSpeed = 20f;
    public float projectileDamage = 12f;
    public LayerMask sightMask = ~0;

    Transform target;
    float nextFire;

    void Start()
    {
        var pm = FindAnyObjectByType<PlayerMotor>();
        if (pm != null) target = pm.transform;
    }

    void Update()
    {
        if (target == null)
        {
            var pm = FindAnyObjectByType<PlayerMotor>();
            if (pm != null) target = pm.transform;
            if (target == null) return;
        }
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist > stopDistance && dist > 0.01f)
        {
            Vector3 dir = to / dist;
            transform.position += dir * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }

        // Contact damage: gnaw the player while touching.
        if (dist <= touchRange)
        {
            var hp = target.GetComponent<IDamageable>();
            if (hp != null) hp.Damage(damagePerSec * Time.deltaTime);
        }

        // Ranged: lob a dodgeable projectile when it can see you. No lead, so you can
        // strafe/slide/grapple around it. (Real-player weapons stay hitscan — AI only.)
        if (dist <= fireRange && Time.time >= nextFire)
        {
            Vector3 muzzle = transform.position + Vector3.up * 1.2f;
            Vector3 aim = target.position + Vector3.up * 1.0f;
            if (CanSee(muzzle, aim))
            {
                FireProjectile(muzzle, (aim - muzzle).normalized);
                nextFire = Time.time + fireCooldown;
            }
        }
    }

    bool CanSee(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.01f) return true;
        // Clear line if nothing blocks, or the first thing hit is the player.
        if (Physics.Raycast(from, d / dist, out RaycastHit h, dist, sightMask,
                QueryTriggerInteraction.Ignore))
            return h.collider.transform == target || h.collider.GetComponentInParent<PlayerHealth>() != null;
        return true;
    }

    void FireProjectile(Vector3 muzzle, Vector3 dir)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BotShot";
        go.transform.position = muzzle;
        go.transform.localScale = Vector3.one * 0.4f;
        Destroy(go.GetComponent<Collider>()); // projectile sweeps with raycasts; no physical collider
        var rend = go.GetComponent<Renderer>();
        Color c = new Color(1f, 0.45f, 0.15f);
        if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", c);
        rend.material.color = c;
        var proj = go.AddComponent<Projectile>();
        proj.speed = projectileSpeed;
        proj.Launch(dir, projectileDamage, ~0, gameObject);
    }
}
