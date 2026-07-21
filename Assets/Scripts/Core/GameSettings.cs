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
    const string KeyName = "opt_playername";

    // Shipped values, named rather than inlined so Reset cannot drift from the initial state
    // the fields below start at.
    public const float DefaultSensitivity = 0.08f;   // degrees per mouse pixel
    public const float DefaultVolume = 0.7f;
    public const float DefaultFov = 90f;             // FOV at a standstill

    public static float Sensitivity = DefaultSensitivity;
    public static float Volume = DefaultVolume;
    public static float Fov = DefaultFov;

    // Empty means "use the colour name". Not reset by ResetToDefaults — wiping someone's name
    // because they wanted the default FOV back is not what that button says it does.
    public static string PlayerName = "";

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
        PlayerName = PlayerIdentity.Sanitise(PlayerPrefs.GetString(KeyName, PlayerName));
        loaded = true;
        Keybinds.Load(); // controls live alongside these; one call site, one place to forget
        Apply();
    }

    // Back to the shipped values. Persists immediately rather than waiting for a menu close,
    // because the whole point of a reset is that the player has got themselves somewhere they
    // cannot see or play their way out of — a slider dragged to 0.01 sensitivity is not
    // recoverable by dragging it back if you cannot turn far enough to find the menu.
    //
    // Controls are NOT reset here. They are a separate axis with their own reset in the
    // rebinding panel, and wiping someone's remapped movement because they wanted the default
    // FOV back would be its own bug report.
    public static void ResetToDefaults()
    {
        Sensitivity = DefaultSensitivity;
        Volume = DefaultVolume;
        Fov = DefaultFov;
        Apply();
        Save();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeySens, Sensitivity);
        PlayerPrefs.SetFloat(KeyVol, Volume);
        PlayerPrefs.SetFloat(KeyFov, Fov);
        PlayerPrefs.SetString(KeyName, PlayerName ?? "");
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
