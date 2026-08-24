using UnityEngine;
using UnityEngine.InputSystem;

// Pulls the camera back behind your own player so you can watch your own animation. F9.
//
// It exists because the body is invisible to the one person who most needs to judge it. The
// rig only renders on REMOTE players — you never see your own — so evaluating the walk, the
// slide or a rocket-jump tuck meant standing up a second client every time. The preview scene
// covers poses in isolation; this covers them while you actually play.
//
// Owner-only, and a view change ONLY: it deliberately does not become a third-person game.
// Two things have to hold for that to be true.
//
// 1. Shots must still leave your EYE. WeaponController aims from its `aim` transform, which is
//    the camera — pull the camera back three metres and every shot would originate three
//    metres behind you, firing through your own shoulders. So the camera moves and an eye
//    anchor stays behind, and `aim` is pointed at the anchor for as long as the view is out.
// 2. The first-person viewmodel has to go. It is drawn by an overlay camera pinned to the
//    view, so from behind it would hang in the air in front of the lens.
[RequireComponent(typeof(PlayerMotor))]
public class ThirdPersonView : MonoBehaviour
{
    [Tooltip("Key that toggles the view. F9 to stay clear of PassivePicker's F1-F7.")]
    public Key toggleKey = Key.F9;

    [Tooltip("How far behind the head the camera sits.")]
    public float distance = 3.2f;
    [Tooltip("How far above it.")]
    public float height = 0.45f;
    [Tooltip("Sideways offset, so the body is not dead centre hiding what it is running at.")]
    public float shoulder = 0.65f;
    [Tooltip("Layers the pull-back is blocked by, so the camera does not end up inside a wall. " +
             "The Hitbox layer is stripped at runtime — your own hitboxes sit exactly where the " +
             "camera is trying to travel, and would pin it to the back of your head.")]
    public LayerMask blockMask = ~0;

    public bool Active { get; private set; }

    Transform cam;
    Transform eye;            // stays at the head, so aiming is unchanged
    Vector3 camLocalOnEnter;  // exact mount to put the camera back on
    PlayerBody body;
    WeaponController weapons;
    WeaponView gunView;

    void Awake()
    {
        var c = GetComponentInChildren<Camera>(true);
        if (c != null) cam = c.transform;
        weapons = GetComponent<WeaponController>();
        blockMask &= ~(1 << PlayerBody.HitboxLayer);
        blockMask &= ~(1 << 2);   // corpses, same reason WeaponController strips them
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();
    }

    public void Toggle() => SetActive(!Active);

    public void SetActive(bool on)
    {
        if (cam == null || on == Active) return;
        Active = on;

        // Re-resolved every toggle, never cached-if-null. Both are added at RUNTIME by other
        // components — PlayerBody by PlayerNetwork, WeaponView by WeaponController.Start — so a
        // lookup that happens to run first gets null, and a `if (x == null)` guard would then
        // hold that null forever and silently stop hiding the viewmodel for the whole session.
        body = GetComponent<PlayerBody>();
        gunView = GetComponent<WeaponView>();

        // Your own body is built but hidden (PlayerBody.Attach takes `visible: !IsOwner`), so
        // there is nothing to spawn here — just something to reveal.
        if (body != null) body.SetVisible(on);
        if (gunView != null) gunView.SetShown(!on);

        if (on)
        {
            // Remembered rather than recomputed. Zeroing it on the way out and trusting
            // PlayerMotor to rewrite eye height works only for as long as the motor is running
            // — pause it, disable it, or die with the view out, and the camera is left down at
            // the player's feet. DeathCam stores and restores for the same reason.
            camLocalOnEnter = cam.localPosition;
            if (eye == null)
            {
                var go = new GameObject("EyeAnchor");
                go.transform.SetParent(cam.parent, false);
                eye = go.transform;
            }
            eye.gameObject.SetActive(true);
            if (weapons != null) weapons.aim = eye;
        }
        else
        {
            if (weapons != null) weapons.aim = cam;
            if (eye != null) eye.gameObject.SetActive(false);
            cam.localPosition = camLocalOnEnter;   // back on the exact mount it left
        }
    }

    // LateUpdate so PlayerMotor has set the eye height and MouseLook has set the pitch. Writing
    // the camera before either would be overwritten by both.
    void LateUpdate()
    {
        if (!Active || cam == null) return;

        // Catch a viewmodel that was built AFTER the view was switched on. Toggling third
        // person during the first frames of a life is exactly when that happens.
        if (gunView == null)
        {
            gunView = GetComponent<WeaponView>();
            if (gunView != null) gunView.SetShown(false);
        }

        // Where the camera WOULD be in first person. The motor drives this every frame as the
        // stance changes, so the anchor tracks a crouch for free.
        Vector3 eyePos = cam.parent != null
            ? cam.parent.TransformPoint(EyeLocal()) : transform.position + Vector3.up * 1.6f;

        if (eye != null)
        {
            eye.position = eyePos;
            eye.rotation = cam.rotation;   // shots go where you are LOOKING, from where your head is
        }

        Vector3 want = eyePos - cam.forward * distance + Vector3.up * height + cam.right * shoulder;

        // Sphere, not a ray: a ray finds the one gap a camera cannot actually fit through.
        Vector3 dir = want - eyePos;
        float dist = dir.magnitude;
        if (dist > 0.001f && Physics.SphereCast(eyePos, 0.22f, dir / dist, out RaycastHit hit,
                                                dist, blockMask, QueryTriggerInteraction.Ignore))
            want = eyePos + dir / dist * Mathf.Max(0f, hit.distance - 0.05f);

        cam.position = want;
    }

    // The motor writes the camera's local Y for stance; anything else about the mount stays put.
    Vector3 EyeLocal()
    {
        var motor = GetComponent<PlayerMotor>();
        float y = motor != null ? Mathf.Max(0.2f, motor.height - 0.4f) : 1.6f;
        return new Vector3(0f, y, 0f);
    }
}
