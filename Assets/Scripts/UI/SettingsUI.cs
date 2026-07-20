using UnityEngine;

// Shared settings block, drawn by both the connect screen and the pause menu.
//
// Lives in one place because they must stay identical: a player who tunes sensitivity in the
// menu and finds different controls in game — or cannot find the setting at all before their
// first match — is exactly the problem settings were added to solve.
public static class SettingsUI
{
    public const float Height = 132f;

    static GUIStyle label, value;

    static void EnsureStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        label.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
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

        float s = Row(x, y, w, "Mouse sensitivity", GameSettings.Sensitivity,
            GameSettings.SensRange.x, GameSettings.SensRange.y, "0.000");
        y += 40f;
        float vol = Row(x, y, w, "Volume", GameSettings.Volume, 0f, 1f, "0.00");
        y += 40f;
        float fov = Row(x, y, w, "Field of view", GameSettings.Fov,
            GameSettings.FovRange.x, GameSettings.FovRange.y, "0");

        bool changed = !Mathf.Approximately(s, GameSettings.Sensitivity)
                       || !Mathf.Approximately(vol, GameSettings.Volume)
                       || !Mathf.Approximately(fov, GameSettings.Fov);
        if (!changed) return false;

        GameSettings.Sensitivity = s;
        GameSettings.Volume = vol;
        GameSettings.Fov = fov;
        GameSettings.Apply();
        return true;
    }
}
