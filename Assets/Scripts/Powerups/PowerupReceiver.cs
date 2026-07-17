using System.Collections.Generic;
using UnityEngine;

// Central power-up state, lives on the Player. Pickups call Grant(); ability
// scripts (GrappleHook now; Haste/Quad/etc later) poll IsActive()/Remaining().
// Timed model: each grant is a countdown that Update() bleeds down and drops.
public class PowerupReceiver : MonoBehaviour
{
    readonly Dictionary<PowerupType, float> remaining = new();
    readonly List<PowerupType> scratch = new(); // reused each tick, no alloc

    // Grant (or refresh) a timed power-up. Re-picking extends to the longer time.
    public void Grant(PowerupType type, float duration)
    {
        if (duration <= 0f) return;
        remaining.TryGetValue(type, out float cur);
        remaining[type] = Mathf.Max(cur, duration);
    }

    public bool IsActive(PowerupType type) =>
        remaining.TryGetValue(type, out float t) && t > 0f;

    public float Remaining(PowerupType type) =>
        remaining.TryGetValue(type, out float t) ? Mathf.Max(0f, t) : 0f;

    // Fill `into` with the currently-active power-ups (for HUD). Clears first.
    public void GetActive(List<PowerupSlot> into)
    {
        into.Clear();
        foreach (var kv in remaining)
            if (kv.Value > 0f) into.Add(new PowerupSlot(kv.Key, kv.Value));
    }

    void Update()
    {
        if (remaining.Count == 0) return;
        float dt = Time.deltaTime;

        // Snapshot keys so we can Remove() expired ones while walking the set.
        scratch.Clear();
        scratch.AddRange(remaining.Keys);
        foreach (var type in scratch)
        {
            float t = remaining[type] - dt;
            if (t <= 0f) remaining.Remove(type);
            else remaining[type] = t;
        }
    }
}
