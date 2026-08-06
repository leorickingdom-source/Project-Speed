using UnityEngine;

// Momentum: your damage scales with how fast you are moving — and, crucially, keeps scaling
// for a moment after you stop.
//
// BASELINE for every player, like the grapple and the wall jump. It was 1 of 6 passive picks,
// and that made the game's central promise — "more movement -> more speed" -> more power — an
// opt-in most players declined, while scope, headshots, Flashpoint and Oddball all paid for
// standing still. A pillar cannot sit in a slot that competes with +40 HP. Momentum is now
// what speed IS FOR; the passives choose how you fight on top of it.
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
    [Tooltip("Damage bonus at fullBonusSpeed. 0.25 = +25%. It was 0.4 while this was " +
             "a PICK, where the bonus was paid for by giving up a slot; baseline removes that " +
             "price, so the number came down with it — otherwise every gun in the game quietly " +
             "gets a shorter TTK. Breakpoints at 0.25 against 150 HP: revolver 65 -> 81, a " +
             "2-shot body kill inside its 20m full-damage band instead of 3 (Vitality's 190 " +
             "puts it back to 3, which is now what Vitality answers); sniper 100 -> 125, still " +
             "a 2-shot; shotgun 8x13 = 104 -> 130, still a 2-shot with 20 to spare.")]
    public float maxBonus = 0.25f;
    [Tooltip("Speed the bonus starts ramping from. Matches PlayerMotor.groundSpeed by default, " +
             "so plain running earns nothing at all.")]
    public float rampStartSpeed = 9f;
    [Tooltip("Speed at which TIER ONE is fully earned. 16 matches the slide ceiling and " +
             "roughly the air ceiling (groundSpeed * flowMax).")]
    public float fullBonusSpeed = 16f;

    [Header("Tier two — the speeds only the verbs reach")]
    [Tooltip("Bonus at tier2FullSpeed and above, TOTAL (not added to maxBonus). The second " +
             "segment exists because the curve used to stop at 16, which every slide and bhop " +
             "already reaches — so the grapple, the rocket jump and the slingshot release were " +
             "all damage-neutral, and the connect screen's 'speed is damage' was true only up " +
             "to the speed you get for tapping crouch (BACKLOG 4b). 16->28 at +25->+40% makes " +
             "the verbs worth damage without touching anything below 16: nothing existing is " +
             "nerfed, tier one breakpoints hold exactly. CEILING CHECK, against 150 HP: 0.40 " +
             "keeps point-blank shotgun at 104*1.40 = 145.6 and a sniper body at 140 — both " +
             "still 2-shots. 0.44 is where the shotgun one-shots; do not raise past it.")]
    public float tier2MaxBonus = 0.40f;
    [Tooltip("Speed at which tier two is fully earned. 28: the fast reel sustains ~24, a " +
             "rocket launch ~27, a slung release arc low 30s — so the top of the curve is " +
             "reachable ONLY by the movement verbs, briefly, which is the whole point.")]
    public float tier2FullSpeed = 28f;

    [Header("Carry into combat")]
    [Tooltip("Seconds the bonus holds at its peak after you slow down. This is what lets the " +
             "passive touch COMBAT at all — without it the bonus is 1.00x exactly when you are " +
             "standing still shooting, which is most of a fight.")]
    public float holdTime = 1f;
    [Tooltip("How fast the bonus falls once holdTime expires, in bonus per second. 0.5 drains a " +
             "full 0.4 bonus in 0.8s, so peak to nothing is about 1.8s.")]
    public float decayPerSec = 0.5f;

    PlayerMotor motor;
    float held;        // current bonus above 1 (0..maxBonus)
    float holdLeft;

    // 1.0 while you are at or below the running cap, so callers never branch on it. Also the
    // reason WeaponController can treat a MISSING component as 1x: bots and dummies simply
    // do not carry this, and nothing else changes.
    public float Scale => 1f + held;

    // Instant, speed-only bonus. `held` snaps up to this and lags behind it on the way down.
    // Two segments: 9->16 earns the first 25% (slide/bhop territory, unchanged since it was
    // tuned), 16->28 climbs to 40% (grapple/rocket territory — see tier2MaxBonus for why).
    float SpeedBonus
    {
        get
        {
            float speed = motor.Speed;
            float span1 = Mathf.Max(0.01f, fullBonusSpeed - rampStartSpeed);
            float bonus = maxBonus * Mathf.Clamp01((speed - rampStartSpeed) / span1);
            if (speed > fullBonusSpeed && tier2MaxBonus > maxBonus)
            {
                float span2 = Mathf.Max(0.01f, tier2FullSpeed - fullBonusSpeed);
                bonus += (tier2MaxBonus - maxBonus)
                         * Mathf.Clamp01((speed - fullBonusSpeed) / span2);
            }
            return bonus;
        }
    }

    void Awake() => motor = GetComponent<PlayerMotor>();

    // FixedUpdate, not Update: PlayerMotor writes velocity on the fixed tick, so sampling here
    // catches every speed value instead of missing brief peaks between render frames.
    void FixedUpdate()
    {
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
