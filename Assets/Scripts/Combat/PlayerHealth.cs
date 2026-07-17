using UnityEngine;

// The player's hit points. Takes damage through IDamageable (rocket splash, bots, and
// other players later), dies and respawns at the spawn point with a short invulnerability
// window. No passive regen by default (arena-style — grab health pickups); set
// regenPerSec > 0 for a Halo-style recharge.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHp = 100f;
    public float respawnDelay = 1.5f;
    public float spawnInvuln = 2f;         // no damage for this long after (re)spawn

    [Header("Regen (0 = off)")]
    public float regenPerSec = 0f;
    public float regenDelay = 3f;          // idle time after damage before regen starts

    [Tooltip("Where to respawn. Defaults to the player's start pose.")]
    public Transform spawnPoint;

    [Header("Bounds")]
    [Tooltip("Fall below this world Y and you die + respawn (out-of-bounds kill plane).")]
    public float killY = -25f;

    public float Hp { get; private set; }
    public bool Alive { get; private set; } = true;
    public bool Invulnerable => Time.time < invulnUntil;

    PlayerMotor motor;
    Vector3 spawnPos;
    Quaternion spawnRot;
    float invulnUntil;
    float lastDamageTime = -999f;
    float reviveAt;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        if (spawnPoint != null) { spawnPos = spawnPoint.position; spawnRot = spawnPoint.rotation; }
        else { spawnPos = transform.position; spawnRot = transform.rotation; }
        Hp = maxHp;
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
        if (Alive && amount > 0f) Hp = Mathf.Min(maxHp, Hp + amount);
    }

    void Update()
    {
        if (!Alive)
        {
            if (Time.time >= reviveAt) Respawn();
            return;
        }

        // Fell out of the world -> instant death (invuln doesn't save you from the void).
        if (transform.position.y < killY) { Die(); return; }

        if (regenPerSec > 0f && Hp < maxHp && Time.time - lastDamageTime >= regenDelay)
            Hp = Mathf.Min(maxHp, Hp + regenPerSec * Time.deltaTime);
    }

    void Die()
    {
        Alive = false;
        reviveAt = Time.time + respawnDelay;
        motor.velocity = Vector3.zero;
        motor.enabled = false; // freeze the sim until respawn
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(spawnPos, spawnRot);
        motor.velocity = Vector3.zero;
        motor.enabled = true;
        Hp = maxHp;
        Alive = true;
        invulnUntil = Time.time + spawnInvuln;
    }
}
