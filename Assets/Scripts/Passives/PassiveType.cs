// Named passive kinds. A player brings exactly ONE (see PassiveLoadout).
//
// Grapple is deliberately NOT in this list. It is a BASELINE mechanic every player has,
// not a choice: the core traversal verb should not cost a loadout slot, or half the
// players are not playing the movement game at all. Same reasoning as Warsow's dash and
// Titanfall's wall-run — the signature verb is free, and choices sit on top of it.
public enum PassiveType
{
    None,       // no passive equipped
    Vitality,   // +max HP (amount owned by PlayerHealth)
    Momentum,   // damage scales with your speed (see MomentumDamage)
}
