using UnityEngine;
using UnityEngine.InputSystem;

// Data-driven loadout. Number keys select a weapon; left mouse fires (held for automatic
// weapons, click for semi). Hitscan weapons raycast + draw a pooled tracer and can score
// headshots (top slice of a target = bonus damage). Each weapon has a magazine; R reloads.
// Real-player PvP is hitscan; bots use dodgeable Projectiles.
//
// The loadout is currently all-hitscan: the rocket is shelved (see DefaultLoadout) to keep
// rocket-jumping out of the game. The FireKind.Projectile path below is still live and
// tested, so a projectile weapon can come back without rebuilding anything.
public enum FireKind { Hitscan, Projectile }

[System.Serializable]
public class Weapon
{
    public string name = "Pistol";
    public FireKind kind = FireKind.Hitscan;
    public bool automatic = false;     // hold to fire vs click
    public float cycle = 0.25f;        // seconds between shots
    public float damage = 20f;         // per hitscan pellet

    [Header("Hitscan")]
    public int pellets = 1;            // >1 = spread shot
    public float spreadDegrees = 0f;
    public float range = 200f;
    public Color tracer = Color.white;

    [Header("Projectile (rocket)")]
    public float projectileSpeed = 40f;
    public float blastRadius = 5f;
    public float blastDamage = 90f;
    public float blastForce = 16f;     // knockback to others
    public float selfForce = 24f;      // your own rocket-jump kick
    [Tooltip("Fraction of your own blast you take. Quake halves it so rocket-jumping is " +
             "repeatable; at 1 a single jump costs ~70% of your health.")]
    [Range(0f, 1f)] public float selfDamageScale = 0.5f;

    [Header("Ammo")]
    public int magSize = 15;
    public float reloadTime = 1.2f;
    [System.NonSerialized] public int ammo; // rounds left in the mag (runtime, never serialized)
}

public class WeaponController : MonoBehaviour
{
    public InputReader input;
    public Transform aim;

    [Header("Masks")]
    public LayerMask hitMask = ~0;      // player layer removed at runtime

    [Header("Loadout (keys 1-5)")]
    public Weapon[] weapons;            // auto-filled with the default 5 if left empty

    [Header("Headshots (hitscan)")]
    public float headMultiplier = 2f;
    [Tooltip("Top fraction of a target's height that counts as a headshot.")]
    [Range(0f, 1f)] public float headFraction = 0.28f;

    [Header("Tracers")]
    public float tracerTime = 0.04f;

    public int Current { get; private set; }
    public Weapon CurrentWeapon => (weapons != null && Current >= 0 && Current < weapons.Length) ? weapons[Current] : null;
    public string CurrentName => CurrentWeapon != null ? CurrentWeapon.name : "-";
    public int CurrentAmmo => CurrentWeapon != null ? CurrentWeapon.ammo : 0;
    public int CurrentMag => CurrentWeapon != null ? CurrentWeapon.magSize : 0;
    public bool Reloading => Time.time < reloadDoneAt;

    MomentumDamage momentum;
    HighgroundDamage highground;

    // Combined damage-passive multiplier. Each source returns 1 when not equipped, and
    // pick-one means at most one is ever above 1 — multiplying keeps it correct either way
    // and means a new damage passive only has to be added here.
    public float DamageScale => (momentum != null ? momentum.Scale : 1f)
                              * (highground != null ? highground.Scale : 1f);

