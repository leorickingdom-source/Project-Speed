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

    [Header("Burst kick")]
    [Tooltip("Extra FOV punched in the instant you dash, decaying over ~a quarter second. " +
             "The steady speed->FOV map above cannot sell a BURST: a forward dash at speed " +
             "changes velocity by a few m/s, which moves the smooth curve barely a degree — " +
             "hence the playtest note that a forward dash feels like nothing. The kick is the " +
             "event cue; the curve remains the state cue.")]
    public float dashKickFov = 14f;
    [Tooltip("Same, for the double jump's air jump.")]
    public float jumpKickFov = 9f;
    [Tooltip("How fast the kick decays. ~5 reads as a snap that settles in a quarter second.")]
    public float kickDecay = 5f;

    float kick;
    float lastDashCooldown;
    int lastAirJumpsLeft = -1;
    WeaponController weapon;

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInParent<WeaponController>();
        if (motor == null) motor = GetComponentInParent<PlayerMotor>();
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null && motor != null) cam = motor.GetComponentInChildren<Camera>();
    }

    void LateUpdate()
    {
        if (motor == null || cam == null) return;

        // Scope wins outright: no speed widening, no burst kick. Those exist to SELL speed,
        // and a scope that breathed with your velocity would make a precision tool feel like
        // it was drifting. Snappier lerp than the speed curve so it reads as optics, not drag.
        if (weapon != null && weapon.Scoped)
        {
            var w = weapon.CurrentWeapon;
            float scopeTarget = w != null && w.scopeFov > 0f ? w.scopeFov : baseFov;
            kick = 0f;
            lastDashCooldown = motor.DashCooldownLeft;
            lastAirJumpsLeft = motor.AirJumpsLeft;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, scopeTarget,
                1f - Mathf.Exp(-18f * Time.deltaTime));
            return;
        }

        // Bursts are DETECTED from motor state rather than pushed from PlayerMotor, keeping
        // the deterministic sim free of any camera dependency — the same pattern PlayerAudio
        // uses for the dash sound.
        float cd = motor.DashCooldownLeft;
        if (cd > lastDashCooldown + 0.01f) kick = Mathf.Max(kick, dashKickFov);
        lastDashCooldown = cd;

        int aj = motor.AirJumpsLeft;
        if (lastAirJumpsLeft >= 0 && aj < lastAirJumpsLeft) kick = Mathf.Max(kick, jumpKickFov);
        lastAirJumpsLeft = aj;

        kick *= Mathf.Exp(-kickDecay * Time.deltaTime);

        float t = Mathf.Clamp01(motor.Speed / Mathf.Max(0.01f, speedForMaxFov));
        float target = Mathf.Lerp(baseFov, maxFov, t) + kick;
        float k = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, k);
    }
}
