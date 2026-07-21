using UnityEngine;
using UnityEngine.InputSystem;

// Playtest overlay: a controls card (shown at launch, toggle with Tab) and an Esc pause
// menu with a Quit button. Pausing freezes the sim (timeScale 0) and frees the cursor.
// Self-contained — delete this component for a release build.
public class GameMenu : MonoBehaviour
{
    // Built from the live bindings, not written out by hand. The old hardcoded list was a
    // second source of truth for the controls and could only ever be right for a player who
    // had not changed anything — which, now that changing them is possible, is the wrong
    // default assumption.
    static string[] controls;
    static int controlsVersion = -1;

    static string[] Controls()
    {
        if (controls != null && controlsVersion == Keybinds.Version) return controls;
        controlsVersion = Keybinds.Version;

        // Primary slot only for the composite lines — spelling out both slots turns "WASD"
        // into "W / Up A / Left S / Down D / Right", which nobody reads.
        string P(GameAction a) => Keybinds.Label(Keybinds.Get(a, 0));
        string B(GameAction a) => Keybinds.Label(a);

        controls = new[]
        {
            $"{P(GameAction.MoveForward)}{P(GameAction.MoveLeft)}{P(GameAction.MoveBack)}{P(GameAction.MoveRight)}  —  move",
            $"{B(GameAction.Jump)}  —  jump (hold to bunny-hop)",
            $"{B(GameAction.Crouch)}  —  crouch · slide · crouch-jump",
            $"Mouse  —  look     {B(GameAction.Fire)}  —  fire     {B(GameAction.Reload)}  —  reload",
            $"{B(GameAction.Grapple)}  —  grapple (reels you in)",
            $"{B(GameAction.Dash)}, or {P(GameAction.Jump)} in mid-air  —  dash (Dash passive)",
            "Deathmatch  —  one weapon, most kills wins",
            "Aim for the head  —  2x damage",
            $"{B(GameAction.ToggleControls)}  —  this card     {B(GameAction.Pause)}  —  pause",
        };
        return controls;
    }

    bool showControls = true; // visible on launch
    bool paused;

    // Read by the HUD components so they can stand down while the menu is up. Unity draws
    // OnGUI in script execution order, which is not guaranteed stable frame to frame, so a
    // fullscreen pause overlay competing with six other OnGUI drawers produces intermittent
    // flicker. Suppressing the HUD removes the contest rather than trying to order it.
    public static bool IsPaused { get; private set; }
    GUIStyle panel, header, row, hint;

    void Awake()
    {
        GameSettings.Load();
        IsPaused = false; // static, so a stale true would survive a scene load
    }

    // Clears pause from outside — used when leaving a match, since the static would otherwise
    // persist and keep the menu drawn over the connect screen.
    public static void ForceUnpause()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        foreach (var gm in FindObjectsByType<GameMenu>(FindObjectsSortMode.None))
            if (gm != null) gm.paused = false;
    }

    void Update()
    {
        KeybindsUI.Tick();
        // The rebinder owns the keyboard while it is up: every press there is either a new
        // binding or a way out of the panel, and none of them should also toggle the menu.
        if (KeybindsUI.Open || KeybindsUI.ConsumedInput) return;

        if (Keybinds.Pressed(GameAction.ToggleControls)) showControls = !showControls;
        if (Keybinds.Pressed(GameAction.Pause)) SetPaused(!paused);

        var kb = Keyboard.current;
        if (paused && kb != null && kb.qKey.wasPressedThisFrame) Quit();
    }

    void SetPaused(bool p)
    {
        // Write on close rather than on every slider tick — dragging a slider would otherwise
        // hit the disk dozens of times a second.
        if (paused && !p) GameSettings.Save();
        paused = p;
        IsPaused = p;
        Time.timeScale = p ? 0f : 1f;
        Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = p;
    }

    void Quit()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnGUI()
    {
        // Negative depth draws last, i.e. on top. Belt and braces alongside the HUD standing
        // down: whatever else is drawing, the menu is above it.
        GUI.depth = -100;
        EnsureStyles();

        // Nothing until you are actually in a match. The connect screen owns the display
        // before that, and this HUD used to draw straight through it — the hint line sits at
        // Screen.height - 26, which lands on top of the loadout descriptions in a short window.
        if (!FishNet.InstanceFinder.IsClientStarted && !FishNet.InstanceFinder.IsServerStarted)
            return;

        // Modal. IMGUI will happily route a click to the Quit button sitting underneath the
        // rebinder, so nothing else may draw while it is up.
        if (KeybindsUI.Open) { KeybindsUI.Draw(); return; }

        var lines = Controls();

        if (showControls && !paused)
            ControlsCard(new Rect(Screen.width - 468f, 12f, 456f, 34f + lines.Length * RowH),
                $"CONTROLS   ({Keybinds.Label(Keybinds.Get(GameAction.ToggleControls, 0))})");

        GUI.Label(new Rect(12f, Screen.height - 30f, 900f, 26f),
            $"[{Keybinds.Label(Keybinds.Get(GameAction.ToggleControls, 0))}] controls     " +
            $"[{Keybinds.Label(Keybinds.Get(GameAction.Pause, 0))}] pause / quit", hint);

        if (paused)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Height is derived, not a magic number: the controls card grows with the bindings
            // list and the settings block grew a button row, and the old fixed 470 quietly put
            // the Resume button underneath the sliders.
            float cardH = lines.Length * RowH + 8f;
            const float pad = 16f, gap = 10f, leaveH = 34f, footH = 38f;
            float w = 520f;
            float h = 52f + cardH + gap + SettingsUI.Height + gap + leaveH + 6f + footH + pad;
            float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x, y, w, h), "PAUSED", panel);

            float cy = y + 52f;
            ControlsCard(new Rect(x + pad, cy, w - pad * 2f, cardH), null);
            cy += cardH + gap;

            SettingsUI.Draw(x + pad, cy, w - pad * 2f);
            cy += SettingsUI.Height + gap;

            if (GUI.Button(new Rect(x + pad, cy, w - pad * 2f, leaveH), "Leave match  —  back to menu"))
            {
                GameSettings.Save();
                paused = false;
                ConnectUI.LeaveMatch();
                return;
            }
            cy += leaveH + 6f;

            float halfW = (w - pad * 2f - 8f) * 0.5f;
            if (GUI.Button(new Rect(x + pad, cy, halfW, footH),
                    $"Resume  ({Keybinds.Label(Keybinds.Get(GameAction.Pause, 0))})")) SetPaused(false);
            if (GUI.Button(new Rect(x + pad + halfW + 8f, cy, halfW, footH), "Quit  (Q)")) Quit();
        }
    }

    // Row pitch, shared by the card and by the pause panel that sizes itself from it. A single
    // constant because the two used to be separate numbers and drifted apart the moment the
    // font changed, leaving the panel too short for its own contents.
    const float RowH = 28f;

    void ControlsCard(Rect r, string heading)
    {
        GUI.Box(r, GUIContent.none, panel);
        float yy = r.y + 6f;
        if (heading != null) { GUI.Label(new Rect(r.x + 10f, yy, r.width - 20f, 26f), heading, header); yy += 30f; }
        foreach (var c in Controls()) { GUI.Label(new Rect(r.x + 12f, yy, r.width - 24f, 26f), c, row); yy += RowH; }
    }

    void EnsureStyles()
    {
        if (panel != null) return;
        panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = 19 };
        header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 17 };
        header.normal.textColor = new Color(0.55f, 0.9f, 1f);
        row = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        row.normal.textColor = Color.white;
        hint = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
    }
}