    float nextFire;
    float reloadDoneAt;
    LineRenderer[] pool;
    float[] poolHide;
    int poolNext;

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
        momentum = GetComponent<MomentumDamage>();     // optional — absent means no speed bonus
        highground = GetComponent<HighgroundDamage>(); // optional — absent means no height bonus
        if (aim == null) { var c = GetComponentInChildren<Camera>(); if (c) aim = c.transform; }
        hitMask &= ~(1 << gameObject.layer);
        if (weapons == null || weapons.Length == 0) weapons = DefaultLoadout();
        foreach (var w in weapons) w.ammo = w.magSize; // start loaded
        BuildPool(16);
    }

    static Weapon[] DefaultLoadout() => new[]
    {
        new Weapon { name = "Pistol", kind = FireKind.Hitscan, automatic = true,  cycle = 0.28f,
                     damage = 22f, pellets = 1, spreadDegrees = 0f,  range = 200f, tracer = new Color(0.90f, 0.90f, 0.70f),
                     magSize = 15, reloadTime = 1.0f },
        new Weapon { name = "Rifle",  kind = FireKind.Hitscan, automatic = true,  cycle = 0.11f,
                     damage = 14f, pellets = 1, spreadDegrees = 1.5f, range = 200f, tracer = new Color(1.00f, 0.80f, 0.35f),
                     magSize = 30, reloadTime = 1.6f },
        // Rocket SHELVED (not deleted) — rocket-jumping is a Quake signature and this game
        // is deliberately diverging from it. Everything it needs still works: FireKind
        // .Projectile, FireProjectile(), Rocket.cs and Explosion.cs are all intact, so
        // restoring it is re-adding this entry (plus a digit key below) and nothing else.
        // new Weapon { name = "Rocket", kind = FireKind.Projectile, automatic = true,  cycle = 0.9f,
        //              projectileSpeed = 40f, blastRadius = 5f, blastDamage = 90f, blastForce = 16f, selfForce = 24f,
        //              magSize = 4, reloadTime = 2.2f },
        new Weapon { name = "Sniper", kind = FireKind.Hitscan, automatic = true,  cycle = 1.2f,
                     damage = 100f, pellets = 1, spreadDegrees = 0f, range = 400f, tracer = new Color(0.40f, 0.90f, 1.00f),
                     magSize = 5, reloadTime = 1.8f },
        new Weapon { name = "SMG",    kind = FireKind.Hitscan, automatic = true,  cycle = 0.07f,
                     damage = 9f,  pellets = 1, spreadDegrees = 3.5f, range = 150f, tracer = new Color(0.80f, 0.90f, 1.00f),
                     magSize = 30, reloadTime = 1.4f },
        new Weapon { name = "Shotgun", kind = FireKind.Hitscan, automatic = true, cycle = 0.7f,
                     damage = 10f, pellets = 8, spreadDegrees = 8f,  range = 40f,  tracer = new Color(1.00f, 0.75f, 0.35f),
                     magSize = 6, reloadTime = 1.8f },
    };

    void BuildPool(int n)
    {
        pool = new LineRenderer[n];
        poolHide = new float[n];
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Tracer" + i);
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.03f;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.material = new Material(sh);
            lr.enabled = false;
            pool[i] = lr;
        }
    }

    void Update()
    {
        if (Time.timeScale == 0f) return; // no firing / switching while paused
        var kb = Keyboard.current;
        var m = Mouse.current;
        if (kb != null && weapons != null)
        {
            int prev = Current;
            if (kb.digit1Key.wasPressedThisFrame) Current = 0;
            if (kb.digit2Key.wasPressedThisFrame) Current = 1;
            if (kb.digit3Key.wasPressedThisFrame) Current = 2;
            if (kb.digit4Key.wasPressedThisFrame) Current = 3;
            if (kb.digit5Key.wasPressedThisFrame) Current = 4;
            // digit6 unbound while the rocket is shelved — the Clamp below would only
            // fold it onto the last weapon, which reads as a stuck key rather than a no-op.
            Current = Mathf.Clamp(Current, 0, weapons.Length - 1);
            if (Current != prev) reloadDoneAt = 0f;       // switching cancels a reload
            if (kb.rKey.wasPressedThisFrame) StartReload();
        }

        Weapon w = CurrentWeapon;

        // Finish an in-progress reload.
        if (w != null && reloadDoneAt > 0f && Time.time >= reloadDoneAt)
        {
            w.ammo = w.magSize;
            reloadDoneAt = 0f;
        }

        if (w != null && m != null && !Reloading)
        {
            bool wantFire = w.automatic ? m.leftButton.isPressed : m.leftButton.wasPressedThisFrame;
            if (wantFire && Time.time >= nextFire)
            {
                if (w.ammo > 0)
                {
                    Fire(w);
                    w.ammo--;
                    nextFire = Time.time + w.cycle;
                }
                else StartReload(); // clicked empty -> auto-reload
            }
        }

        if (pool != null)
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].enabled && Time.time > poolHide[i]) pool[i].enabled = false;
    }

    void StartReload()
    {
        var w = CurrentWeapon;
        if (w != null && !Reloading && w.ammo < w.magSize)
            reloadDoneAt = Time.time + w.reloadTime;
    }

    void Fire(Weapon w)
    {
        if (aim == null) return;
        if (w.kind == FireKind.Projectile) FireProjectile(w);
        else FireHitscan(w);
    }

    void FireHitscan(Weapon w)
    {
        Vector3 origin = aim.position;
        int shots = Mathf.Max(1, w.pellets);
        float scale = DamageScale; // sampled once per shot, so every pellet of a spread agrees
        for (int i = 0; i < shots; i++)
        {
            Vector3 dir = aim.forward;
            if (w.spreadDegrees > 0f)
            {
                Vector2 off = Random.insideUnitCircle * Mathf.Tan(w.spreadDegrees * Mathf.Deg2Rad);
                dir = (aim.forward + aim.right * off.x + aim.up * off.y).normalized;
            }
            Vector3 end = origin + dir * w.range;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, w.range, hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var hp = hit.collider.GetComponentInParent<IDamageable>();
                if (hp != null)
                {
                    // Headshot: hit lands in the top slice of the target's collider.
                    Bounds b = hit.collider.bounds;
                    bool head = hit.point.y >= b.max.y - b.size.y * headFraction;
                    hp.Damage((head ? w.damage * headMultiplier : w.damage) * scale);
                }
            }
            Tracer(origin - aim.up * 0.15f, end, w.tracer);
        }
    }

    void FireProjectile(Weapon w)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Rocket";
        go.transform.position = aim.position + aim.forward * 0.6f; // spawn just ahead of the camera
        go.transform.localScale = Vector3.one * 0.35f;
        Destroy(go.GetComponent<Collider>()); // Rocket sweeps with SphereCast; no physical collider
        var rend = go.GetComponent<Renderer>();
        Color c = new Color(1f, 0.5f, 0.15f);
        if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", c);
        rend.material.color = c;
        var rocket = go.AddComponent<Rocket>();
        rocket.speed = w.projectileSpeed;
        rocket.blastRadius = w.blastRadius;
        rocket.damage = w.blastDamage;
        rocket.blastForce = w.blastForce;
        rocket.selfForce = w.selfForce;
        rocket.selfDamageScale = w.selfDamageScale;
        rocket.damageScale = DamageScale; // sampled at launch — your speed when you fired
        rocket.Launch(aim.forward, hitMask, gameObject); // travel mask excludes us -> fire at your feet to rocket-jump
    }

    void Tracer(Vector3 a, Vector3 b, Color col)
    {
        if (pool == null) return;
        int idx = poolNext;
        poolNext = (poolNext + 1) % pool.Length;
        var lr = pool[idx];
        lr.startColor = lr.endColor = col;
        if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", col);
        lr.material.color = col;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.enabled = true;
        poolHide[idx] = Time.time + tracerTime;
    }
}
