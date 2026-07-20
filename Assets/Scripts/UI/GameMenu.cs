using UnityEngine;
using UnityEngine.InputSystem;

// Playtest overlay: a controls card (shown at launch, toggle with Tab) and an Esc pause
// menu with a Quit button. Pausing freezes the sim (timeScale 0) and frees the cursor.
// Self-contained — delete this component for a release build.
public class GameMenu : MonoBehaviour
{
    static readonly string[] Controls =
    {
        "WASD  —  move",
        "Space  —  jump (hold to bunny-hop)",
        "Ctrl / C  —  crouch · slide · crouch-jump",
        "Mouse  —  look     LMB  —  fire     R  —  reload",
        "Deathmatch  —  one weapon, most kills wins",
        "RMB  —  grapple (reels you in)",
        "Shift, or Space in mid-air  —  dash (Dash passive)",
        "Aim for the head  —  2x damage",
    };

    bool showControls = true; // visible on launch
    bool paused;

    // Read by the HUD components so they can stand down while the menu is up. Unity draws
    // OnGUI in script execution order, which is not guaranteed stable frame to frame, so a
    // fullscreen pause overlay competing with six other OnGUI drawers produces intermittent
    // flicker. Suppressing the HUD removes the contest rather than trying to order it.
    public static bool IsPaused { get; private set; }
    GUIStyle panel, header, row, hint;

    void Awake() => GameSettings.Load();

    // Slider + live value. Applies on every change so the effect is felt while dragging —
    // sensitivity in particular is impossible to judge without moving the mouse as you set it.
    float SettingRow(float x, float y, float w, string name, float value, float min, float max,
        string fmt)
    {
        GUI.Label(new Rect(x, y, w * 0.55f, 20f), name, row);
        GUI.Label(new Rect(x + w * 0.55f, y, w * 0.45f, 20f), value.ToString(fmt), row);
        return GUI.HorizontalSlider(new Rect(x, y + 20f, w, 16f), value, min, max);
    }

    void DrawSettings(float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 22f), "SETTINGS", header);
        y += 24f;

        float s = SettingRow(x, y, w, "Mouse sensitivity", GameSettings.Sensitivity,
            GameSettings.SensRange.x, GameSettings.SensRange.y, "0.000");
        y += 42f;
        float v = SettingRow(x, y, w, "Volume", GameSettings.Volume, 0f, 1f, "0.00");
        y += 42f;
        float f = SettingRow(x, y, w, "Field of view", GameSettings.Fov,
            GameSettings.FovRange.x, GameSettings.FovRange.y, "0");

        if (!Mathf.Approximately(s, GameSettings.Sensitivity)
            || !Mathf.Approximately(v, GameSettings.Volume)
            || !Mathf.Approximately(f, GameSettings.Fov))
        {
            GameSettings.Sensitivity = s;
            GameSettings.Volume = v;
            GameSettings.Fov = f;
            GameSettings.Apply();
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.tabKey.wasPressedThisFrame) showControls = !showControls;
        if (kb.escapeKey.wasPressedThisFrame) SetPaused(!paused);
        if (paused && kb.qKey.wasPressedThisFrame) Quit();
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

        if (showControls && !paused)
            ControlsCard(new Rect(Screen.width - 344f, 12f, 332f, 30f + Controls.Length * 24f), "CONTROLS   (Tab)");

        GUI.Label(new Rect(12f, Screen.height - 26f, 800f, 22f), "[Tab] controls     [Esc] pause / quit", hint);

        if (paused)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = 400f, h = 470f, x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
            GUI.Box(new Rect(x, y, w, h), "PAUSED", panel);
            ControlsCard(new Rect(x + 16f, y + 46f, w - 32f, Controls.Length * 24f + 6f), null);
            DrawSettings(x + 16f, y + 56f + Controls.Length * 24f, w - 32f);
            if (GUI.Button(new Rect(x + 16f, y + h - 84f, w - 32f, 30f), "Leave match  —  back to menu"))
            {
                GameSettings.Save();
                paused = false;
                ConnectUI.LeaveMatch();
                return;
            }
            if (GUI.Button(new Rect(x + 16f, y + h - 48f, (w - 40f) * 0.5f, 34f), "Resume  (Esc)")) SetPaused(false);
            if (GUI.Button(new Rect(x + w * 0.5f + 4f, y + h - 48f, (w - 40f) * 0.5f, 34f), "Quit  (Q)")) Quit();
        }
    }

    void ControlsCard(Rect r, string heading)
    {
        GUI.Box(r, GUIContent.none, panel);
        float yy = r.y + 6f;
        if (heading != null) { GUI.Label(new Rect(r.x + 10f, yy, r.width - 20f, 22f), heading, header); yy += 26f; }
        foreach (var c in Controls) { GUI.Label(new Rect(r.x + 12f, yy, r.width - 24f, 22f), c, row); yy += 24f; }
    }

    void EnsureStyles()
    {
        if (panel != null) return;
        panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
        header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
        header.normal.textColor = new Color(0.55f, 0.9f, 1f);
        row = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        row.normal.textColor = Color.white;
        hint = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
    }
}
