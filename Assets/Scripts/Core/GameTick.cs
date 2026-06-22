using UnityEngine;

// Locks physics to a high fixed tick so Quake-style air control is crisp and
// frame-rate independent. Attach once (on the Player is fine).
public class GameTick : MonoBehaviour
{
    [Tooltip("Physics ticks per second. 100 = crisp strafe control.")]
    public int physicsHz = 100;

    [Tooltip("Render frame cap. 0 leaves it uncapped.")]
    public int targetFps = 240;

    void Awake()
    {
        Time.fixedDeltaTime = 1f / Mathf.Max(30, physicsHz);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFps > 0 ? targetFps : -1;
        // Keep simulating even when the editor window isn't focused (so play mode
        // advances while tools drive it, and alt-tabbing mid-match doesn't freeze).
        Application.runInBackground = true;
    }
}
