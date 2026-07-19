using UnityEngine;

// The ONE passive this player brings. Persistent by design — no timer, no pickup and no
// expiry; a passive is part of the build you bring into the match.
//
// A single value rather than a list, on purpose. With a list, every passive has to be
// balanced against simply having it for free, which forces a contrived drawback onto
// each one. Pick-one makes passives compete with each other instead — less code, and
// an actual decision.
//
// Deliberately a dumb "what is on" holder: each consumer owns its own numbers
// (PlayerHealth owns the Vitality HP bonus, MomentumDamage owns its speed ramp), so
// balance values stay next to the system they affect as more passives land.
public class PassiveLoadout : MonoBehaviour
{
    [Tooltip("The single passive this player brings into the match. None = no passive.")]
    public PassiveType passive = PassiveType.None;

    // Guards None so Has(None) is false rather than matching an empty loadout.
    public bool Has(PassiveType type) => type != PassiveType.None && passive == type;
}
