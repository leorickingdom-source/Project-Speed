using UnityEngine;

// Highground passive: your damage scales with altitude.
//
// Measured as ABSOLUTE world height above groundLevel, not height above whatever surface
// is directly beneath you. A downward raycast would read ~0 while you are standing on the
// 8m deck — which is exactly the situation "high ground" is supposed to mean. The cost of
// absolute measurement is that groundLevel is per-map and has to be set for a new arena.
//
// Pairs with the map rather than with movement tech: decks, grapple perches and jump-pad
// apexes become damage positions, so verticality is worth contesting. Where Momentum
// rewards arriving fast, this rewards arriving high.
//
// No hold/decay window (unlike MomentumDamage) because altitude is already continuous —
// you stand on a deck for as long as you like. If dropping off a deck mid-fight feels bad,
// a hold window is the same shape as the one in MomentumDamage.
public class HighgroundDamage : MonoBehaviour
{
    [Tooltip("Damage bonus at fullBonusHeight and above. 0.4 = +40%, matching Momentum. Do NOT " +
             "raise past 0.5 without rechecking breakpoints: at 1.5x a sniper body shot is " +
             "exactly 150 and silently becomes a one-shot.")]
    public float maxBonus = 0.4f;
    [Tooltip("World Y treated as the arena floor. PER-MAP — set this when building a new arena.")]
    public float groundLevel = 0f;
    [Tooltip("Height above groundLevel where the bonus starts. 2 sits above a normal jump " +
             "(~1.45m) so hopping on the spot earns nothing; you have to actually be elevated.")]
    public float rampStartHeight = 2f;
    [Tooltip("Height at which the bonus is fully earned. 10 is around jump-pad apex (~9m) and " +
             "above the 8m decks, so the decks pay well and the very top pays out fully.")]
    public float fullBonusHeight = 10f;

    PassiveLoadout passives;

    // Returns 1.0 when the passive is not equipped, so callers never branch on it.
    public float Scale
    {
        get
        {
            if (passives == null || !passives.Has(PassiveType.Highground)) return 1f;
            float span = Mathf.Max(0.01f, fullBonusHeight - rampStartHeight);
            float t = Mathf.Clamp01((transform.position.y - groundLevel - rampStartHeight) / span);
            return 1f + maxBonus * t;
        }
    }

    void Awake() => passives = GetComponent<PassiveLoadout>();
}
