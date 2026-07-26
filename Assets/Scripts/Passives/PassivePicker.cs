using UnityEngine;
using UnityEngine.InputSystem;

// TESTING TOOL — swap the equipped passive live with F1..F6, so feel can be A/B'd without
// leaving play mode, editing the Inspector, and re-entering. Draws a small legend top-right
// with the active passive highlighted. Delete this component (and its script) before ship.
//
// Works for every passive including Featherweight and Dash: Equip() fires PassiveLoadout
// .Changed, which makes PlayerMotor re-resolve the radius and dash flag it caches. Vitality
// only raises the HP ceiling — current HP tops up on the next respawn, so fall in the pit
// to see the full 190.
public class PassivePicker : MonoBehaviour
{
    public PassiveLoadout loadout;

    // Index = function key. F1 = None, then one per listed passive.
    // Dash shelved alongside its connect-screen entry — see PassiveChoice.Options.
    // Momentum is not here either: it is baseline, so there is nothing to toggle. To A/B its
    // feel, change maxBonus on the player's MomentumDamage instead.
    static readonly PassiveType[] Order =
    {
        PassiveType.None,
        PassiveType.Vitality,
        PassiveType.Featherweight,
        PassiveType.DoubleJump,
        PassiveType.Highground,
        PassiveType.Camper,
    };

    GUIStyle style, active;

    void Awake()
    {
        if (loadout == null) loadout = GetComponent<PassiveLoadout>();
        if (loadout == null) loadout = FindAnyObjectByType<PassiveLoadout>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || loadout == null) return;
        if (KeybindsUI.Open) return; // F1-F7 belong to the rebinder while it is capturing

        // Key.F1..F12 are sequential in the enum, so F1 + i indexes each row's key.
        for (int i = 0; i < Order.Length; i++)
            if (kb[(Key)((int)Key.F1 + i)].wasPressedThisFrame)
                loadout.Equip(Order[i]);
    }

    void OnGUI()
    {
        if (loadout == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
            active = new GUIStyle(style) { fontStyle = FontStyle.Bold };
            active.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }

        float x = Screen.width - 230f, y = 12f;
        GUI.Label(new Rect(x, y, 230f, 22f), $"PASSIVE  (F1-F{Order.Length})", style);
        y += 24f;
        for (int i = 0; i < Order.Length; i++)
        {
            bool on = loadout.passive == Order[i];
            GUI.Label(new Rect(x, y, 230f, 20f),
                $"F{i + 1}  {(on ? "▶ " : "")}{Order[i]}", on ? active : style);
            y += 20f;
        }
    }
}
