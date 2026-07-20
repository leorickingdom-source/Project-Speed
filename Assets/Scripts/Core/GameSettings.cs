using UnityEngine;

// Player preferences, persisted to PlayerPrefs.
//
// Mouse sensitivity was hardcoded until now, which meant anyone who was not the developer
// played at the developer's setting. In an FPS that is not a missing option — aim preference
// varies enormously, and a player fighting the controls cannot give you usable feedback on
// anything else.
//
// Apply() pushes to live components rather than having each one poll, so a change takes
// effect the instant the slider moves.
public static class GameSettings
{
    const string KeySens = "opt_sensitivity";
    const string KeyVol = "opt_volume";
    const string KeyFov = "opt_fov";

    public static float Sensitivity = 0.08f;   // degrees per mouse pixel
    public static float Volume = 0.7f;
    public static float Fov = 90f;             // FOV at a standstill

    // How much SpeedFeel widens FOV at top speed. Kept as a delta so raising base FOV does
    // not compound into an unusable maximum.
    public const float FovSpeedBoost = 28f;

    public static readonly Vector2 SensRange = new Vector2(0.01f, 0.40f);
    public static readonly Vector2 FovRange = new Vector2(70f, 120f);

    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        Sensitivity = PlayerPrefs.GetFloat(KeySens, Sensitivity);
        Volume = PlayerPrefs.GetFloat(KeyVol, Volume);
        Fov = PlayerPrefs.GetFloat(KeyFov, Fov);
        loaded = true;
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeySens, Sensitivity);
        PlayerPrefs.SetFloat(KeyVol, Volume);
        PlayerPrefs.SetFloat(KeyFov, Fov);
        PlayerPrefs.Save();
    }

    // Push to whatever exists right now. Safe to call before a player has spawned — it simply
    // finds nothing, and PlayerNetwork calls it again once the local player appears.
    public static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(Volume);

        foreach (var ml in Object.FindObjectsByType<MouseLook>(FindObjectsSortMode.None))
            if (ml != null) ml.sensitivity = Sensitivity;

        foreach (var sf in Object.FindObjectsByType<SpeedFeel>(FindObjectsSortMode.None))
        {
            if (sf == null) continue;
            sf.baseFov = Fov;
            sf.maxFov = Fov + FovSpeedBoost;
        }
    }
}
