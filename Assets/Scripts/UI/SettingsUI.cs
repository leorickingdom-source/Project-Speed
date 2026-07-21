using UnityEngine;

// Shared settings block, drawn by both the connect screen and the pause menu.
//
// Lives in one place because they must stay identical: a player who tunes sensitivity in the
// menu and finds different controls in game — or cannot find the setting at all before their
// first match — is exactly the problem settings were added to solve.
public static class SettingsUI
{
    // Title row + name row + three slider rows + the button row underneath. Kept exact because
    // ConnectUI sizes its backing panel from it, and an underestimate puts the buttons off it.
    public const float Height = 226f;

    static GUIStyle label, value, button, field;

    static void EnsureStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        label.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
        button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        field = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
    }

    static float Row(float x, float y, float w, string name, float v, float min, float max,
        string fmt)
    {
        GUI.Label(new Rect(x, y, w * 0.6f, 20f), name, label);
        GUI.Label(new Rect(x + w * 0.6f, y, w * 0.4f, 20f), v.ToString(fmt), value);
        return GUI.HorizontalSlider(new Rect(x, y + 20f, w, 16f), v, min, max);
    }

    // Returns true if anything changed, so the caller can decide when to persist.
    public static bool Draw(float x, float y, float w, string title = "SETTINGS")
    {
        EnsureStyles();
        if (!string.IsNullOrEmpty(title))
        {
            GUI.Label(new Rect(x, y, w, 20f), title, label);
            y += 22f;
        }

        // Name lives here rather than only on the connect screen so it can be changed mid-match
        // — PlayerIdentity notices and re-submits. Sanitised on the way in as well as on the
        // server, so what you see in the box is what everyone else will see.
        GUI.Label(new Rect(x, y, w * 0.45f, 20f), "Player name", label);
        string typed = GUI.TextField(new Rect(x + w * 0.45f, y - 2f, w * 0.55f, 24f),
            GameSettings.PlayerName ?? "", PlayerIdentity.MaxNameLength, field);
        bool nameChanged = typed != GameSettings.PlayerName;
        if (nameChanged) GameSettings.PlayerName = PlayerIdentity.Sanitise(typed);
        y += 32f;

        float s = Row(x, y, w, "Mouse sensitivity", GameSettings.Sensitivity,
            GameSettings.SensRange.x, GameSettings.SensRange.y, "0.000");
        y += 40f;
        float vol = Row(x, y, w, "Volume", GameSettings.Volume, 0f, 1f, "0.00");
        y += 40f;
        float fov = Row(x, y, w, "Field of view", GameSettings.Fov,
            GameSettings.FovRange.x, GameSettings.FovRange.y, "0");
        y += 44f;

        // Controls sit next to the sliders rather than in their own menu branch: they are the
        // same question ("how does this play for me"), and a player hunting for sensitivity is
        // the same player who wants to move WASD off a layout that does not have it there.
        float half = (w - 8f) * 0.5f;
        bool openControls = GUI.Button(new Rect(x, y, half, 28f), "Controls…", button);
        bool reset = GUI.Button(new Rect(x + half + 8f, y, half, 28f), "Reset to default", button);

        if (openControls) KeybindsUI.Open = true;

        if (reset)
        {
            GameSettings.ResetToDefaults();
            return true;
        }

        bool changed = !Mathf.Approximately(s, GameSettings.Sensitivity)
                       || !Mathf.Approximately(vol, GameSettings.Volume)
                       || !Mathf.Approximately(fov, GameSettings.Fov);
        if (!changed) return nameChanged;

        GameSettings.Sensitivity = s;
        GameSettings.Volume = vol;
        GameSettings.Fov = fov;
        GameSettings.Apply();
        return true;
    }
}
