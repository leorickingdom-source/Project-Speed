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
public enum FireKind { Hitscan, Projectile, Arrow, Melee }

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
    [Tooltip("Extra damage for hitting a target with the projectile ITSELF, on top of the blast " +
             "it is standing in. 0 = splash only. Without it a rocket is worth the same whether " +
             "you led the shot or missed by a metre, because a 'direct hit' is just splash at " +
             "distance zero — and the blast centre sits outside the victim's capsule anyway, so " +
             "even a perfect hit only scored 0.87 falloff.")]
    public float directDamage = 0f;
    // Both scaled by sqrt(28/22) = 1.128 when gravity went 22 -> 28, so a rocket jump reaches
    // the same height and a blast throws you the same distance as before the change.
    public float blastForce = 18f;     // knockback to others
    public float selfForce = 27f;      // your own rocket-jump kick
    [Tooltip("Fraction of your own blast you take. Quake halves it so rocket-jumping is " +
             "repeatable; at 1 a single jump costs ~70% of your health.")]
    [Range(0f, 1f)] public float selfDamageScale = 0.5f;

    [Tooltip("Field of view while scoped. 0 = this weapon has no scope. Magnification is the " +
             "whole feature: the sniper is the only gun whose targets are routinely too small " +
             "to resolve at the range it is built for.")]
    public float scopeFov = 0f;

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
             "3 SMG, 4 Shotgun, 5 Rocket. Only the OFFLINE path uses this — networked play is " +
             "overwritten by SetLockedWeapon with the connect-screen pick. It was 1, the Rifle, " +
             "which is now shelved from the menu, so editor testing was handing out a weapon no " +
             "player can choose.")]
    public int startingWeapon = 0;
    [Tooltip("Off = deathmatch with a single weapon: the number keys do nothing and you keep " +
             "what you spawned with. Turn on to restore 1-5 switching once map pickups exist.")]
    public bool allowWeaponSwitching = false;

    [Header("Headshots (hitscan)")]
    public float headMultiplier = 2f;
    [Tooltip("Top fraction of a target's height that counts as a headshot.")]
    [Range(0f, 1f)] public float headFraction = 0.28f;

    [Header("Melee")]
    [Tooltip("Forgiveness radius on the swing sweep — a melee that has to be aimed like a " +
             "sniper is one nobody reaches for in the panic it exists for. Shared by the quick " +
             "melee and by the oddball swing, which is why it lives here rather than on a " +
             "weapon: only one of those two is a weapon.")]
    public float meleeRadius = 0.5f;

    [Tooltip("Damage of the UNIVERSAL quick melee — the one every player has on its own key, " +
             "whatever they are holding. Not lethal, unlike the knife it replaces: that was a " +
             "one-hit kill because it cost you a gun for the entire match, and the same swing " +
             "handed to everyone for free would make every close-range fight a race to press " +
             "V. 70 against 150 HP is three taps from full, which nobody is standing still " +
             "for; it is a finisher and a panic button, not an opener. Momentum-scaled like " +
             "every other outgoing number, so arriving fast turns it into two.")]
    public float quickMeleeDamage = 70f;
    [Tooltip("Reach of the quick melee. Shorter than the knife's 3.5, which was a weapon you " +
             "built a loadout around and needed the range to justify itself. 2.5 is about a " +
             "capsule and a half — you have to actually be there.")]
    public float quickMeleeRange = 2.5f;
    [Tooltip("Seconds between quick melees. Long enough that mashing it is worse than shooting " +
             "with almost anything, which is the point: melee is what you do when shooting is " +
             "not available.")]
    public float quickMeleeCooldown = 0.9f;
    [Tooltip("Quick-melee damage while the Executioner passive is equipped. 108 rather than " +
             "70: two taps at any speed, and ONE above about 27 m/s, where Momentum's second " +
             "tier is nearly maxed. That threshold is the entire design of the pick -- it is " +
             "only reachable on a rope, a rocket launch or a slung release, so the old knife's " +
             "instant kill comes back as something you have to ARRIVE for rather than " +
             "something you spawn with. A full armour plate still survives it (250 effective " +
             "HP is two taps even at speed), which keeps the counterplay a pickup rather than " +
             "a coin flip. Guns are untouched: this is the only passive that scales one verb.")]
    public float executionerMeleeDamage = 108f;

    [Header("Tracers")]
    public float tracerTime = 0.04f;

    public int Current { get; private set; }

    // The weapon you committed to on the connect screen. Current can leave this temporarily
    // (rocket pickup) but always comes back to it.
    int lockedIndex;

    // Applied once by PlayerNetwork when the owning player spawns, then never changed —
    // the loadout is locked for the match by design.
    public void SetLockedWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;
        Current = lockedIndex = Mathf.Clamp(index, 0, weapons.Length - 1);
        reloadDoneAt = 0f;
        // Awake empties the launcher because it is a pickup for everyone else. If it is your
        // PICK, this is the moment that stops being true — without it you would spawn holding
        // an empty tube and have to press R before you could move.
        if (lockedIndex == RocketIndex) weapons[RocketIndex].ammo = weapons[RocketIndex].magSize;
        // Start() may not have run yet when this arrives; Start re-applies visibility too.
        if (knifeView != null) knifeView.SetVisible(Current == KnifeIndex);
    }

    // Rocket pickup collected: swap to the rocket with `rockets` in the tube. The locked
    // weapon comes back when they run out (see the fire path) or on respawn. Called from
    // PlayerNetwork's TargetRpc, so only the collecting player's own client runs it.
    public void GiveRocket(int rockets)
    {
        if (weapons == null || RocketIndex >= weapons.Length) return;
        weapons[RocketIndex].ammo = Mathf.Max(1, rockets);
        Current = RocketIndex;
        reloadDoneAt = 0f; // a reload mid-swap would otherwise finish on the wrong weapon
        // Even a knife player holds the launcher while it has rockets — it is the one thing
        // that gives that loadout a ranged answer, briefly.
        if (knifeView != null) knifeView.SetVisible(false);
    }
    public Weapon CurrentWeapon => (weapons != null && Current >= 0 && Current < weapons.Length) ? weapons[Current] : null;
    public string CurrentName => CurrentWeapon != null ? CurrentWeapon.name : "-";
    public int CurrentAmmo => CurrentWeapon != null ? CurrentWeapon.ammo : 0;
    public int CurrentMag => CurrentWeapon != null ? CurrentWeapon.magSize : 0;
    // Ammoless weapons print a dash instead of "0 / 0", which otherwise reads as empty —
    // the exact opposite of "this one never runs out".
    public bool CurrentIsAmmoless => CurrentWeapon != null && CurrentWeapon.magSize <= 0;
    public bool Reloading => Time.time < reloadDoneAt;

    // Knife or objective swing. Read by InputReader to decide whether a fire press is a
    // SWING, which the grapple treats as a commitment (see GrappleHook.meleeDropsHook).
    public bool CurrentIsMelee => CurrentWeapon != null && CurrentWeapon.kind == FireKind.Melee;

    // True while the scope is held on a weapon that has one. Read by SpeedFeel (camera FOV),
    // MouseLook (sensitivity) and the HUD (overlay) — the state lives here because the WEAPON
    // decides whether scoping is even possible.
    public bool Scoped { get; private set; }

    // Mouse sensitivity multiplier so a scoped flick moves the same distance ON SCREEN as an
    // unscoped one. Without it, magnification multiplies your aim error by the same factor it
    // multiplies the target, which is worse than no scope at all.
    public float ScopeSensitivityScale
    {
        get
        {
            var w = CurrentWeapon;
            if (!Scoped || w == null || w.scopeFov <= 0f) return 1f;
            float baseFov = Mathf.Max(1f, GameSettings.Fov);
            return Mathf.Clamp(w.scopeFov / baseFov, 0.15f, 1f);
        }
    }
    // 0..1 through the current reload, for the HUD's progress ring. 0 when not reloading.
    public float ReloadProgress01
    {
        get
        {
            if (!Reloading) return 0f;
            float total = reloadDoneAt - reloadStartedAt;
            return total > 0f ? Mathf.Clamp01((Time.time - reloadStartedAt) / total) : 0f;
        }
    }
    // True while this player is carrying an OBJECTIVE — the oddball or the flag. Both are
    // two-handed: your gun is gone, and the objective itself becomes the weapon.
    public bool BallCarrier
    {
        get
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            if (match == null || net == null) return false;
            return match.IsCarrier(net.OwnerId) || match.IsFlagCarrier(net.OwnerId);
        }
    }

    MomentumDamage momentum;
    HighgroundDamage highground;
    CamperDamage camper;
    PlayerNetwork net;                       // null offline
    HitFeedback feedback;                    // owner-only shot confirmation
    PlayerAudio audioFx;
    TracerRenderer tracers;                  // stays active on remote players too
    PlayerHealth health;                     // gates firing: the dead do not shoot
    readonly RaycastHit[] rayHits = new RaycastHit[16];

    // Combined damage-passive multiplier. Each source returns 1 when not equipped, and
    // pick-one means at most one is ever above 1 — multiplying keeps it correct either way
    // and means a new damage passive only has to be added here.
    public float DamageScale => (momentum != null ? momentum.Scale : 1f)
                              * (highground != null ? highground.Scale : 1f)
                              * (camper != null ? camper.Scale : 1f);

    float nextFire;
    float reloadDoneAt;
    float reloadStartedAt;
    KnifeView knifeView;        // owner-only viewmodel, built at runtime
    float meleeNextAt;          // quick melee cooldown, independent of the weapon's cycle
    int preBallIndex;           // weapon to restore when the ball leaves your hands
    MatchManager match; // oddball carrier check; found lazily, null offline

    // Resolved once and again on every loadout change, never asked per swing — the same
    // pattern PlayerMotor and GrappleHook use, so all three agree about what is equipped and
    // none of them puts a lookup on a hot path.
    PassiveLoadout passives;
    bool hasExecutioner;

    void OnDestroy()
    {
        if (passives != null) passives.Changed -= ApplyPassives;
    }

    void ApplyPassives()
    {
        hasExecutioner = passives != null && passives.Has(PassiveType.Executioner);
    }

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
        passives = GetComponent<PassiveLoadout>();     // optional — absent means no passive
        if (passives != null) passives.Changed += ApplyPassives;
        ApplyPassives();
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
        health = GetComponent<PlayerHealth>();
        // Corpses live on Ignore Raycast (layer 2) so a body tumbling next to a fight can
        // never eat a shot. Stripped here rather than trusted from the inspector, because
        // hitMask defaults to ~0 and nobody remembers layer flags during a playtest.
        hitMask &= ~(1 << 2);
        if (weapons == null || weapons.Length == 0) weapons = DefaultLoadout();
        foreach (var w in weapons) w.ammo = w.magSize; // start loaded
        // ...except the rocket, which is a MAP PICKUP. Loading it here handed every player
        // four rockets at spawn. They were unreachable (nothing switches to it without a
        // pickup) so nothing visibly broke — but the first pickup of the match would then
        // grant rockets on top of a tube that was never supposed to have any.
        Current = lockedIndex = Mathf.Clamp(startingWeapon, 0, weapons.Length - 1);
        // Emptied unless the launcher is the loadout — offline play sets that through
        // startingWeapon, networked play through SetLockedWeapon, so both paths need the check.
        if (RocketIndex < weapons.Length && lockedIndex != RocketIndex)
            weapons[RocketIndex].ammo = 0;
    }

    // The viewmodel is built here rather than placed on the prefab so it can never be half
    // configured in the inspector, and so it finds the camera after PlayerNetwork has decided
    // which camera survives. Start() runs after that.
    void Start()
    {
        if (aim == null) return;
        knifeView = gameObject.AddComponent<KnifeView>();
        knifeView.Build(aim);
        knifeView.SetVisible(Current == KnifeIndex);
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
        // Falloff added after playtest: zero falloff made it a BETTER SNIPER THAN THE SNIPER.
        // At 90m both are 0 spread and 100% hit, but the Revolver killed in 1.10s against the
        // Sniper's 1.20s — and unlike the Sniper it is not punished under 10m, so it simply had
        // no bad range. "Consistent" is a fine identity; "no weakness at any distance" is not.
        // Full damage to 25m keeps it the most dependable gun at the ranges fights actually
        // happen at, and the taper past that hands the long lane back to the Sniper.
        //
        // Automatic (hold to fire) rather than semi. At this cycle it is comfort, not
        // power: a human clicks far faster than the gun fires, so the cycle was already
        // doing the gating and the click was only ever making the player's hand do the work.
        //
        // Playtest: still reading as too good. Light touch, identity intact: cycle 0.55 -> 0.6
        // (3-shot kill 1.10s -> 1.20s, level with the sniper's 2-shot instead of beating it)
        // and full-damage range 25m -> 20m so the taper starts inside mid-range fights rather
        // than after them. Still 65 damage, still a 3-shot body kill.
        new Weapon { name = "Revolver", kind = FireKind.Hitscan, automatic = true, cycle = 0.6f,
                     damage = 65f, pellets = 1, spreadDegrees = 0f,  range = 200f, tracer = new Color(1.00f, 0.72f, 0.40f),
                     nearDistance = 20f, nearMultiplier = 1f, farDistance = 60f, farMultiplier = 0.55f,
                     // Light zoom, not a scope. The Revolver is the other weapon whose whole
                     // identity is that each shot must land, so it gets help SEEING the target
                     // — but at 65 (vs base 90) it magnifies about 1.4x against the sniper's
                     // 2.8x, which reads as leaning in rather than setting up.
                     scopeFov = 65f,
                     magSize = 6, reloadTime = 1.4f },
        // Rifle: honest all-rounder, mild taper so it never dominates the sniper lane.
        new Weapon { name = "Rifle",  kind = FireKind.Hitscan, automatic = true,  cycle = 0.11f,
                     damage = 14f, pellets = 1, spreadDegrees = 1.5f, range = 200f, tracer = new Color(1.00f, 0.80f, 0.35f),
                     nearDistance = 45f, nearMultiplier = 1f, farDistance = 90f, farMultiplier = 0.7f,
                     // Same light zoom as the Revolver: the Rifle is the all-rounder, so it
                     // should be able to take a considered shot at 60m without being handed
                     // the sniper's magnification.
                     scopeFov = 70f,
                     magSize = 30, reloadTime = 1.6f },
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
                     // Scoped view. Costs peripheral vision — in a game where people close
                     // 20m in a second, tunnel vision IS the price of the magnification.
                     scopeFov = 32f,
                     nearDistance = 10f, nearMultiplier = 0.4f, farDistance = 25f, farMultiplier = 1f,
                     // 1.5s, down from 1.8 (playtest). The sniper already pays for its power
                     // twice — a 1.2s cycle AND a 5-round magazine — so the reload was a third
                     // tax on the same weakness. Still the joint-longest in the game.
                     magSize = 5, reloadTime = 1.5f },
        // SMG: close-mid pressure, gutted at range so it cannot contest the lane.
        //
        // 45 rounds, not the 30 it shared with the Rifle. Identical magazines gave the SPRAY
        // weapon worse ammo economy than the precision one: 17 shots a kill against the Rifle's
        // 11 means 30 rounds bought 1.8 kills versus 2.7, and armour widened that to 1.07 versus
        // 1.67 — one armoured kill consumed 28 of 30 rounds. A weapon whose whole premise is
        // volume of fire cannot be the one that runs dry first.
        // 45 also puts its held-fire duration (45 * 0.07 = 3.15s) level with the Rifle's 3.19s,
        // so "how long can I hold the trigger" stops being the axis they differ on. What still
        // separates them is range: spread alone drops the SMG to a low hit rate at 30m.
        //
        // Playtest: "not rewarding". 9 -> 10 damage (kill 17 -> 15 hits, DPS 129 -> 143) and
        // spread 3.5 -> 3.0 so more of the stream actually connects inside its own bracket.
        // The range identity is untouched — falloff still guts it past 20m — this is making
        // its GOOD range feel good rather than letting it reach further.
        new Weapon { name = "SMG",    kind = FireKind.Hitscan, automatic = true,  cycle = 0.07f,
                     damage = 10f, pellets = 1, spreadDegrees = 3.0f, range = 150f, tracer = new Color(0.80f, 0.90f, 1.00f),
                     nearDistance = 20f, nearMultiplier = 1f, farDistance = 45f, farMultiplier = 0.4f,
                     magSize = 45, reloadTime = 1.4f },
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
        //
        // Playtest: "not rewarding". The problem was CONSISTENCY, not ceiling: at 8 degrees a
        // point-blank shell could still miss half its pellets, so the weapon that gambles the
        // most per trigger pull was also the most random about paying out. Spread 8 -> 6.5
        // tightens the pattern inside its 8m kill bracket; cycle 0.7 -> 0.65 softens the cost
        // of a pellet-lottery loss. Falloff unchanged — it still cannot contest 20m.
        new Weapon { name = "Shotgun", kind = FireKind.Hitscan, automatic = true, cycle = 0.65f,
                     damage = 13f, pellets = 8, spreadDegrees = 6.5f, range = 40f,  tracer = new Color(1.00f, 0.75f, 0.35f),
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

        // Rocket — now BOTH a connect-screen loadout AND the map pickup, and the two are the
        // same weapon behaving differently on purpose.
        //
        // It was pickup-only because rocket-jumping-as-baseline is a Quake signature this game
        // diverges from. What that missed: the launcher is the easiest speed in the game, and
        // hiding it behind a 30s map timer meant the easiest speed was also the rarest. As a
        // PICK it costs you a gun — 4 tubes, a 2.6s reload and 0.9s between shots against
        // someone holding a rifle — so choosing it is choosing mobility over rate of fire,
        // which is the same trade the Knife makes at the other extreme.
        //
        // Which behaviour you get keys off lockedIndex, not off Current:
        //   loadout rocket  -> reloads on R, refills on respawn
        //   pickup rocket   -> never reloads, dies with you, and hands your own gun back when
        //                      the tube runs dry (that is the power spike the map fights over)
        // Its weapons[] slot stays 5, so nothing that stores an index has to move.
        // projectileSpeed 40 -> 60. Quake's rocket is ~23 m/s and everything in that game moves
        // at roughly half our speeds; 40 was slower than Quake in relative terms, which made a
        // rocket at range a suggestion — a target sliding at 16 or swinging at 30 simply left
        // before it arrived, and 55m took 1.4s to cross. 60 makes that 0.9s. Deliberately still
        // dodgeable at distance: splash plus a hitscan-fast projectile would make the launcher
        // the best gun at every range instead of the mobility pick with a punishing miss.
        // Rocket-jumping is unaffected either way — the floor is 1m from the muzzle.
        // directDamage 40: a centre-mass hit is ~78 of splash, so 118 total against 150 HP.
        // Deliberately NOT lethal on its own — a direct hit plus any chip damage kills, which
        // makes the airshot the moment it should be without turning every connected rocket
        // into a one-shot. Never applies to the shooter, so rocket jumps cost the same 31.
        new Weapon { name = "Rocket", kind = FireKind.Projectile, automatic = true, cycle = 0.9f,
                     projectileSpeed = 60f, blastRadius = 5f, blastDamage = 90f, directDamage = 40f,
                     blastForce = 18f, selfForce = 27f,   // 1.128x, see the fields — gravity 22 -> 28
                     // 0.35, not Quake's 0.5. Quake hands you armour shards and a health pickup
                     // on every corner; this game has NO regen of any kind, so a 45-per-jump
                     // tax turned the mobility tool into a countdown. 31 still costs a fifth of
                     // your health for a launch that clears 13m.
                     selfDamageScale = 0.35f,
                     tracer = new Color(1.00f, 0.50f, 0.15f),
                     magSize = 4, reloadTime = 2.6f },

        // Knife — a PRIMARY loadout, picked on the connect screen like any gun and locked for
        // the match. Not a sidearm everyone carries: that version let a rifle player keep the
        // instant kill as a free panic button, which is the opposite of a commitment. Choosing
        // it means choosing to own no ranged option at all on a map up to 150m across.
        //
        // The trade is the whole design: nothing whatsoever at range, an unanswerable win
        // inside 3.5m. Cycle 0.75 (not the sidearm's 1.1) because a whiffed swing is already
        // punished by the fact that the other player has a gun — punishing it twice with a
        // long recovery made the pick unusable rather than risky.
        //
        // damage is the lethal constant: the swing either connects and kills, or it misses.
        // magSize 0 marks it ammoless — the HUD reads that rather than printing "0 / 0".
        // automatic: hold to keep swinging. The 0.75s cycle already paces it, so requiring a
        // click per swing only made the player's hand do work the cooldown was doing anyway —
        // the same reasoning that made the Revolver hold-to-fire.
        new Weapon { name = "Knife", kind = FireKind.Melee, automatic = true, cycle = 0.75f,
                     damage = MeleeDamage, range = 3.5f, tracer = new Color(0.85f, 0.92f, 1.00f),
                     magSize = 0, reloadTime = 0f },

        // The oddball itself, swung as a weapon. Not selectable: you are handed it the moment
        // you pick the ball up and it is taken away when you lose it. Slower and shorter than
        // the knife because it is a lump of objective, not a blade — carrying is still a
        // downgrade for everyone except the player who committed to melee anyway.
        new Weapon { name = "Ball", kind = FireKind.Melee, automatic = true, cycle = 1.0f,
                     damage = MeleeDamage, range = 3.0f, tracer = new Color(0.75f, 0.55f, 1.00f),
                     magSize = 0, reloadTime = 0f },
    };

    // Fixed slots in the default loadout. The pickup path, the melee swap and the HUD all
    // need these, and "the last entry" is too easy to silently break by appending a weapon.
    public const int RocketIndex = 5;
    public const int KnifeIndex = 6;
    public const int BallIndex = 7;

    void Update()
    {
        if (Time.timeScale == 0f) return;   // no firing / switching while paused
        if (KeybindsUI.Open) return;        // nor while a click is being read as a binding
        if (ConnectUI.MenuOpen) return;     // nor while the connect panel owns the screen
        // Dead players fire nothing — the corpse camera is third person and the crosshair is
        // gone, so a shot from a dead body was pure ghost damage from the victim's screen.
        // Also freezes reload progress while dead; ResetForRespawn refills anyway.
        if (health != null && !health.Alive) { Scoped = false; return; }
        // Carrying the oddball REPLACES your weapon with the ball. Both hands are on it, so
        // the gun is gone for as long as you hold the objective — but you are not helpless:
        // you can swing the thing. That is the trade the mode is built on, and forcing the
        // slot here means no fire path anywhere can bypass it.
        ApplyBallGate();
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
                if (Current != prev)
                {
                    reloadDoneAt = 0f;   // switching cancels a reload
                    // ...and an EMPTY gun starts its own. Arriving on a dry weapon and having
                    // it sit inert until you press fire (to be told "no") was a hidden click
                    // tax; StartReload's own guards skip the knife and the pickup launcher.
                    var sw = CurrentWeapon;
                    if (sw != null && sw.magSize > 0 && sw.ammo <= 0) StartReload();
                }
            }
        }

        // Remappable, unlike the weapon-slot digits above: reload is pressed constantly, the
        // slots are off entirely in the default deathmatch mode.
        if (Keybinds.Pressed(GameAction.Reload)) StartReload();

        // Quick melee, deliberately OUTSIDE the !Reloading gate below: being mid-reload with
        // someone in your face is the single most common reason to swing, and a melee that
        // politely waits for the magazine is a melee that is never there when it is needed.
        // It does not cancel the reload either — the swing happens, the mag keeps filling.
        if (Keybinds.Pressed(GameAction.Melee)) TryQuickMelee();

        Weapon w = CurrentWeapon;

        // Scope state. Dropped during a reload: working the bolt with your eye in the glass
        // is exactly the moment you most need to see someone arriving.
        Scoped = w != null && w.scopeFov > 0f && !Reloading && Keybinds.Held(GameAction.Scope);

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
                // magSize 0 means ammoless (the knife) — it can always swing, and must never
                // fall into the auto-reload branch below looking for rounds it does not use.
                if (w.magSize <= 0)
                {
                    Fire(w);
                    nextFire = Time.time + w.cycle;
                }
                else if (w.ammo > 0)
                {
                    Fire(w);
                    w.ammo--;
                    nextFire = Time.time + w.cycle;
                    // Last rocket out -> the PICKUP is spent, back to your own gun. Skipped when
                    // the launcher IS your gun: there is nothing to hand back, and the empty
                    // tube should reload instead (see the else branch below).
                    if (Current == RocketIndex && w.ammo <= 0 && lockedIndex != RocketIndex)
                    {
                        Current = lockedIndex;
                        // ...and put the viewmodel back with it. A Knife player who spent a
                        // rocket pickup would otherwise finish the life swinging an invisible
                        // blade, since GiveRocket had hidden it.
                        if (knifeView != null)
                            knifeView.SetMode(Current == KnifeIndex ? KnifeView.Mode.Knife
                                                                    : KnifeView.Mode.None);
                    }

                    // Reload starts the INSTANT the mag runs dry, not on the next trigger pull
                    // — the pull that used to start it is the one that loses a fight (playtest
                    // ask: "automatic reload when no ammo"). Read back through CurrentWeapon,
                    // because the pickup-launcher branch above may just have swapped the slot:
                    // a handed-back gun with rounds left must not be reloaded out from under
                    // the player, while a handed-back EMPTY one should start immediately.
                    var cw = CurrentWeapon;
                    if (cw != null && cw.magSize > 0 && cw.ammo <= 0) StartReload();
                }
                else StartReload(); // clicked a gun that was ALREADY empty -> reload it
            }
        }

        // Tracer expiry moved to TracerRenderer, which stays active on remote players too.
    }

    void StartReload()
    {
        // A PICKUP launcher is refilled by the map, not by R. A launcher you chose on the
        // connect screen is your gun and reloads like one — same slot, told apart by whether
        // it is what you locked in.
        if (Current == RocketIndex && lockedIndex != RocketIndex) return;
        var w = CurrentWeapon;
        if (w != null && !Reloading && w.ammo < w.magSize)
        {
            reloadStartedAt = Time.time;
            reloadDoneAt = Time.time + w.reloadTime;
        }
    }

    // Fresh life, fresh mags. Called by PlayerHealth on respawn. Fixes the death-mid-reload
    // trap: dying while reloading left reloadDoneAt in the future and the mag at 0, and with
    // firing blocked while dead the reload could never complete — the sniper (longest reload,
    // smallest mag) respawned with 0 rounds and a stuck "reloading" that never finished.
    public void ResetForRespawn()
    {
        if (weapons == null) return;
        foreach (var w in weapons) w.ammo = w.magSize;
        reloadDoneAt = 0f;
        nextFire = 0f;
        // Back to the loadout you committed to, whatever you happened to be holding when you
        // died — a rocket from a pickup, or the objective itself.
        Current = lockedIndex;
        preBallIndex = lockedIndex;   // stale value here would hand back a spent launcher

        // Unspent PICKUP rockets die with you: the pickup is earned per life, like armour. A
        // loadout launcher respawns loaded, like every other gun (the loop above already did
        // it, so this only has to leave it alone).
        if (RocketIndex < weapons.Length && lockedIndex != RocketIndex)
            weapons[RocketIndex].ammo = 0;

        // Viewmodel follows the LOADOUT, not a swap. This used to force it off, which left a
        // Knife player respawning empty-handed — holding the knife, swinging it, killing with
        // it, with nothing on screen — for the rest of that life.
        if (knifeView != null)
            knifeView.SetMode(Current == KnifeIndex ? KnifeView.Mode.Knife : KnifeView.Mode.None);
    }

    // Force the ball into your hands while you carry it, and give your own weapon back the
    // moment you do not. Runs every frame rather than on a pickup event because the carrier
    // is a synced value owned by the server — the client learns it by observation, and an
    // event we forgot to send would leave someone holding a rifle they should not have.
    void ApplyBallGate()
    {
        if (weapons == null || BallIndex >= weapons.Length) return;

        bool carrying = BallCarrier;
        if (carrying && Current != BallIndex)
        {
            preBallIndex = Current;
            Current = BallIndex;
            reloadDoneAt = 0f;              // whatever was reloading is now on the floor
            if (knifeView != null) knifeView.SetMode(KnifeView.Mode.Ball);
        }
        else if (!carrying && Current == BallIndex)
        {
            Current = preBallIndex;
            if (knifeView != null)
                knifeView.SetMode(Current == KnifeIndex ? KnifeView.Mode.Knife : KnifeView.Mode.None);
            // Handed back an empty gun — start the reload rather than leaving it idle.
            var back = CurrentWeapon;
            if (back != null && back.magSize > 0 && back.ammo <= 0) StartReload();
        }
    }

    // The universal quick melee: every player, every weapon, its own key.
    //
    // It replaces the Knife LOADOUT, which was a genuinely different bargain — no gun at all,
    // in exchange for a 3.5m one-hit kill. That bargain is what justified the lethality, and
    // it is exactly what does not survive being handed to everybody: a free instant kill on a
    // 0.75s cycle would end every close fight before either gun mattered. So this swing is
    // ordinary damage on a cooldown, and the knife weapon stays in weapons[] shelved (see
    // LoadoutChoice) rather than deleted, because the oddball still swings through the same
    // path and every stored slot index still has to mean what it meant.
    void TryQuickMelee()
    {
        if (Time.time < meleeNextAt) return;
        // Carrying the oddball already melees with Fire, and its swing is the one that is
        // supposed to be lethal. Two melee buttons on one weapon is just a way to spend a
        // cooldown you did not mean to.
        if (CurrentIsMelee) return;

        meleeNextAt = Time.time + quickMeleeCooldown;
        // Momentum-scaled, like the guns: 70 becomes 98 at rope speed, which is what turns a
        // three-tap into a two-tap for the player who actually arrived fast. Executioner moves
        // the same curve up a rung -- two taps standing still, one above ~27 m/s.
        float damage = hasExecutioner ? executionerMeleeDamage : quickMeleeDamage;
        Swing(quickMeleeRange, damage * DamageScale, QuickMeleeTracer, quick: true);
    }

    // The weapon-driven melee: the shelved knife, and the oddball you are forced to carry.
    void Melee(Weapon w) => Swing(w.range, w.damage, w.tracer, quick: false);

    // One swing implementation, whoever asked for it.
    //
    // Deliberately no cone or lunge: it lands where the crosshair is, which keeps the
    // counterplay honest — back up, or shoot the person sprinting at you.
    void Swing(float range, float damage, Color tracerColor, bool quick)
    {
        if (aim == null) return;

        // Announced even on a whiff, and visible to everyone. The swing sound and the slash
        // ARE the counterplay cue: hearing or seeing one behind you is what lets you turn
        // around, and a silent invisible whiff would make the approach free.
        if (audioFx != null) audioFx.PlayMelee();
        if (net != null && net.IsSpawned) net.ReportFire(MeleeAudioIndex);
        // A quick melee BORROWS the viewmodel for one swing — your hands are meant to be full
        // of gun — where a knife or ball swing is the thing already in them.
        if (knifeView != null) { if (quick) knifeView.QuickSwing(); else knifeView.Swing(); }
        SlashFx(range, tracerColor);

        // Sweep rather than a thin ray, and take the first thing that is not us. Uses the
        // same self-exclusion rule as hitscan, since our own capsule sits on the muzzle.
        int n = Physics.SphereCastNonAlloc(aim.position, meleeRadius, aim.forward, rayHits,
            range, hitMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        RaycastHit best = default;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider.transform.root == transform.root) continue;
            if (rayHits[i].collider.GetComponentInParent<IDamageable>() == null) continue;
            if (rayHits[i].distance >= bestDist) continue;
            bestDist = rayHits[i].distance;
            best = rayHits[i];
            found = true;
        }
        if (!found) return;

        var target = best.collider.GetComponentInParent<IDamageable>();
        if (target != null) ApplyDamage(best.collider, target, damage, KillKind.Melee);
    }

    // Pale blue, the colour the knife's slash always was — the cue players already read as
    // "somebody swung at me" should not change just because everybody can do it now.
    static readonly Color QuickMeleeTracer = new Color(0.85f, 0.92f, 1.00f);

    // The arc of the swing, drawn through the SAME pooled renderer and network message the
    // bullet tracers use — so a knife swing is as visible to a bystander as a gunshot, on
    // every machine, for no new plumbing. Diagonal because a horizontal line reads as a
    // laser; a slash has to look like it was swung.
    void SlashFx(float range, Color tracerColor)
    {
        Vector3 mid = aim.position + aim.forward * (range * 0.55f);
        Vector3 a = mid - aim.right * 0.85f + aim.up * 0.35f;
        Vector3 b = mid + aim.right * 0.85f - aim.up * 0.35f;
        Tracer(a, b, tracerColor);
    }

    // Past every armour and Vitality combination there is: armour soaks at most 100, so any
    // value over 290 is lethal through a full plate on a Vitality build. 9999 is that with
    // no arithmetic to re-check the day those numbers move.
    public const float MeleeDamage = 9999f;
    // Sentinel weapon index for the fire-audio RPC — PlayerAudio maps it to the swing.
    // Reuses the existing message rather than adding a second one that says the same thing.
    public const int MeleeAudioIndex = -1;

    void Fire(Weapon w)
    {
        if (aim == null) return;

        // Local first, so your own shot is instant. Then tell everyone else. The knife is
        // skipped here and announces itself inside Melee() — routing it through the gunshot
        // table would give a blade the revolver's report.
        if (w.kind != FireKind.Melee)
        {
            if (audioFx != null) audioFx.PlayFire(Current);
            if (net != null && net.IsSpawned) net.ReportFire(Current);
        }

        if (w.kind == FireKind.Projectile) FireProjectile(w);
        else if (w.kind == FireKind.Arrow) FireArrow(w);
        else if (w.kind == FireKind.Melee) Melee(w);
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
                    ApplyDamage(hit.collider, hp, dmg, head ? KillKind.Headshot : KillKind.Normal);
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
    // `kind` rides along so the kill feed can say HOW the kill was earned.
    void ApplyDamage(Collider victim, IDamageable hp, float damage, KillKind kind)
    {
        // Confirm to the shooter immediately, before any network round-trip. This is the one
        // cue that has to feel instant, and it matches the trust model anyway — the client
        // already decides its own hits (see PlayerNetwork.ReportHit).
        if (feedback != null) feedback.ShowHit(kind);

        var nob = victim.GetComponentInParent<FishNet.Object.NetworkObject>();
        if (net != null && net.IsSpawned && nob != null) net.ReportHit(nob, damage, kind);
        else hp.Damage(damage);
    }

    void FireProjectile(Weapon w)
    {
        // From a MUZZLE, not the eye. The rocket used to spawn dead on the camera ray, which
        // made it invisible to the one player who most needs to see it: flying exactly along
        // your own line of sight, it never moves across your screen — it sits behind the
        // crosshair dot and shrinks (playtest: "cant see rocket projectile"). Offset to the
        // right and below, where every FPS puts the launcher, so the shot visibly LEAVES you.
        Vector3 origin = aim.position + aim.right * 0.28f - aim.up * 0.18f + aim.forward * 0.5f;

        // Converge on what the crosshair actually points at, so the offset costs no accuracy.
        // Skipped point-blank (rocket-jump floor shots are ~1-1.7m away), where convergence
        // math degenerates and a blast 0.28m off centre is nothing against a 5m radius.
        Vector3 dir = aim.forward;
        if (NearestHitIgnoringSelf(aim.position, aim.forward, 300f, out RaycastHit aimHit)
            && aimHit.distance > 2f)
            dir = (aimHit.point - origin).normalized;

        // Always spawn locally — the shooter's rocket must leave the barrel THIS frame.
        // Offline and on the host this copy is also the authoritative one (Damage writes go
        // through). On a pure client it is only a visual: every Damage/impulse it lands is
        // authority-refused, so the server is told to fire the REAL one. Same split SimpleBot
        // uses for its shots — visuals everywhere, truth on the server.
        SpawnRocket(origin, dir, Current, DamageScale);
        // The CONVERGED direction, not aim.forward — the server's rocket must fly the same
        // line as the one the shooter is watching, or the visual hits and the real one misses.
        if (net != null && net.IsSpawned && !net.IsServerStarted)
            net.ReportRocket(origin, dir, Current, DamageScale);
    }

    // The one rocket material — static so a match's worth of rockets cannot leak instances.
    static Material rocketMat;

    // Shared by the local fire path and PlayerNetwork's rocket RPCs, so every machine builds
    // the same projectile from the same stats. Owner is always THIS player: the travel mask
    // must exclude the shooter (rocket-jumping fires at your own feet) and self-damage has to
    // know whose feet those are.
    public void SpawnRocket(Vector3 origin, Vector3 dir, int weaponIndex, float damageScale)
    {
        if (weapons == null || weaponIndex < 0 || weaponIndex >= weapons.Length) return;
        Weapon w = weapons[weaponIndex];

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Rocket";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.35f;
        Destroy(go.GetComponent<Collider>()); // Rocket sweeps with SphereCast; no physical collider

        // UNLIT, and with a trail. A lit sphere is whatever the room's light says it is —
        // dim, shadowed on the side you see — and a 60 m/s object with no trail exists on
        // screen for a handful of frames with nothing marking where it has been. The trail is
        // most of the projectile's visibility; every shooter's rocket is really a smoke line.
        // Same shader-with-fallback pattern the grapple rope uses, for the same build reason.
        //
        // ONE material, shared by every rocket ever fired (sharedMaterial, static cache).
        // Destroy(gameObject) does not destroy materials a renderer instantiated, so a
        // per-rocket `new Material` is a leak the length of a match — an automatic launcher
        // at 0.9s/shot is ~1300 dead materials in a twenty-minute session.
        Color c = new Color(1f, 0.55f, 0.15f);
        if (rocketMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            rocketMat = new Material(sh);
            if (rocketMat.HasProperty("_BaseColor")) rocketMat.SetColor("_BaseColor", c);
            rocketMat.color = c;
        }
        go.GetComponent<Renderer>().sharedMaterial = rocketMat;

        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = rocketMat;
        trail.time = 0.3f;
        trail.startWidth = 0.28f;
        trail.endWidth = 0f;
        trail.startColor = c;
        trail.endColor = new Color(c.r, c.g, c.b, 0f);
        trail.numCapVertices = 2;

        var rocket = go.AddComponent<Rocket>();
        rocket.speed = w.projectileSpeed;
        rocket.blastRadius = w.blastRadius;
        rocket.damage = w.blastDamage;
        rocket.blastForce = w.blastForce;
        rocket.directDamage = w.directDamage;
        rocket.selfForce = w.selfForce;
        rocket.selfDamageScale = w.selfDamageScale;
        rocket.damageScale = damageScale; // sampled at launch — the shooter's speed when firing
        rocket.Launch(dir, hitMask, gameObject); // travel mask excludes us -> fire at your feet to rocket-jump
    }

    // Delegates to the always-active renderer, and tells observers so they see the shot too.
    void Tracer(Vector3 a, Vector3 b, Color col)
    {
        if (tracers != null) tracers.Show(a, b, col, tracerTime);
        if (net != null && net.IsSpawned) net.ReportTracer(a, b);
    }

}
