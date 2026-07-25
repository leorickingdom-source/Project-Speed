using UnityEngine;

// Third-person camera while dead, aimed at whoever killed you.
//
// Dying used to leave the camera welded inside your own frozen body, staring at whatever you
// happened to be looking at when you lost. That is the one moment where a player most wants
// information — what killed me, from where, was I flanked — and it was the moment the game
// showed them the least. Pulling back and turning to face the killer answers all three without
// a single line of UI.
//
// Owner-only: PlayerNetwork disables this on remote players, and PlayerHealth checks `enabled`
// before driving it, so nobody else's death moves your view.
public class DeathCam : MonoBehaviour
{
    [Tooltip("How far behind the body the camera pulls back.")]
    public float distance = 4.2f;
    [Tooltip("How high above the body it sits.")]
    public float height = 2.6f;
    [Tooltip("Higher snaps to the killer faster. Low enough to read as a turn, not a cut.")]
    public float turnSpeed = 6f;
    public float moveSpeed = 7f;

    [Tooltip("Layers the pull-back is blocked by, so the camera does not end up inside a wall.")]
    public LayerMask blockMask = ~0;

    MouseLook look;
    Transform cam;
    Transform camParent;
    Vector3 camLocalPos;
    Quaternion camLocalRot;

    bool active;
    bool haveTarget;
    Transform killer;    // live, so the camera TRACKS them rather than staring at a memory
    Vector3 target;      // killer's current position, or their last known one once they are gone
    Vector3 anchor;      // where the body was when it died

    void Awake()
    {
        look = GetComponent<MouseLook>();
        var c = GetComponentInChildren<Camera>(true);
        if (c != null) cam = c.transform;
    }

    // killerTransform may be null even when killerPos is not: the killer can leave, die and
    // respawn elsewhere, or simply not be a player. In that case the camera holds on their last
    // known position rather than snapping to wherever their object ended up.
    //
    // Both null means nobody was recorded — a pit fall, the void, or a stale attacker. The
    // camera still pulls back, it just keeps facing the way you were already looking.
    public void Begin(Transform killerTransform, Vector3? killerPos)
    {
        if (active || cam == null) return;

        active = true;
        killer = killerTransform;
        haveTarget = killerPos.HasValue || killerTransform != null;
        target = killerTransform != null ? AimPointOf(killerTransform)
               : killerPos ?? (transform.position + transform.forward * 10f);
        anchor = transform.position;

        // Remember where the camera lived so it can be put back EXACTLY. Reparenting is the
        // one reliable way to move it: while dead the body is frozen, but a reconcile can still
        // snap the parent transform, and a child camera would be dragged along with it.
        camParent = cam.parent;
        camLocalPos = cam.localPosition;
        camLocalRot = cam.localRotation;
        cam.SetParent(null, true);

        // Hand over look control, or MouseLook would fight this for the same rotation.
        if (look != null) look.enabled = false;

        // Start from the current pose and ease out, so death reads as the camera pulling away
        // rather than a hard cut to somewhere else.
        ApplyPose(1f);
    }

    // Chest height rather than the transform origin, which sits at the feet — aiming at the
    // origin puts the killer in the top half of frame and the floor in the bottom half.
    static Vector3 AimPointOf(Transform t) => t.position + Vector3.up * 1.0f;

    // Late killer info. The damage report and the death itself travel as separate messages,
    // so the camera can start with no target and learn who did it a packet later. Re-aiming
    // mid-swing is fine — the easing in ApplyPose turns it into one continuous turn.
    public void Retarget(Transform killerTransform, Vector3? killerPos)
    {
        if (!active) return;
        killer = killerTransform;
        if (killerTransform != null) target = AimPointOf(killerTransform);
        else if (killerPos.HasValue) target = killerPos.Value;
        else return;
        haveTarget = true;
    }

    public void End()
    {
        if (!active) return;
        active = false;
        killer = null;

        if (cam != null)
        {
            cam.SetParent(camParent, false);
            cam.localPosition = camLocalPos;
            cam.localRotation = camLocalRot;
        }

        if (look != null) look.enabled = true;
    }

    // LateUpdate so the body has finished moving for the frame before the camera reads it.
    void LateUpdate()
    {
        if (!active || cam == null) return;
        ApplyPose(Time.unscaledDeltaTime);
    }

    // `step` of 1 snaps; anything smaller eases. Uses UNSCALED time: dying while the pause menu
    // is open would otherwise leave the camera stuck mid-swing at timeScale 0.
    void ApplyPose(float step)
    {
        // Re-read the killer every frame so the camera follows them as they move. The moment
        // they stop existing — despawned, disconnected, or a bot that died — this stops updating
        // and the last known position is what stays on screen, which is better than the camera
        // whipping to a respawn point on the far side of the map.
        if (killer != null) target = AimPointOf(killer);

        Vector3 lookDir = target - anchor;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.01f) lookDir = transform.forward;
        lookDir.Normalize();

        // Behind the corpse relative to the killer, so the shot that killed you is on screen
        // together with the body it hit.
        Vector3 want = anchor - lookDir * distance + Vector3.up * height;

        // Do not pull back through geometry — on a map with a tunnel and a pit, half the deaths
        // would otherwise put the camera inside a wall looking at black.
        Vector3 from = anchor + Vector3.up * height;
        Vector3 delta = want - from;
        float dist = delta.magnitude;
        if (dist > 0.01f && Physics.Raycast(from, delta / dist, out RaycastHit hit, dist,
                blockMask, QueryTriggerInteraction.Ignore))
            want = hit.point - delta / dist * 0.3f;

        Vector3 aimAt = haveTarget ? target : anchor + Vector3.up * 1.2f;
        Quaternion wantRot = Quaternion.LookRotation((aimAt - want).normalized, Vector3.up);

        if (step >= 1f)
        {
            cam.SetPositionAndRotation(want, wantRot);
            return;
        }

        cam.position = Vector3.Lerp(cam.position, want, 1f - Mathf.Exp(-moveSpeed * step));
        cam.rotation = Quaternion.Slerp(cam.rotation, wantRot, 1f - Mathf.Exp(-turnSpeed * step));
    }

    // A disabled or destroyed player must never leave the camera orphaned in the world.
    void OnDisable() => End();
}
