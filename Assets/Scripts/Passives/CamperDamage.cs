using UnityEngine;

// Camper passive. A JOKE, and the exact inverse of Momentum: enormous damage while you
// are barely moving, and it falls off a cliff the moment you do anything the rest of the
// game is about.
//
// It is self-limiting rather than balanced by numbers. Cashing it in means standing still
// in an open arena shooter, which makes you the easiest target on the map — and the moment
// you dodge, strafe or jump, it is gone. The tension IS the joke.
//
// Note the falloff is a 1 m/s band (4 -> 5) rather than a literal cliff at exactly 4.0.
// A hard threshold would flicker between full buff and nothing every tick while hovering
// at the boundary. It still reads as drastic.
[RequireComponent(typeof(PlayerMotor))]
public class CamperDamage : MonoBehaviour
{
    [Tooltip("Damage bonus while at or below fullBonusSpeed. 1.0 = +100%. WARNING: at 2.0x a " +
             "sniper body shot is 200 against 150 HP, so a stationary sniper ONE-SHOTS. That is " +
             "the joke, but drop this to 0.45 if it wrecks playtests — 145 keeps it a 2-shot.")]
    public float maxBonus = 1f;
    [Tooltip("At or below this speed the bonus is full. 4 m/s matches PlayerMotor's " +
             "flowMoveThreshold, the speed the game already treats as barely moving.")]
    public float fullBonusSpeed = 4f;
    [Tooltip("At or above this speed the bonus is entirely gone. The narrow band between this " +
             "and fullBonusSpeed is what stops per-tick flicker at the boundary.")]
    public float cutoffSpeed = 5f;

    PlayerMotor motor;
    PassiveLoadout passives;

    // Returns 1.0 when the passive is not equipped, so callers never branch on it.
    public float Scale
    {
        get
        {
            if (passives == null || !passives.Has(PassiveType.Camper)) return 1f;
            float span = Mathf.Max(0.01f, cutoffSpeed - fullBonusSpeed);
            float t = Mathf.Clamp01((motor.Speed - fullBonusSpeed) / span); // 0 = still, 1 = moving
            return 1f + maxBonus * (1f - t);
        }
    }

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        passives = GetComponent<PassiveLoadout>();
    }
}
