using UnityEngine;

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
    public PlayerArmour armour;
    public WeaponController weapon;

    [Header("Nameplates")]
    [Tooltip("Show the names of players you can actually see.")]
    public bool showNameplates = true;
    [Tooltip("Half-angle from the crosshair a player must be within to be named. Deliberately " +
             "NOT 180: naming every visible player turns the HUD into a tracker that follows " +
             "people through your peripheral vision. This answers 'who am I looking at', which " +
             "is what names are for. Raise it if you want them always on.")]
    [Range(5f, 180f)] public float nameplateAngle = 25f;
    [Tooltip("Past this distance a name is not drawn at all.")]
    public float nameplateRange = 70f;
    [Tooltip("Geometry that blocks a nameplate. Without a line-of-sight test, names through " +
             "walls are a wallhack you shipped on purpose.")]
    public LayerMask nameplateBlockMask = ~0;

    [Header("Ping")]
    [Tooltip("Show round-trip time to the server. Worth having permanently visible while the " +
             "group plays through a relay, because the tunnel's cost is the main thing you " +
             "cannot judge from inside the game otherwise.")]
    public bool showPing = true;
    [Tooltip("Green below this. Under a tick and a half at 100Hz still feels direct.")]
    public float pingGood = 60f;
    [Tooltip("Amber below this, red above. Past here you are visibly shooting at where someone " +
             "WAS, which in a game this fast is the point it starts costing you fights.")]
    public float pingBad = 120f;

    [Header("Respawn")]
    [Tooltip("Seconds of countdown below which it switches to one decimal, so the last second " +
             "reads as a countdown rather than a static '1'.")]
    public float countdownPreciseUnder = 3f;

    [Header("Speed gauge")]
    [Tooltip("Speed the bar reads as full. 20 sits just above the dash (18) and slide ceiling (16).")]
    public float maxDisplaySpeed = 20f;
    public float barWidth = 260f;
    public float barHeight = 10f;
    [Tooltip("Bottom margin in pixels.")]
    public float bottomMargin = 28f;

    [Header("Debug")]
    [Tooltip("The Debug readout bind (F3 by default) toggles the raw engine readouts. " +
             "Off for players.")]
    public bool showDebug = false;

    GUIStyle big, small, dim;
    Texture2D pixel;

    // One nameplate, resolved in Update and drawn in OnGUI. Separated because OnGUI runs
    // several times a frame and raycasting once per pass would triple the cost for no gain.
    struct Plate
    {
        public Vector2 pos;   // already in GUI space (y down)
        public string text;
        public Color tint;
        public float alpha;
    }

    readonly System.Collections.Generic.List<Plate> plates = new System.Collections.Generic.List<Plate>();
    PlayerIdentity[] others = System.Array.Empty<PlayerIdentity>();
    float nextOtherScan;
    Camera view;
    GUIStyle plateStyle;

    // Smoothed, because raw RTT jitters by tens of milliseconds between samples and a number
    // that flickers is one nobody reads. -1 means "no sample yet".
    float shownPing = -1f;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (motor == null) motor = FindAnyObjectByType<PlayerMotor>();
        if (health == null && motor != null) health = motor.GetComponent<PlayerHealth>();
        if (armour == null && motor != null) armour = motor.GetComponent<PlayerArmour>();
        if (weapon == null && motor != null) weapon = motor.GetComponent<WeaponController>();

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    void Update()
    {
        if (KeybindsUI.Open) return; // a key pressed while rebinding is a binding, not a command
        if (Keybinds.Pressed(GameAction.ToggleDebug)) showDebug = !showDebug;
        RefreshNameplates();
        RefreshPing();
    }

    void RefreshPing()
    {
        if (!showPing || !FishNet.InstanceFinder.IsClientStarted)
        {
            shownPing = -1f;
            return;
        }

        var tm = FishNet.InstanceFinder.TimeManager;
        if (tm == null) { shownPing = -1f; return; }

        float rtt = tm.RoundTripTime;
        // Snap on the first sample so it does not visibly climb from zero on connect.
        shownPing = shownPing < 0f ? rtt : Mathf.Lerp(shownPing, rtt, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
    }

    void DrawPing()
    {
        if (shownPing < 0f) return;

        Color c = shownPing <= pingGood ? new Color(0.45f, 0.95f, 0.5f)
                : shownPing <= pingBad ? new Color(1f, 0.82f, 0.35f)
                : new Color(1f, 0.45f, 0.4f);

        var style = new GUIStyle(small) { fontStyle = FontStyle.Bold };
        style.normal.textColor = c;
        // Under the Leave match button, which owns the very top-left corner.
        GUI.Label(new Rect(14f, 48f, 200f, 24f), $"{shownPing:0} ms", style);
    }

    void RefreshNameplates()
    {
        plates.Clear();
        if (!showNameplates) return;

        // The death camera reparents the camera away from the body, so re-resolve rather than
        // caching once — a stale reference would leave nameplates projected from nowhere.
        if (view == null || !view.isActiveAndEnabled)
        {
            view = motor != null ? motor.GetComponentInChildren<Camera>() : null;
            if (view == null) view = Camera.main;
        }
        if (view == null) return;

        // Players do not join every frame. Rescanning at 2Hz keeps the allocation off the
        // frame budget while still picking up someone who connected a moment ago.
        if (Time.time >= nextOtherScan)
        {
            others = FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None);
            nextOtherScan = Time.time + 0.5f;
        }

        Vector3 eye = view.transform.position;
        Vector3 fwd = view.transform.forward;
        float rangeSqr = nameplateRange * nameplateRange;

        foreach (var id in others)
        {
            if (id == null || id.IsOwner) continue;

            // Dead players do not get a name — their body is about to teleport anyway.
            var hp = id.GetComponent<PlayerHealth>();
            if (hp != null && !hp.Alive) continue;

            Vector3 head = id.transform.position + Vector3.up * 1.25f;
            Vector3 to = head - eye;
            float distSqr = to.sqrMagnitude;
            if (distSqr > rangeSqr || distSqr < 0.01f) continue;

            float dist = Mathf.Sqrt(distSqr);
            Vector3 dir = to / dist;

            float angle = Vector3.Angle(fwd, dir);
            if (angle > nameplateAngle) continue;

            // Line of sight. Anything solid in the way and the name is not drawn — otherwise
            // this is a wallhack with a nice font.
            if (Physics.Raycast(eye, dir, out RaycastHit hit, dist - 0.35f, nameplateBlockMask,
                    QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerIdentity>() != id)
                continue;

            Vector3 sp = view.WorldToScreenPoint(head);
            if (sp.z <= 0f) continue;

            // Fade out toward the edge of the cone and with distance, so a name entering view
            // arrives rather than pops.
            float edge = 1f - Mathf.Clamp01(angle / Mathf.Max(1f, nameplateAngle));
            float far = 1f - Mathf.Clamp01(dist / Mathf.Max(1f, nameplateRange));
            float a = Mathf.Clamp01(Mathf.Min(edge * 3f, 1f) * Mathf.Min(far * 3f, 1f));
            if (a <= 0.02f) continue;

            plates.Add(new Plate
            {
                pos = new Vector2(sp.x, Screen.height - sp.y), // WorldToScreenPoint is y-up
                text = id.Name,
                tint = id.Tint,
                alpha = a,
            });
        }
    }

    void DrawNameplates()
    {
        if (plates.Count == 0) return;

        if (plateStyle == null)
            plateStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        foreach (var p in plates)
        {
            var c = p.tint;
            c.a = p.alpha;
            plateStyle.normal.textColor = c;
            GUI.Label(new Rect(p.pos.x - 130f, p.pos.y - 34f, 260f, 24f), p.text, plateStyle);
        }
    }

    void Box(float x, float y, float w, float h, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        if (motor == null || GameMenu.IsPaused || KeybindsUI.Open) return;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            small.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            dim = new GUIStyle(GUI.skin.label) { fontSize = 15 };
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
            GUI.Label(new Rect(28f, sh - bottomMargin - 42f, 220f, 42f), hp, big);
            if (health.Alive && health.Invulnerable)
                GUI.Label(new Rect(28f, sh - bottomMargin - 64f, 220f, 22f), "invulnerable", dim);
        }

        // Armour sits beside health rather than replacing it, and only appears when you have
        // some. A permanent "0" would take up the same space to say nothing, and the point of
        // armour is that HAVING it is the state worth noticing.
        if (armour != null && armour.HasArmour)
        {
            var armourStyle = new GUIStyle(big) { fontSize = 25 };
            armourStyle.normal.textColor = ArmourTint;
            GUI.Label(new Rect(118f, sh - bottomMargin - 36f, 130f, 30f), $"{armour.Points:0}", armourStyle);

            // Small bar underneath so the level is readable without reading the number.
            float armourT = Mathf.Clamp01(armour.Points / Mathf.Max(1f, armour.MaxArmour));
            Box(118f, sh - bottomMargin - 7f, 80f, 5f, new Color(0f, 0f, 0f, 0.45f));
            Box(118f, sh - bottomMargin - 7f, 80f * armourT, 5f, ArmourTint);
        }

        if (weapon != null)
        {
            string ammo = weapon.Reloading ? "reloading" : $"{weapon.CurrentAmmo} / {weapon.CurrentMag}";
            var r = new Rect(sw - 268f, sh - bottomMargin - 42f, 240f, 42f);
            var right = new GUIStyle(big) { alignment = TextAnchor.MiddleRight };
            GUI.Label(r, ammo, right);
            var rightSmall = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(sw - 268f, sh - bottomMargin - 66f, 240f, 22f), weapon.CurrentName, rightSmall);
        }

        DrawMobilityPerk(barX, barY);
        DrawNameplates();
        DrawPing();

        // Crosshair, but not while dead: the death camera is third person, so a centre dot
        // would sit in mid-air pointing at nothing and imply you can still shoot.
        bool dead = health != null && !health.Alive;
        if (!dead)
            GUI.DrawTexture(new Rect(sw * 0.5f - 2f, sh * 0.5f - 2f, 4f, 4f), Texture2D.whiteTexture);
        else
            DrawRespawnCountdown(sw, sh);

        if (showDebug) DrawDebug();
    }

    static readonly Color ArmourTint = new Color(0.45f, 0.72f, 1f);

    // The one thing a dead player wants to know. Without it the wait has no shape — you cannot
    // tell a respawn delay from a hang, and you cannot get ready for the moment you are back.
    void DrawRespawnCountdown(float sw, float sh)
    {
        float left = health.RespawnCountdown;

        var title = new GUIStyle(big) { fontSize = 25, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        var number = new GUIStyle(big) { fontSize = 66, alignment = TextAnchor.MiddleCenter };
        number.normal.textColor = new Color(1f, 0.85f, 0.4f);

        float cy = sh * 0.34f;

        // Who did it. The death camera already SWINGS to face them, but a coloured capsule at
        // 30m is not an identity — without the name the one thing you most want to know at the
        // moment of dying is the one thing the screen does not say. Absent for a pit fall or
        // out-of-bounds, where there genuinely is no killer to name.
        string killer = health.LastAttackerName;
        if (!string.IsNullOrEmpty(killer))
        {
            var by = new GUIStyle(big) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            by.normal.textColor = new Color(1f, 0.5f, 0.45f);
            GUI.Label(new Rect(0f, cy - 42f, sw, 34f), $"killed by {killer}", by);
        }

        GUI.Label(new Rect(0f, cy, sw, 32f), "RESPAWNING IN", title);
        // One decimal at the end so the last stretch visibly counts rather than sitting on "1".
        string text = left <= countdownPreciseUnder ? $"{left:0.0}" : $"{Mathf.CeilToInt(left)}";
        GUI.Label(new Rect(0f, cy + 32f, sw, 74f), text, number);
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
            // Read from the live bindings — a remapped dash printed as "Shift" is worse than
            // no prompt at all, because the player trusts it and concludes dash is broken.
            text = ready
                ? $"DASH  [{Keybinds.Label(GameAction.Dash)} / {Keybinds.Label(GameAction.Jump)} in air]"
                : $"DASH  {motor.DashCooldownLeft:0.0}s";
        }
        else
        {
            ready = motor.AirJumpsLeft > 0;
            text = ready ? $"DOUBLE JUMP  [{Keybinds.Label(GameAction.Jump)}]" : "DOUBLE JUMP  spent";
        }

        const float w = 250f, h = 30f;
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
        GUI.Label(new Rect(14, 50, 700, 20),
            $"{Keybinds.Label(GameAction.ToggleDebug)} hides this", dim);
    }
}
