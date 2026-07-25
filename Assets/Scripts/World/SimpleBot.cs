using FishNet.Object;
using UnityEngine;

// Ground chaser: walks toward the nearest living player, gnaws on contact, lobs dodgeable
// projectiles at range. Placeholder enemy for the movement playground — no nav mesh, no
// collision avoidance.
//
// SERVER-AUTHORITATIVE. It used to be a plain MonoBehaviour, which meant every client ran its
// own copy of the AI: three machines each moved the bot somewhere different and each applied
// its own contact damage. Now the server alone decides where a bot is and what it does, and the
// transform replicates. Offline it keeps full authority, so single player is unchanged.
//
// Targeting re-evaluates instead of latching onto the first player it ever found. The old
// FindAnyObjectByType picked an arbitrary player once and then chased that one forever, even
// across the map, past somebody standing next to it.
[RequireComponent(typeof(Health))]
public class SimpleBot : NetworkBehaviour
{
    [Tooltip("Which bot slot this is. The host picks how many bots run; slots at or above that " +
             "number take themselves off the board. Set 0,1,2,... per bot in the scene.")]
    public int botIndex;

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
    [Tooltip("Cone the bot's aim is randomised within at the LOWEST difficulty, shrinking to " +
             "zero at full. Its shots are otherwise perfectly aimed at the moment of firing.")]
    public float maxAimErrorDegrees = 7f;
    public LayerMask sightMask = ~0;

    [Header("Targeting")]
    [Tooltip("Seconds between target re-evaluations. Not every frame: picking the nearest " +
             "player is a scan over everyone, and a bot that re-aims 100 times a second " +
             "oscillates between two equidistant targets instead of committing to one.")]
    public float retargetInterval = 0.5f;

    Health health;
    MatchManager match;
    Transform target;
    float nextFire;
    float nextRetarget;
    bool live = true;

    bool HasAuthority => !IsSpawned || IsServerStarted;

    void Awake()
    {
        health = GetComponent<Health>();

        // Same dark head cap players wear, so "aim for the head" is learnable on bots — the
        // safe targets — before it has to be executed on people. 0.28 matches the
        // WeaponController default headFraction; bots carry no WeaponController to read it from.
        var rend = GetComponent<Renderer>();
        if (rend != null) HeadCapVisual.Attach(transform, 0.28f, rend.material.color);
    }

    // How many bots the host asked for. Read through MatchManager so it is the same number on
    // every client — the same route Pickup uses for the pickups mode.
    int WantedBots
    {
        get
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            return match != null ? match.BotCount : BotChoice.Count;
        }
    }

    // Damage and rate-of-fire scalar, host-decided and synced alongside the count. Read live
    // rather than cached in Awake so it stays correct if the value ever changes mid-match.
    float Difficulty
    {
        get
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            return Mathf.Clamp(match != null ? match.BotDifficulty : BotChoice.Difficulty, 0.05f, 1f);
        }
    }

    void Update()
    {
        // Runs everywhere: whether this slot is in play is a synced decision, and every client
        // has to hide the ones that are not.
        bool want = botIndex < WantedBots;
        if (want != live)
        {
            live = want;
            if (health != null) health.SetSuppressed(!live);
        }

        if (!live || !HasAuthority) return;
        if (health != null && !health.Alive) return;

        if (Time.time >= nextRetarget)
        {
            nextRetarget = Time.time + retargetInterval;
            target = FindNearestLivingPlayer();
        }
        if (target == null) return;

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

        float diff = Difficulty;

        // Contact damage: gnaw the player while touching. Scaled hardest by difficulty because
        // it is CONTINUOUS — three bots touching at once is the single biggest source of death,
        // and it is the one a player cannot react to, only walk out of.
        if (dist <= touchRange)
        {
            var hp = target.GetComponent<IDamageable>();
            if (hp != null) hp.Damage(damagePerSec * diff * Time.deltaTime);
        }

        // Ranged: lob a dodgeable projectile when it can see you. No lead, so you can
        // strafe/slide/grapple around it. (Real-player weapons stay hitscan — AI only.)
        if (dist <= fireRange && Time.time >= nextFire)
        {
            Vector3 muzzle = transform.position + Vector3.up * 1.2f;
            Vector3 aim = target.position + Vector3.up * 1.0f;
            if (CanSee(muzzle, aim))
            {
                Vector3 dir = (aim - muzzle).normalized;

                // Aim error, widening as difficulty drops. Without it the shot is pixel-perfect
                // at the moment of firing, so the projectile is only "dodgeable" by moving AFTER
                // it is already travelling — a bot that never misses is not easier to read, it
                // just kills you more slowly.
                float errorDeg = Mathf.Lerp(maxAimErrorDegrees, 0f, diff);
                if (errorDeg > 0.01f)
                {
                    Vector2 off = Random.insideUnitCircle * Mathf.Tan(errorDeg * Mathf.Deg2Rad);
                    Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                    Vector3 up = Vector3.Cross(dir, right);
                    dir = (dir + right * off.x + up * off.y).normalized;
                }

                // Slower cadence at lower difficulty, so the gap between shots is a real window.
                nextFire = Time.time + fireCooldown / diff;

                if (IsSpawned) BroadcastShot(muzzle, dir);
                else FireProjectile(muzzle, dir);
            }
        }
    }

    // Everyone spawns the projectile so everyone SEES it — a shot that only exists on the
    // server is damage arriving from nothing. Only the server's copy actually hurts anybody:
    // PlayerHealth and Health both refuse writes without authority, so the client copies are
    // pure visuals that expire on their own.
    [ObserversRpc(RunLocally = true)]
    void BroadcastShot(Vector3 muzzle, Vector3 dir) => FireProjectile(muzzle, dir);

    // Nearest living player, so a bot fights whoever is actually near it.
    Transform FindNearestLivingPlayer()
    {
        Transform best = null;
        float bestSqr = float.MaxValue;

        foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (p == null || !p.Alive) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d >= bestSqr) continue;
            bestSqr = d;
            best = p.transform;
        }

        return best;
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
        proj.Launch(dir, projectileDamage * Difficulty, ~0, gameObject);
    }
}
