using UnityEngine;
using UnityEngine.InputSystem;

// Player-facing HUD. Speed is a GAUGE rather than a number: in a momentum game what matters
// is whether you're building or bleeding speed, which a filling bar shows at a glance while
// a decimal readout forces you to read and compare.
//
// The gauge marks groundSpeed, because that's the meaningful line — PlayerMotor hard-caps
// running there, so everything past the mark is speed you earned by bhopping, sliding or
// grappling. Momentum's damage bonus ramps over the same range, so the bar doubles as that
// passive's readout without a separate number.
//
// Engine internals (raw velocity, grounded, flow multiplier, stance) are dev data and are
// hidden behind F3 rather than shown to players.
public class SpeedHud : MonoBehaviour
{
    public PlayerMotor motor;
    public PlayerHealth health;
    public WeaponController weapon;

    [Header("Speed gauge")]
    [Tooltip("Speed the bar reads as full. 20 sits just above the dash (18) and slide ceiling (16).")]
    public float maxDisplaySpeed = 20f;
    public float barWidth = 260f;
    public float barHeight = 10f;
    [Tooltip("Bottom margin in pixels.")]
    public float bottomMargin = 28f;

    [Header("Debug")]
    [Tooltip("F3 toggles the raw engine readouts. Off for players.")]
    public bool showDebug = false;

    GUIStyle big, small, dim;
    Texture2D pixel;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (motor == null) motor = FindAnyObjectByType<PlayerMotor>();
        if (health == null && motor != null) health = motor.GetComponent<PlayerHealth>();
        if (weapon == null && motor != null) weapon = motor.GetComponent<WeaponController>();

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f3Key.wasPressedThisFrame) showDebug = !showDebug;
    }

    void Box(float x, float y, float w, float h, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        if (motor == null || GameMenu.IsPaused) return;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            small.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            dim = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            dim.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        }

        float sw = Screen.width, sh = Screen.height;
        float barX = (sw - barWidth) * 0.5f, barY = sh - bottomMargin - barHeight;

        // --- speed gauge ---
        float t = Mathf.Clamp01(motor.Speed / Mathf.Max(0.01f, maxDisplaySpeed));
        Box(barX - 1f, barY - 1f, barWidth + 2f, barHeight + 2f, new Color(0f, 0f, 0f, 0.45f));

        // Warms as you pass the running cap — the visual cue that you're in earned-speed
        // territory, which is also where Momentum's bonus lives.
        float runT = motor.groundSpeed / Mathf.Max(0.01f, maxDisplaySpeed);
        Color fill = t <= runT
            ? new Color(0.55f, 0.75f, 0.95f, 0.9f)
            : Color.Lerp(new Color(0.95f, 0.75f, 0.3f, 0.95f), new Color(1f, 0.45f, 0.25f, 1f),
                Mathf.InverseLerp(runT, 1f, t));
        Box(barX, barY, barWidth * t, barHeight, fill);

        // Tick at the running cap.
        Box(barX + barWidth * runT, barY - 3f, 1.5f, barHeight + 6f, new Color(1f, 1f, 1f, 0.55f));

        // --- health (left) and ammo (right), the two numbers a player actually needs ---
        if (health != null)
        {
            string hp = health.Alive ? $"{health.Hp:0}" : "DEAD";
            GUI.Label(new Rect(28f, sh - bottomMargin - 34f, 200f, 34f), hp, big);
            if (health.Alive && health.Invulnerable)
                GUI.Label(new Rect(28f, sh - bottomMargin - 52f, 200f, 20f), "invulnerable", dim);
        }

        if (weapon != null)
        {
            string ammo = weapon.Reloading ? "reloading" : $"{weapon.CurrentAmmo} / {weapon.CurrentMag}";
            var r = new Rect(sw - 228f, sh - bottomMargin - 34f, 200f, 34f);
            var right = new GUIStyle(big) { alignment = TextAnchor.MiddleRight };
            GUI.Label(r, ammo, right);
            var rightSmall = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(sw - 228f, sh - bottomMargin - 54f, 200f, 20f), weapon.CurrentName, rightSmall);
        }

        DrawMobilityPerk(barX, barY);

        // Center crosshair dot.
        GUI.DrawTexture(new Rect(sw * 0.5f - 2f, sh * 0.5f - 2f, 4f, 4f), Texture2D.whiteTexture);

        if (showDebug) DrawDebug();
    }

    // Mobility perk state, centred directly above the gauge. Deliberately prominent: it is a
    // charge you spend and wait on, so "can I do it right now" has to be answerable without
    // looking away from the crosshair. Reads as a lit pill when ready, dim when spent.
    void DrawMobilityPerk(float barX, float barY)
    {
        bool hasDash = motor.HasDash;
        bool hasDJ = motor.HasDoubleJump;
        if (!hasDash && !hasDJ) return;

        bool ready;
        string text;
        if (hasDash)
        {
            ready = motor.DashCooldownLeft <= 0f;
            text = ready ? "DASH  [Shift / Space in air]" : $"DASH  {motor.DashCooldownLeft:0.0}s";
        }
        else
        {
            ready = motor.AirJumpsLeft > 0;
            text = ready ? "DOUBLE JUMP  [Space]" : "DOUBLE JUMP  spent";
        }

        const float w = 200f, h = 26f;
        float x = barX + (barWidth - w) * 0.5f, y = barY - h - 10f;

        Color bg = ready ? new Color(1f, 0.78f, 0.28f, 0.22f) : new Color(0f, 0f, 0f, 0.35f);
        Box(x, y, w, h, bg);
        // Bright top edge while charged — readable in peripheral vision without reading text.
        if (ready) Box(x, y, w, 2f, new Color(1f, 0.85f, 0.4f, 0.95f));

        var centred = new GUIStyle(ready ? small : dim) { alignment = TextAnchor.MiddleCenter };
        if (ready) centred.normal.textColor = new Color(1f, 0.9f, 0.55f);
        GUI.Label(new Rect(x, y, w, h), text, centred);
    }

    // Engine internals. Dev-only — this is the data that used to be on screen permanently.
    void DrawDebug()
    {
        string stance = motor.sliding ? "SLIDE" : motor.crouching ? "crouch" : "stand";
        string dmg = weapon != null && weapon.DamageScale > 1.001f
            ? $"   dmg x{weapon.DamageScale:0.00}" : "";
        GUI.Label(new Rect(14, 10, 700, 20), $"speed {motor.Speed:0.0} m/s   grounded {motor.grounded}   [{stance}]{dmg}", dim);
        GUI.Label(new Rect(14, 30, 700, 20),
            $"vel  x{motor.velocity.x:0.0}  y{motor.velocity.y:0.0}  z{motor.velocity.z:0.0}   flow x{motor.flow:0.00}", dim);
        GUI.Label(new Rect(14, 50, 700, 20), "F3 hides this", dim);
    }
}
