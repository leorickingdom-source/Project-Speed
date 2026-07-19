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
        "1-7  —  Pistol Rifle Sniper SMG Shotgun Bow Knives",
        "Bow / Knives  —  travel time, lead your target",
        "RMB  —  grapple (reels you in)",
        "Shift  —  dash (Dash passive only)",
        "Aim for the head  —  2x damage",
    };

    bool showControls = true; // visible on launch
    bool paused;
    GUIStyle panel, header, row, hint;

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
        paused = p;
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
        EnsureStyles();

        if (showControls && !paused)
            ControlsCard(new Rect(Screen.width - 344f, 12f, 332f, 30f + Controls.Length * 24f), "CONTROLS   (Tab)");

        GUI.Label(new Rect(12f, Screen.height - 26f, 800f, 22f), "[Tab] controls     [Esc] pause / quit", hint);

        if (paused)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = 372f, h = 320f, x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
            GUI.Box(new Rect(x, y, w, h), "PAUSED", panel);
            ControlsCard(new Rect(x + 16f, y + 46f, w - 32f, Controls.Length * 24f + 6f), null);
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
