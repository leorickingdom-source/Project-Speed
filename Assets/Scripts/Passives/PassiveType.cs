// Named passive kinds. Unlike PowerupType these never expire: a passive is equipped
// once and stays on for the life of the player — no pickup, no timer, no contest.
// Only Vitality is wired this pass; Grapple is listed because GrappleHook already has
// the gate (requirePowerup) and is the natural next one to convert.
public enum PassiveType
{
    Vitality,   // +max HP (amount owned by PlayerHealth)
    Grapple,    // grapple with no pickup required
}
