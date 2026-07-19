using UnityEngine;

// Momentum passive: your damage scales with how fast you are actually moving.
//
// The ramp starts at the ground speed cap ON PURPOSE. PlayerMotor hard-caps running at
// groundSpeed, so a player who just holds W sits at exactly 1.00x forever — every point
// of bonus has to be earned in the air, in a slide, or on the grapple. That makes this a
// mobility reward rather than a flat stat tax, and unlike a hidden +damage% it stays
// readable in PvP: a dangerous opponent is a visibly fast one.
//
// Deliberately NOT applied to self-damage (see Explosion.Detonate) — scaling your own
// rocket-jump blast with speed would punish the exact behaviour this is meant to reward.
[RequireComponent(typeof(PlayerMotor))]
public class MomentumDamage : MonoBehaviour
{
    [Tooltip("Damage bonus once you are at fullBonusSpeed or above. 0.4 = +40%. Do NOT raise " +
             "past 0.5 without rechecking breakpoints: at 1.5x a sniper body shot is exactly " +
             "150, which turns it into a one-shot kill. 1.4x keeps sniper (140) and shotgun " +
             "(112) as 2-shots while sustained weapons gain a lot.")]
    public float maxBonus = 0.4f;
    [Tooltip("Speed the bonus starts ramping from. Matches PlayerMotor.groundSpeed by " +
             "default, so plain running earns nothing at all.")]
    public float rampStartSpeed = 9f;
    [Tooltip("Speed at which the bonus is fully earned. 16 matches the slide ceiling and " +
             "roughly the air ceiling (groundSpeed * flowMax).")]
    public float fullBonusSpeed = 16f;

    PlayerMotor motor;
    PassiveLoadout passives;

    // Returns 1.0 when the passive is not equipped, so callers never branch on it.
    public float Scale
    {
        get
        {
            if (passives == null || !passives.Has(PassiveType.Momentum)) return 1f;
            float span = Mathf.Max(0.01f, fullBonusSpeed - rampStartSpeed);
            float t = Mathf.Clamp01((motor.Speed - rampStartSpeed) / span);
            return 1f + maxBonus * t;
        }
    }

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        passives = GetComponent<PassiveLoadout>();
    }
}
