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

    // Fired when the passive changes at runtime. Cached consumers (PlayerMotor's radius and
    // dash flag, resolved once in Awake) subscribe to re-resolve; live readers that call
    // Has() every frame don't need it.
    public event System.Action Changed;

    // Guards None so Has(None) is false rather than matching an empty loadout.
    public bool Has(PassiveType type) => type != PassiveType.None && passive == type;

    // Swap the passive at runtime (the testing picker). Fires Changed so cached state updates.
    public void Equip(PassiveType type)
    {
        if (type == passive) return;
        passive = type;
        Changed?.Invoke();
    }
}
