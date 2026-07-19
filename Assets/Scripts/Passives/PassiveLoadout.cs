using UnityEngine;

// Which passives this player has equipped. Persistent by design — unlike
// PowerupReceiver there is no timer, no pickup and no expiry; a passive is part of
// the build you bring into the match.
//
// Deliberately a dumb "what's on" list: each consumer owns its own numbers
// (PlayerHealth owns the Vitality HP bonus, GrappleHook would own its own gate).
// That keeps balance values next to the system they affect instead of collecting
// in one god-object as passives get added.
public class PassiveLoadout : MonoBehaviour
{
    [Tooltip("Passives active for this player. Duplicates are harmless.")]
    public PassiveType[] equipped = new PassiveType[0];

    public bool Has(PassiveType type) => System.Array.IndexOf(equipped, type) >= 0;
}
