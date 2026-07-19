using UnityEngine;

// The player's hit points. Takes damage through IDamageable (rocket splash, bots, and
// other players later), dies and respawns at the spawn point with a short invulnerability
// window. No passive regen by default (arena-style — grab health pickups); set
// regenPerSec > 0 for a Halo-style recharge.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [Tooltip("BASE max HP, before passives. Read MaxHp for the effective value. 150 keeps the " +
             "sniper a 2-shot body kill (100 dmg) while a headshot (200) still one-shots — so " +
             "aim is rewarded and a body hit leaves a real counterplay window.")]
    public float maxHp = 150f;
    [Tooltip("Extra max HP from the Vitality passive. 40 (not 50) on purpose: at 190 a sniper " +
             "body shot is still a 2-shot and a headshot still one-shots, both with margin. " +
             "50 would land you on exactly 200, where BOTH of those sit at zero margin and any " +
             "later tweak to sniper damage or headMultiplier silently flips lethality.")]
    public float vitalityBonusHp = 40f;
    public float respawnDelay = 1.5f;
    public float spawnInvuln = 2f;         // no damage for this long after (re)spawn

    [Header("Regen (0 = off)")]
    public float regenPerSec = 0f;
    public float regenDelay = 3f;          // idle time after damage before regen starts

    [Tooltip("Where to respawn. Defaults to the player's start pose.")]
    public Transform spawnPoint;

    [Header("Bounds")]
    [Tooltip("Fall below this world Y and you die + respawn. Floor top is y=0, so -10 kills a " +
             "pit fall in ~0.7s instead of the ~2s the old -25 took, while leaving a brief " +
             "window to grapple back out.")]
    public float killY = -10f;
    [Tooltip("Die if you get farther than this from arena center on X or Z — the SIDE " +
             "out-of-bounds, so being launched over a wall by a grapple/dash/pad resets you at " +
             "once instead of arcing out and waiting for the long fall. Square half-extent " +
             "because the arena is square; walls sit at 45.5, so 48 clears them with margin. " +
             "0 = off.")]
    public float killDist = 48f;

    public float Hp { get; private set; }
    public bool Alive { get; private set; } = true;
    public bool Invulnerable => Time.time < invulnUntil;

    // Effective ceiling = base + passives. Everything that clamps or refills reads THIS,
    // never the raw maxHp field, so equipping Vitality can't leave you capped at the base.
    public float MaxHp => maxHp + (passives != null && passives.Has(PassiveType.Vitality)
        ? vitalityBonusHp : 0f);

    PlayerMotor motor;
    PassiveLoadout passives;
    Vector3 spawnPos;
    Quaternion spawnRot;
    float invulnUntil;
    float lastDamageTime = -999f;
    float reviveAt;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        passives = GetComponent<PassiveLoadout>(); // optional — null means no passives equipped
        if (spawnPoint != null) { spawnPos = spawnPoint.position; spawnRot = spawnPoint.rotation; }
        else { spawnPos = transform.position; spawnRot = transform.rotation; }
        Hp = MaxHp;
        invulnUntil = Time.time + spawnInvuln;
    }

    public void Damage(float amount)
    {
        if (!Alive || Invulnerable || amount <= 0f) return;
        Hp = Mathf.Max(0f, Hp - amount);
        lastDamageTime = Time.time;
        if (Hp <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (Alive && amount > 0f) Hp = Mathf.Min(MaxHp, Hp + amount);
    }

    void Update()
    {
        if (!Alive)
        {
            if (Time.time >= reviveAt) Respawn();
            return;
        }

        // Out of bounds -> instant death (invuln doesn't save you from the void). Vertical:
        // fell through the pit or off an edge. Horizontal: launched past the perimeter walls.
        Vector3 p = transform.position;
        if (p.y < killY || (killDist > 0f && (Mathf.Abs(p.x) > killDist || Mathf.Abs(p.z) > killDist)))
        {
            Die();
            return;
        }

        if (regenPerSec > 0f && Hp < MaxHp && Time.time - lastDamageTime >= regenDelay)
            Hp = Mathf.Min(MaxHp, Hp + regenPerSec * Time.deltaTime);
    }

    void Die()
    {
        Alive = false;
        reviveAt = Time.time + respawnDelay;
        motor.velocity = Vector3.zero;
        motor.Frozen = true; // freeze the sim until respawn (runtime flag, never serialized)
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        motor.velocity = Vector3.zero;
        motor.Frozen = false;
        Hp = MaxHp;
        Alive = true;
        invulnUntil = Time.time + spawnInvuln;
    }
}
