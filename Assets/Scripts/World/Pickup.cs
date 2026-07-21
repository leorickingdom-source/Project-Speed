using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// A contested map resource. Optional — only active when the host enables the Pickups mode.
//
// This is the layer the arena has been missing: decks, tunnels and jump pads are traversal,
// but nothing in the map was ever WORTH going to. A health pickup on a timer turns a location
// into a decision, and because there is no regen, healing is the scarcest thing in the game.
//
// Server-authoritative: the server alone decides a collection and heals, and `available` is a
// SyncVar so every client agrees on whether it is there. Polls an overlap sphere rather than
// using triggers, matching JumpPad — the player has no rigidbody, so trigger callbacks would
// need one added purely for this.
public class Pickup : NetworkBehaviour
{
    public enum Kind { Health, Armour }

    [Header("Effect")]
    public Kind kind = Kind.Health;
    [Tooltip("HP restored, or armour granted. 50 health is a third of base HP — meaningful " +
             "without erasing a fight. 50 armour is half a full plate.")]
    public float amount = 50f;

    [Header("Timing")]
    [Tooltip("Seconds before it returns. Long enough that taking it is worth remembering, " +
             "which is what makes the spot contested rather than free. Armour should sit " +
             "LONGER than health: it is worth taking at any time, so a short timer would make " +
             "holding its spawn the whole game.")]
    public float respawnSeconds = 20f;

    [Header("Look")]
    [Tooltip("Tint the pickup by kind at startup. Two identical boxes that do different things " +
             "is a decision the player cannot make, because they cannot tell which is which.")]
    public bool tintByKind = true;
    public Color healthColor = new Color(0.35f, 0.95f, 0.45f);
    public Color armourColor = new Color(0.40f, 0.70f, 1.00f);

    [Header("Feel")]
    public float radius = 1.6f;
    public float bobHeight = 0.25f;
    public float bobSpeed = 2f;
    public float spinSpeed = 90f;

    readonly SyncVar<bool> available = new SyncVar<bool>(true);

    Renderer[] visuals;
    Vector3 basePos;
    float readyAt;
    MatchManager match;

    void Awake()
    {
        visuals = GetComponentsInChildren<Renderer>(true);
        basePos = transform.position;
        available.OnChange += OnAvailableChanged;
        if (tintByKind) ApplyTint();
    }

    void ApplyTint()
    {
        if (visuals == null) return;
        Color c = kind == Kind.Armour ? armourColor : healthColor;
        foreach (var r in visuals)
        {
            if (r == null) continue;
            var m = r.material; // instance, so two pickups sharing a material stay independent
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
        }
    }

    void OnDestroy() => available.OnChange -= OnAvailableChanged;

    void OnAvailableChanged(bool prev, bool next, bool asServer) => ApplyVisual(next);

    void ApplyVisual(bool on)
    {
        if (visuals == null) return;
        foreach (var r in visuals) if (r != null) r.enabled = on;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyVisual(available.Value && ModeOn);
    }

    // Pickups mode is decided by the host and synced through MatchManager, so a client cannot
    // disagree about whether the resource exists.
    bool ModeOn
    {
        get
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            return match != null && match.PickupsEnabled;
        }
    }

    void Update()
    {
        bool on = ModeOn;

        // Cosmetic idle motion, run everywhere so it looks alive on every client.
        if (on && available.Value)
        {
            transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        if (!IsServerStarted) return;

        if (!on)
        {
            if (available.Value) { available.Value = false; ApplyVisual(false); }
            return;
        }

        if (!available.Value)
        {
            if (Time.time >= readyAt) available.Value = true;
            return;
        }

        TryCollect();
    }

    void TryCollect()
    {
        var hits = Physics.OverlapSphere(basePos, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            var hp = c.GetComponentInParent<PlayerHealth>();
            if (hp == null || !hp.Alive) continue;
            if (!Grant(hp)) continue; // already full for this kind — walk over it, take nothing

            available.Value = false;
            readyAt = Time.time + respawnSeconds;
            return;
        }
    }

    // Returns false when the player cannot use it, so the pickup stays up. A resource consumed
    // for no effect is a resource the map stopped offering — and the player has no way to know
    // it happened.
    bool Grant(PlayerHealth hp)
    {
        if (kind == Kind.Armour)
        {
            var armour = hp.GetComponent<PlayerArmour>();
            return armour != null && armour.Add(amount);
        }

        if (hp.Hp >= hp.MaxHp) return false;
        hp.Heal(amount);
        return true;
    }
}
