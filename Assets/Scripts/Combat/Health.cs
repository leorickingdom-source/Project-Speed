using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Destructible target that respawns so you can keep practicing. Used by the bots.
//
// SERVER-AUTHORITATIVE, for the same reason PlayerHealth is: this used to be a plain
// MonoBehaviour holding a local float, so in a hosted match every client tracked its own copy
// of a bot's health. Two players shooting the same bot each saw it die at a different moment,
// and neither agreed with the server about whether it was there at all.
//
// Offline (never spawned) it keeps full local authority, so the single-player playground works
// exactly as before — that is what HasAuthority encodes.
public class Health : NetworkBehaviour, IDamageable
{
    public float maxHp = 100f;
    public float respawnDelay = 3f;

    readonly SyncVar<float> hp = new SyncVar<float>();

    public float Hp => hp.Value;
    public bool Alive => hp.Value > 0f;

    bool HasAuthority => !IsSpawned || IsServerStarted;

    Collider col;
    float reviveAt;
    bool suppressed; // held down by something else (SimpleBot when its slot is unused)

    void Awake()
    {
        col = GetComponent<Collider>();
        hp.Value = maxHp;
        hp.OnChange += OnHpChanged;
    }

    void OnDestroy() => hp.OnChange -= OnHpChanged;

    // Runs on every client, so the body disappears everywhere rather than only where the
    // damage was applied.
    void OnHpChanged(float prev, float next, bool asServer) => ApplyVisible(next > 0f && !suppressed);

    void ApplyVisible(bool on)
    {
        // Children too, not just our own renderer: the head cap (HeadCapVisual) is a child,
        // and a dead bot leaving its dark cap floating in the air would be a ghost target.
        // Fetched live rather than cached in Awake because the cap is attached after Awake.
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        if (col != null) col.enabled = on;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyVisible(Alive && !suppressed);
    }

    // Lets the bot take itself off the board without pretending to be dead — an unused bot slot
    // is not a corpse waiting to respawn, it is a bot that does not exist this match.
    public void SetSuppressed(bool on)
    {
        suppressed = on;
        ApplyVisible(Alive && !suppressed);
    }

    public void Damage(float amount)
    {
        if (!Alive || amount <= 0f || suppressed) return;
        if (!HasAuthority) return; // clients ask the server; see PlayerNetwork.ReportHit
        hp.Value = Mathf.Max(0f, hp.Value - amount);
        if (hp.Value <= 0f) reviveAt = Time.time + respawnDelay;
    }

    void Update()
    {
        if (!HasAuthority || Alive || suppressed) return;
        if (Time.time >= reviveAt) hp.Value = maxHp;
    }
}
