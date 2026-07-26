// Named passive kinds. A player brings exactly ONE (see PassiveLoadout).
//
// Grapple is deliberately NOT in this list. It is a BASELINE mechanic every player has,
// not a choice: the core traversal verb should not cost a loadout slot, or half the
// players are not playing the movement game at all. Same reasoning as Warsow's dash and
// Titanfall's wall-run — the signature verb is free, and choices sit on top of it.
//
// Momentum and WallJump are baseline for the same reason and are kept here ONLY so old
// serialized picks still decode: these names are written as INTEGERS into the prefab, the
// scenes and the PassiveLoadout SyncVar, so deleting an entry renumbers every one after it
// and silently turns saved Featherweight picks into someone else's passive.
public enum PassiveType
{
    None,          // no passive equipped
    Vitality,      // +max HP (amount owned by PlayerHealth)
    Momentum,      // BASELINE, not a pick — damage scales with your speed (see MomentumDamage)
    Featherweight, // narrower capsule = harder to hit (radius owned by PlayerMotor)
    Dash,          // burst move in your input direction, on a cooldown (owned by PlayerMotor)
    DoubleJump,    // one extra jump per airtime, refunded on landing (owned by PlayerMotor)
    WallJump,      // kick off walls in mid-air, alternating surfaces (owned by PlayerMotor)
    Highground,    // damage scales with altitude (see HighgroundDamage)
    Camper,        // joke: huge damage while nearly still, gone the moment you move
}
