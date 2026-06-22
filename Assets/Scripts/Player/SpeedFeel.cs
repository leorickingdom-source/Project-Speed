using UnityEngine;

// Widens camera FOV as you go faster so speed is *felt*, not just a HUD number.
// Cheap, compositor-friendly, and the single biggest "sense of speed" win.
public class SpeedFeel : MonoBehaviour
{
    public PlayerMotor motor;
    public Camera cam;

    [Tooltip("FOV at a standstill / walking.")]
    public float baseFov = 90f;
    [Tooltip("FOV at top speed.")]
    public float maxFov = 118f;
    [Tooltip("Speed (m/s) that maps to maxFov.")]
    public float speedForMaxFov = 20f;
    [Tooltip("How snappy the FOV reacts.")]
    public float responsiveness = 8f;

    void Awake()
    {
        if (motor == null) motor = GetComponentInParent<PlayerMotor>();
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null && motor != null) cam = motor.GetComponentInChildren<Camera>();
    }

    void LateUpdate()
    {
        if (motor == null || cam == null) return;
        float t = Mathf.Clamp01(motor.Speed / Mathf.Max(0.01f, speedForMaxFov));
        float target = Mathf.Lerp(baseFov, maxFov, t);
        float k = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, k);
    }
}
