using UnityEngine;
using UnityEngine.InputSystem;

// Data-driven loadout. Number keys select a weapon; left mouse fires (held for automatic
// weapons, click for semi). Hitscan weapons raycast + draw a pooled tracer and can score
// headshots (top slice of a target = bonus damage). Each weapon has a magazine; R reloads.
// Real-player PvP is hitscan; bots use dodgeable Projectiles.
//
// Five hitscan weapons on keys 1-5. The travelling weapons (Bow, Knives, Crossbow) and the
// rocket are all shelved in DefaultLoadout but their fire paths — FireKind.Arrow/Projectile,
// FireArrow(), Projectile.cs, Rocket.cs — stay wired, so any of them returns by uncommenting.
// Hitscan = instant ray. Arrow = dodgeable travelling shot (Projectile, direct damage).
// Projectile = travelling shot that explodes (Rocket, splash + self-knockback).
public enum FireKind { Hitscan, Projectile, Arrow }

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

    [Tooltip("Per-weapon headshot multiplier. 0 = use WeaponController.headMultiplier. Exists " +
             "so one weapon's headshot can be tuned without touching every other weapon's — " +
             "the controller's value is shared, so raising it is a balance change to the whole " +
             "loadout, not to the gun you were actually thinking about.")]
    public float headMultiplierOverride = 0f;

    public float HeadMultiplierOr(float fallback) =>
        headMultiplierOverride > 0f ? headMultiplierOverride : fallback;

    [Header("Damage falloff")]
    // Two points on a damage-vs-distance line, so the SAME fields express both shapes:
    // normal falloff (near=1.0 -> far=0.2, e.g. shotgun) AND inverted (near=0.4 -> far=1.0,
    // e.g. sniper punished up close). Outside the two points the value is clamped.
    // farDistance <= nearDistance disables falloff entirely — a flat, consistent weapon.
    [Tooltip("Distance where nearMultiplier applies. Closer than this is clamped to it.")]
    public float nearDistance = 0f;
    [Range(0f, 2f)] public float nearMultiplier = 1f;
    [Tooltip("Distance where farMultiplier applies. Set <= nearDistance to disable falloff.")]
    public float farDistance = 0f;
    [Range(0f, 2f)] public float farMultiplier = 1f;

    // Damage scale for a hit at this distance.
    public float DamageAtRange(float distance)
    {
        if (farDistance <= nearDistance) return 1f; // falloff disabled
        float t = Mathf.Clamp01((distance - nearDistance) / (farDistance - nearDistance));
        return Mathf.Lerp(nearMultiplier, farMultiplier, t);
    }

    [Header("Projectile (rocket / arrow)")]
    public float projectileSpeed = 40f;
    [Tooltip("Arrow only. Downward accel on the shot. 0 = flat. Realistic drop plus a target " +
             "dashing at 18 m/s is brutal to lead, so keep this small or zero.")]
    public float projectileGravity = 0f;
    [Tooltip("Arrow only. Radius of the sweep used for hit detection.")]
    public float projectileRadius = 0.15f;
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

    [Header("Loadout")]
    public Weapon[] weapons;            // auto-filled with the default 5 if left empty
    [Tooltip("Index into weapons[] that everyone spawns holding. 0 Revolver, 1 Rifle, 2 Sniper, " +
             "3 SMG, 4 Shotgun.")]
    public int startingWeapon = 1;
    [Tooltip("Off = deathmatch with a single weapon: the number keys do nothing and you keep " +
             "what you spawned with. Turn on to restore 1-5 switching once map pickups exist.")]
    public bool allowWeaponSwitching = false;

    [Header("Headshots (hitscan)")]
    public float headMultiplier = 2f;
    [Tooltip("Top fraction of a target's height that counts as a headshot.")]
    [Range(0f, 1f)] public float headFraction = 0.28f;

    [Header("Tracers")]
    public float tracerTime = 0.04f;

    public int Current { get; private set; }

    // Applied once by PlayerNetwork when the owning player spawns, then never changed —
    // the loadout is locked for the match by design.
    public void SetLockedWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;
        Current = Mathf.Clamp(index, 0, weapons.Length - 1);
        reloadDoneAt = 0f;
    }
    public Weapon CurrentWeapon => (weapons != null && Current >= 0 && Current < weapons.Length) ? weapons[Current] : null;
    public string CurrentName => CurrentWeapon != null ? CurrentWeapon.name : "-";
    public int CurrentAmmo => CurrentWeapon != null ? CurrentWeapon.ammo : 0;
    public int CurrentMag => CurrentWeapon != null ? CurrentWeapon.magSize : 0;
    public bool Reloading => Time.time < reloadDoneAt;

    MomentumDamage momentum;
    HighgroundDamage highground;
    CamperDamage camper;
    PlayerNetwork net;                       // null offline
    HitFeedback feedback;                    // owner-only shot confirmation
    PlayerAudio audioFx;
    TracerRenderer tracers;                  // stays active on remote players too
    readonly RaycastHit[] rayHits = new RaycastHit[16];

    // Combined damage-passive multiplier. Each source returns 1 when not equipped, and
    // pick-one means at most one is ever above 1 — multiplying keeps it correct either way
    // and means a new damage passive only has to be added here.
    public float DamageScale => (momentum != null ? momentum.Scale : 1f)
                              * (highground != null ? highground.Scale : 1f)
                              * (camper != null ? camper.Scale : 1f);

    float nextFire;
    float reloadDoneAt;

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
        momentum = GetComponent<MomentumDamage>();     // optional — absent means no speed bonus
        highground = GetComponent<HighgroundDamage>(); // optional — absent means no height bonus
        camper = GetComponent<CamperDamage>();         // optional — absent means no standstill bonus
        if (aim == null) { var c = GetComponentInChildren<Camera>(); if (c) aim = c.transform; }
        // NOTE: the player layer is deliberately NOT stripped from hitMask. It used to be,
        // which meant every player was invisible to hitscan and nobody could damage anyone.
        // Self-hits are excluded per-hit by transform root instead (see NearestHitIgnoringSelf).
        net = GetComponent<PlayerNetwork>();
        feedback = GetComponent<HitFeedback>();
        audioFx = GetComponent<PlayerAudio>();
        tracers = GetComponent<TracerRenderer>();
        if (weapons == null || weapons.Length == 0) weapons = DefaultLoadout();
        foreach (var w in weapons) w.ammo = w.magSize; // start loaded
        Current = Mathf.Clamp(startingWeapon, 0, weapons.Length - 1);
    }

    static Weapon[] DefaultLoadout() => new[]
    {
        // Falloff is what gives these weapons distinct ROLES. Raw DPS stays bunched (64-86)
        // on purpose: with a locked loadout, a strictly-stronger weapon would just be the one
        // correct pick. Differentiating by WHERE the damage applies keeps every choice live.

        // Revolver: the precision pick. Six rounds, semi-auto, and a 3-shot body kill — every
        // trigger pull is a third of a kill, so a miss costs more than it does on any other gun.
        //
        // Replaces the old Pistol, which was a 15-round 22-damage automatic described as "never
        // great, never punished". That is a description of a weapon nobody has an opinion about:
        // its 7-shot kill made it strictly worse than the Rifle's 11 faster ones, and armour
        // pushed it to a 3.08s kill, the slowest in the game. The fix was never more damage per
        // second — it was giving it a reason to exist.
        //
        // Keeps zero falloff, which is now the point rather than a consolation: it is the only
        // weapon in the loadout that hits exactly as hard at 90m as at 2m. That makes it a real
        // long-range threat for someone accurate WITHOUT stepping on the Sniper, which still
        // needs fewer shots at range and still gets punished under 10m. The Revolver trades
        // the Sniper's raw efficiency for never having a bad distance.
        //
        // Semi-auto on purpose (automatic = false -> one shot per click). Held-fire would let
        // the cycle time do the aiming; a deliberate trigger pull is what makes landing three
        // in a row on a bhopping target feel earned.
        new Weapon { name = "Revolver", kind = FireKind.Hitscan, automatic = false, cycle = 0.55f,
                     damage = 65f, pellets = 1, spreadDegrees = 0f,  range = 200f, tracer = new Color(1.00f, 0.72f, 0.40f),
                     magSize = 6, reloadTime = 1.4f },
        // Rifle: honest all-rounder, mild taper so it never dominates the sniper lane.
        new Weapon { name = "Rifle",  kind = FireKind.Hitscan, automatic = true,  cycle = 0.11f,
                     damage = 14f, pellets = 1, spreadDegrees = 1.5f, range = 200f, tracer = new Color(1.00f, 0.80f, 0.35f),
                     nearDistance = 45f, nearMultiplier = 1f, farDistance = 90f, farMultiplier = 0.7f,
                     magSize = 30, reloadTime = 1.6f },
        // Rocket SHELVED (not deleted) — rocket-jumping is a Quake signature and this game
        // is deliberately diverging from it. Everything it needs still works: FireKind
        // .Projectile, FireProjectile(), Rocket.cs and Explosion.cs are all intact, so
        // restoring it is re-adding this entry (plus a digit key below) and nothing else.
        // new Weapon { name = "Rocket", kind = FireKind.Projectile, automatic = true,  cycle = 0.9f,
        //              projectileSpeed = 40f, blastRadius = 5f, blastDamage = 90f, blastForce = 16f, selfForce = 24f,
        //              magSize = 4, reloadTime = 2.2f },
        // Sniper: INVERTED falloff — 40% under 10m, full past 25m. Being rushed is now the
        // sniper's actual weakness rather than a thing players had to agree to pretend.
        //
        // headMultiplierOverride 3x (300 damage), not the shared 2x, because ARMOUR moved the
        // one-shot threshold. Armour soaks min(dmg*0.6, 100), so past 166 damage the soak is
        // capped at 100 and the rest lands on health: a one-shot through full armour needs
        // dmg - 100 >= 150, i.e. 250. 300 clears it by 50 — the SAME margin a 200-damage
        // headshot had over 150 health before armour existed, so the sniper's identity is
        // restored rather than merely rescued at the boundary.
        //
        // Deliberately NOT done by raising the controller's shared headMultiplier: that value
        // is used by every hitscan weapon, so moving it would rebalance the Rifle, SMG, Revolver
        // and Shotgun at the same time.
        //
        // Close range is untouched by this: 300 * 0.4 = 120 under 10m, still not a one-shot,
        // so rushing a sniper is exactly as correct as it was.
        new Weapon { name = "Sniper", kind = FireKind.Hitscan, automatic = true,  cycle = 1.2f,
                     damage = 100f, pellets = 1, spreadDegrees = 0f, range = 400f, tracer = new Color(0.40f, 0.90f, 1.00f),
                     headMultiplierOverride = 3f,
                     nearDistance = 10f, nearMultiplier = 0.4f, farDistance = 25f, farMultiplier = 1f,
                     magSize = 5, reloadTime = 1.8f },
        // SMG: close-mid pressure, gutted at range so it cannot contest the lane.
        new Weapon { name = "SMG",    kind = FireKind.Hitscan, automatic = true,  cycle = 0.07f,
                     damage = 9f,  pellets = 1, spreadDegrees = 3.5f, range = 150f, tracer = new Color(0.80f, 0.90f, 1.00f),
                     nearDistance = 20f, nearMultiplier = 1f, farDistance = 45f, farMultiplier = 0.4f,
                     magSize = 30, reloadTime = 1.4f },
        // Shotgun: brutal inside 8m, nearly harmless by 20m. Must close to matter.
        //
        // 13 per pellet (104 a shell), raised from 10, ENTIRELY to survive armour. Armour soaks
        // a fraction of each hit, so it costs every weapon roughly the same percentage — but it
        // is paid in shots, and players count shots. Going 11 -> 18 is a weapon that got worse;
        // going 2 -> 4 is a weapon that lost its identity, because "two shells" WAS the identity.
        // At 10 the shotgun ended up on a 2.10s armoured kill, slower than the Rifle's 1.87s,
        // which made the one weapon that has to walk into punching range the slowest in the game.
        //
        // 13 restores 2 shells bare / 3 through full armour, and holds both of those against
        // Vitality (190) too. Bare performance is deliberately unchanged: this is an anti-armour
        // correction, not a buff to the gun.
        new Weapon { name = "Shotgun", kind = FireKind.Hitscan, automatic = true, cycle = 0.7f,
                     damage = 13f, pellets = 8, spreadDegrees = 8f,  range = 40f,  tracer = new Color(1.00f, 0.75f, 0.35f),
                     nearDistance = 8f, nearMultiplier = 1f, farDistance = 20f, farMultiplier = 0.2f,
                     magSize = 6, reloadTime = 1.8f },
        // Bow / Knives / Crossbow SHELVED (not deleted). All-projectile play was the problem:
        // direct-hit only (no splash), so a miss is worth nothing, against the fastest
        // movement in the game — near-misses never punish and fast players become unhittable.
        // FireKind.Arrow, FireArrow() and Projectile.cs are all intact, so restoring any of
        // these is uncommenting the entry plus its digit key. Values kept for that.
        // new Weapon { name = "Bow", kind = FireKind.Arrow, automatic = false, cycle = 0.85f,
        //              damage = 90f, projectileSpeed = 75f, projectileGravity = 0f, projectileRadius = 0.15f,
        //              tracer = new Color(0.85f, 0.80f, 0.60f),
        //              magSize = 5, reloadTime = 1.6f },
        // new Weapon { name = "Knives", kind = FireKind.Arrow, automatic = true, cycle = 0.22f,
        //              damage = 26f, projectileSpeed = 55f, projectileGravity = 4f, projectileRadius = 0.12f,
        //              tracer = new Color(0.75f, 0.80f, 0.85f),
        //              magSize = 12, reloadTime = 1.3f },
        // new Weapon { name = "Crossbow", kind = FireKind.Arrow, automatic = true, cycle = 0.35f,
        //              damage = 38f, projectileSpeed = 90f, projectileGravity = 0f, projectileRadius = 0.13f,
        //              tracer = new Color(0.70f, 0.65f, 0.55f),
        //              magSize = 8, reloadTime = 1.5f },
    };

    void Update()
    {
        if (Time.timeScale == 0f) return;   // no firing / switching while paused
        if (KeybindsUI.Open) return;        // nor while a click is being read as a binding
        var kb = Keyboard.current;
        if (kb != null && weapons != null)
        {
            // Deathmatch default: one weapon, no switching. The number keys are ignored
            // entirely rather than clamped, so a stray press cannot silently swap your gun.
            if (allowWeaponSwitching)
            {
                int prev = Current;
                if (kb.digit1Key.wasPressedThisFrame) Current = 0;
                if (kb.digit2Key.wasPressedThisFrame) Current = 1;
                if (kb.digit3Key.wasPressedThisFrame) Current = 2;
                if (kb.digit4Key.wasPressedThisFrame) Current = 3;
                if (kb.digit5Key.wasPressedThisFrame) Current = 4;
                // digit6-8 unbound while Bow / Knives / Crossbow are shelved.
                Current = Mathf.Clamp(Current, 0, weapons.Length - 1);
                if (Current != prev) reloadDoneAt = 0f;   // switching cancels a reload
            }
        }

        // Remappable, unlike the weapon-slot digits above: reload is pressed constantly, the
        // slots are off entirely in the default deathmatch mode.
        if (Keybinds.Pressed(GameAction.Reload)) StartReload();

        Weapon w = CurrentWeapon;

        // Finish an in-progress reload.
        if (w != null && reloadDoneAt > 0f && Time.time >= reloadDoneAt)
        {
            w.ammo = w.magSize;
            reloadDoneAt = 0f;
        }

        if (w != null && !Reloading)
        {
            bool wantFire = w.automatic
                ? Keybinds.Held(GameAction.Fire)
                : Keybinds.Pressed(GameAction.Fire);
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

        // Tracer expiry moved to TracerRenderer, which stays active on remote players too.
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

        // Local first, so your own shot is instant. Then tell everyone else.
        if (audioFx != null) audioFx.PlayFire(Current);
        if (net != null && net.IsSpawned) net.ReportFire(Current);

        if (w.kind == FireKind.Projectile) FireProjectile(w);
        else if (w.kind == FireKind.Arrow) FireArrow(w);
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
            if (NearestHitIgnoringSelf(origin, dir, w.range, out RaycastHit hit))
            {
                end = hit.point;
                var hp = hit.collider.GetComponentInParent<IDamageable>();
                if (hp != null)
                {
                    // Headshot: hit lands in the top slice of the target's collider.
                    bool head = Headshot.IsHead(hit.collider, hit.point, headFraction);
                    // Falloff is per-pellet: each shotgun pellet has its own travel distance.
                    float dmg = (head ? w.damage * w.HeadMultiplierOr(headMultiplier) : w.damage)
                                * scale * w.DamageAtRange(hit.distance);
                    ApplyDamage(hit.collider, hp, dmg);
                }
            }
            Tracer(origin - aim.up * 0.15f, end, w.tracer);
        }
    }

    // Dodgeable travelling shot (bow / knives). Same spawn shape as FireProjectile but it
    // builds a Projectile — direct damage, no splash, no self-knockback — so these can't
    // be used to launch yourself the way the shelved rocket could.
    void FireArrow(Weapon w)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = w.name;
        go.transform.position = aim.position + aim.forward * 0.6f;
        go.transform.localScale = new Vector3(0.08f, 0.25f, 0.08f);
        Destroy(go.GetComponent<Collider>()); // Projectile sweeps with SphereCast
        var rend = go.GetComponent<Renderer>();
        if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", w.tracer);
        rend.material.color = w.tracer;

        var proj = go.AddComponent<Projectile>();
        proj.speed = w.projectileSpeed;
        proj.gravity = w.projectileGravity;
        proj.castRadius = w.projectileRadius;
        proj.headMultiplier = headMultiplier;
        proj.headFraction = headFraction;
        proj.damageScale = DamageScale;  // sampled at launch, like the rocket
        proj.Launch(aim.forward, w.damage, hitMask, gameObject);
    }

    // Closest hit that isn't part of this player. Needed because the player layer must stay
    // IN hitMask for players to be shootable at all, which also means our own capsule sits
    // right on the muzzle — a plain Raycast would hit ourselves and block every shot.
    bool NearestHitIgnoringSelf(Vector3 origin, Vector3 dir, float range, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, range, hitMask,
            QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider.transform.root == transform.root) continue; // ourselves
            if (rayHits[i].distance >= bestDist) continue;
            bestDist = rayHits[i].distance;
            best = rayHits[i];
            found = true;
        }
        return found;
    }

    // Networked players must be damaged by the SERVER or the hit only exists on the shooter's
    // screen. Anything without a NetworkObject (dummies, offline play) is applied locally.
    void ApplyDamage(Collider victim, IDamageable hp, float damage)
    {
        // Confirm to the shooter immediately, before any network round-trip. This is the one
        // cue that has to feel instant, and it matches the trust model anyway — the client
        // already decides its own hits (see PlayerNetwork.ReportHit).
        if (feedback != null) feedback.ShowHit();

        var nob = victim.GetComponentInParent<FishNet.Object.NetworkObject>();
        if (net != null && net.IsSpawned && nob != null) net.ReportHit(nob, damage);
        else hp.Damage(damage);
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

    // Delegates to the always-active renderer, and tells observers so they see the shot too.
    void Tracer(Vector3 a, Vector3 b, Color col)
    {
        if (tracers != null) tracers.Show(a, b, col, tracerTime);
        if (net != null && net.IsSpawned) net.ReportTracer(a, b);
    }

}
