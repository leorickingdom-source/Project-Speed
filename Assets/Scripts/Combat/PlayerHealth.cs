using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// The player's hit points. Takes damage through IDamageable (bots, other players), dies and
// respawns at the spawn point with a short invulnerability window. No passive regen by
// default (arena-style); set regenPerSec > 0 for a Halo-style recharge.
//
// SERVER-AUTHORITATIVE once networked: HP lives in a SyncVar that only the server writes,
// so every client sees the same health and a client cannot heal itself by editing memory.
// Offline (not spawned) it keeps full local authority, so single-player still works exactly
// as before — that's what HasAuthority encodes.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerHealth : NetworkBehaviour, IDamageable
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

    // The single source of truth for health. Only the server assigns it once spawned;
    // clients receive it and react through OnChange.
    readonly SyncVar<float> hp = new SyncVar<float>();

    public float Hp => hp.Value;
    // Derived rather than stored, so it can never disagree with the synced HP.
    public bool Alive => hp.Value > 0f;
    public bool Invulnerable => Time.time < invulnUntil;

    // Who may mutate health: the server when networked, ourselves when running offline.
    bool HasAuthority => !IsSpawned || IsServerStarted;

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
        hp.Value = MaxHp;
        invulnUntil = Time.time + spawnInvuln;
        // Every client reacts to health changes locally (freeze on death, unfreeze on
        // respawn) even though only the server decides them.
        hp.OnChange += OnHpChanged;
    }

    void OnDestroy() => hp.OnChange -= OnHpChanged;

    void OnHpChanged(float prev, float next, bool asServer)
    {
        if (prev > 0f && next <= 0f) ApplyDeadState();
        else if (prev <= 0f && next > 0f) ApplyAliveState();
    }

    public void Damage(float amount)
    {
        if (!Alive || Invulnerable || amount <= 0f) return;
        // Clients never write health — they ask the server (see PlayerNetwork.ReportHit).
        if (!HasAuthority) return;
        hp.Value = Mathf.Max(0f, hp.Value - amount);
        lastDamageTime = Time.time;
        if (hp.Value <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (!Alive || amount <= 0f || !HasAuthority) return;
        hp.Value = Mathf.Min(MaxHp, hp.Value + amount);
    }

    void Update()
    {
        // Death, respawn timing, out-of-bounds and regen are all authority decisions; clients
        // just render the result. Without this every client would run its own respawn clock.
        if (!HasAuthority) return;

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
            hp.Value = 0f;
            Die();
            return;
        }

        if (regenPerSec > 0f && hp.Value < MaxHp && Time.time - lastDamageTime >= regenDelay)
            hp.Value = Mathf.Min(MaxHp, hp.Value + regenPerSec * Time.deltaTime);
    }

    // Authority-side death: schedule the revive. The visible freeze is applied by
    // ApplyDeadState via the SyncVar callback, so it happens on every client, not just here.
    void Die()
    {
        reviveAt = Time.time + respawnDelay;
        ApplyDeadState();
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        hp.Value = MaxHp; // drives ApplyAliveState everywhere through OnChange
        invulnUntil = Time.time + spawnInvuln;
    }

    void ApplyDeadState()
    {
        if (motor == null) return;
        motor.velocity = Vector3.zero;
        motor.Frozen = true; // freeze the sim until respawn (runtime flag, never serialized)
    }

    void ApplyAliveState()
    {
        if (motor == null) return;
        motor.velocity = Vector3.zero;
        motor.Frozen = false;
    }
}
