using UnityEngine;

// Momentum passive: your damage scales with how fast you are moving — and, crucially,
// keeps scaling for a moment after you stop.
//
// The ramp starts at the ground speed cap ON PURPOSE. PlayerMotor hard-caps running at
// groundSpeed, so a player who just holds W sits at 1.00x forever — every point of bonus
// has to be earned in the air, in a slide, or on the grapple.
//
// But speed alone made the passive nearly unfeelable: fights happen close to stationary,
// so the bonus collapsed to 1.00x at exactly the moment you were shooting. The bonus now
// RISES instantly with speed and LAGS on the way down (holdTime, then decayPerSec). That
// converts it from "damage while travelling" into "reward for entering a fight fast" —
// slide or grapple in, and the bonus is still up while you fire.
//
// Deliberately NOT applied to self-damage (see Explosion.Detonate).
[RequireComponent(typeof(PlayerMotor))]
public class MomentumDamage : MonoBehaviour
{
    [Tooltip("Damage bonus at fullBonusSpeed and above. 0.4 = +40%. Do NOT raise past 0.5 " +
             "without rechecking breakpoints: at 1.5x a sniper body shot is exactly 150, which " +
             "silently turns it into a one-shot. 1.4x keeps sniper (140) and shotgun (112) as " +
             "2-shots while sustained weapons gain a lot.")]
    public float maxBonus = 0.4f;
    [Tooltip("Speed the bonus starts ramping from. Matches PlayerMotor.groundSpeed by default, " +
             "so plain running earns nothing at all.")]
    public float rampStartSpeed = 9f;
    [Tooltip("Speed at which the bonus is fully earned. 16 matches the slide ceiling and " +
             "roughly the air ceiling (groundSpeed * flowMax).")]
    public float fullBonusSpeed = 16f;

    [Header("Carry into combat")]
    [Tooltip("Seconds the bonus holds at its peak after you slow down. This is what lets the " +
             "passive touch COMBAT at all — without it the bonus is 1.00x exactly when you are " +
             "standing still shooting, which is most of a fight.")]
    public float holdTime = 1f;
    [Tooltip("How fast the bonus falls once holdTime expires, in bonus per second. 0.5 drains a " +
             "full 0.4 bonus in 0.8s, so peak to nothing is about 1.8s.")]
    public float decayPerSec = 0.5f;

    PlayerMotor motor;
    PassiveLoadout passives;
    float held;        // current bonus above 1 (0..maxBonus)
    float holdLeft;

    // Returns 1.0 when the passive is not equipped, so callers never branch on it.
    public float Scale => Equipped ? 1f + held : 1f;

    bool Equipped => passives != null && passives.Has(PassiveType.Momentum);

    // Instant, speed-only bonus. `held` snaps up to this and lags behind it on the way down.
    float SpeedBonus
    {
        get
        {
            float span = Mathf.Max(0.01f, fullBonusSpeed - rampStartSpeed);
            float t = Mathf.Clamp01((motor.Speed - rampStartSpeed) / span);
            return maxBonus * t;
        }
    }

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        passives = GetComponent<PassiveLoadout>();
    }

    // FixedUpdate, not Update: PlayerMotor writes velocity on the fixed tick, so sampling here
    // catches every speed value instead of missing brief peaks between render frames.
    void FixedUpdate()
    {
        if (!Equipped) return;

        float target = SpeedBonus;
        if (target >= held)
        {
            held = target;            // gaining speed pays out immediately
            holdLeft = holdTime;
        }
        else
        {
            holdLeft -= Time.fixedDeltaTime;
            if (holdLeft <= 0f)
                held = Mathf.MoveTowards(held, target, decayPerSec * Time.fixedDeltaTime);
        }
    }
}
