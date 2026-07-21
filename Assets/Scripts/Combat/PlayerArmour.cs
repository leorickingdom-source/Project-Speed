using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Armour: a second, spendable health pool that soaks part of every hit.
//
// It exists to make the map worth moving through. Health pickups only matter once you have
// already been hurt, so a player at full HP has no reason to contest one — armour is worth
// taking at ANY time, which turns its spawn into a permanent decision rather than a
// conditional one. That is the whole reason arena shooters put armour on a timer.
//
// Soaks a FRACTION rather than absorbing damage outright, so armour never makes you immune to
// a burst — it stretches the fight. Full armour on top of full health takes the effective pool
// from 150 to ~250, which is a real advantage without making an armoured player unkillable by
// anything the loadout can put out in one magazine.
//
// Server-authoritative like PlayerHealth, and for the same reason: a client that could write
// its own armour could simply never lose any.
public class PlayerArmour : NetworkBehaviour
{
    [Tooltip("Ceiling. Matches the heavy pickup so one heavy fills you from empty.")]
    public float maxArmour = 100f;

    [Tooltip("Fraction of each incoming hit taken from armour instead of health, while armour " +
             "lasts. 0.6 means an armoured player survives roughly 1.65x the damage of a bare " +
             "one — noticeable, but still killable inside a single engagement.")]
    [Range(0f, 1f)] public float absorbFraction = 0.6f;

    [Tooltip("Lose armour on death. On = armour is a thing you go and earn again, which is what " +
             "keeps the pickup contested. Off would let a player bank it across lives.")]
    public bool clearOnDeath = true;

    readonly SyncVar<float> points = new SyncVar<float>();

    public float Points => points.Value;
    public float MaxArmour => maxArmour;
    public bool HasArmour => points.Value > 0.01f;

    // Same rule as PlayerHealth: the server owns this once spawned, we own it offline.
    bool HasAuthority => !IsSpawned || IsServerStarted;

    // Takes a hit, returns whatever is left over for health. Called by PlayerHealth.Damage,
    // which is the single choke point every damage source already goes through — putting it
    // anywhere else would mean remembering to route splash, contact and fall damage separately.
    public float Absorb(float incoming)
    {
        if (incoming <= 0f || !HasAuthority) return incoming;

        float armour = points.Value;
        if (armour <= 0f) return incoming;

        float soaked = Mathf.Min(incoming * absorbFraction, armour);
        points.Value = armour - soaked;
        return incoming - soaked;
    }

    // Returns false when already full, so the pickup can decline to be consumed — same rule
    // health pickups use, and for the same reason: a wasted resource stops being a decision.
    public bool Add(float amount)
    {
        if (amount <= 0f || !HasAuthority) return false;
        if (points.Value >= maxArmour - 0.01f) return false;
        points.Value = Mathf.Min(maxArmour, points.Value + amount);
        return true;
    }

    public void ClearOnRespawn()
    {
        if (!HasAuthority || !clearOnDeath) return;
        points.Value = 0f;
    }
}
