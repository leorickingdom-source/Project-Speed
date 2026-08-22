using UnityEngine;
using UnityEngine.Rendering.Universal;

// First-person GUN viewmodel: hands and a rifle in the corner of your view, animated.
//
// Until now the project rendered no weapon models at all — KnifeView says so in its own
// header, and built a blade out of primitives because a melee with nothing on screen is an
// invisible attack. The guns had the same hole and nobody noticed, because a hitscan weapon
// announces itself with a tracer. What it does NOT announce is reload state, aim state, or
// even that you are holding a gun rather than a knife: all of that lived in HUD text.
//
// The art is the Easy FPS pack (Assets/Easy FPS). Only its MODEL, RIG and ANIMATOR are used.
// Its scripts are a complete standalone FPS — legacy Input, CharacterController movement, its
// own gun and inventory logic — every part of which this project already has, networked.
// Nothing in Easy FPS/Scripts is referenced here.
//
// Owner-only by construction, same as KnifeView: it parents to the aim camera, which
// PlayerNetwork disables on remote players, and it is built from WeaponController.Start(),
// which never runs on a disabled component. Bystanders see your shots through the tracer.
public class WeaponView : MonoBehaviour
{
    // Layer 9, added to ProjectSettings/TagManager.asset for this class. The viewmodel lives
    // on its own layer so a dedicated overlay camera can draw it AFTER the world with its own
    // near plane — the fix for the oldest artifact in first-person games, a gun barrel that
    // pokes through every wall you stand next to.
    public const int ViewmodelLayer = 9;

    // Both prefabs ship inside the pack's own Resources folder. Which one you get is driven
    // by Weapon.automatic, so a loadout change needs no new wiring here.
    const string AutoModel = "NewGun_auto";
    const string SemiModel = "NewGun_semi";
    const string MuzzleFlash = "EasyFPS/muzzelFlash 03";

    // Camera-space poses, taken from the values the pack's own GunScript was authored with
    // rather than eyeballed — they are what the animations were posed against.
    static readonly Vector3 RestPos = new Vector3(-0.07f, -0.06f, 0.42f);
    static readonly Vector3 AimPos = new Vector3(0f, -0.03f, 0.30f);
    const float AimTime = 0.05f;   // scope-in is near-instant; a slow raise is a lost duel

    // Which way the ART is handed, measured rather than assumed: with the pack's authored pose
    // the gun mesh renders at camera-local x = -0.065, so it sits on the LEFT. Everything below
    // is expressed as "the side the player asked for", and the mirror is whatever it takes to
    // get there — so if the art is ever replaced with a right-handed viewmodel, this one
    // constant moves and no pose or setting changes.
    const bool ArtSitsOnLeft = true;

    // Read from GameSettings rather than serialized, because this component is built at RUNTIME
    // by WeaponController — an inspector value on it could never be set by a player, only by
    // whoever last edited the script.
    bool mirrorToOtherSide;

    [Header("Feel")]
    [Tooltip("Backward/upward kick applied to the model on each shot, in metres. Purely " +
             "cosmetic — recoil that moved the AIM would be a balance change, and this game " +
             "has none.")]
    public float recoilKick = 0.035f;
    [Tooltip("Seconds for the kick to settle. Shorter than any weapon's cycle so a fast gun " +
             "still shows a distinct kick per shot instead of one smeared push.")]
    public float recoilRecover = 0.09f;
    [Tooltip("Speed above which the animator plays the lowered-gun RUN pose. Deliberately " +
             "high: everyone in this game moves at 9+ m/s permanently, so a run threshold set " +
             "anywhere near normal speed would lower the gun forever and you would never see " +
             "the weapon you are aiming. Only grapple and dash velocities drop it.")]
    public float runPoseSpeed = 24f;
    [Tooltip("Ground speed above which the viewmodel stops animating movement and holds its " +
             "idle pose. Matches PlayerBody.runCutoffSpeed by default so the gun and the body " +
             "stop cycling at the same moment — they are the same player, and one still bobbing " +
             "while the other is braced reads as a bug. Reload, melee and aim are unaffected. " +
             "0 keeps the walk cycle running at any speed.")]
    public float idleAboveSpeed = 14f;

    // Animator parameters, exactly as named in the pack's GunAnimator controller.
    static readonly int PWalk = Animator.StringToHash("walkSpeed");        // float, >1 = moving
    static readonly int PMax = Animator.StringToHash("maxSpeed");          // int,   >4 = run pose
    static readonly int PReload = Animator.StringToHash("reloading");      // bool
    static readonly int PAim = Animator.StringToHash("aiming");            // bool
    static readonly int PMelee = Animator.StringToHash("meeleAttack");     // bool

    // The controller's `changingWeapon` bool is NEVER set, and must not be. Every state routes
    // to Character_GunTakeDown on it — a state with ZERO outgoing transitions that plays the
    // takeout clip at speed -1, i.e. the gun swings down out of frame and stays there. The only
    // path out of it in the entire controller is the AnyState edge into Player_Reload, so
    // setting that bool once at spawn hid the weapon until the player happened to press R.
    // A weapon swap replays the takeout STATE directly instead, which is the same animation
    // without the trap door.
    const string TakeoutState = "Character_GUnTakeout";

    // Character_Reload is 2.97s of clip at the state's 1.5x speed, leaving through an exit
    // time of ~0.93 — about 1.84s of animation for a reload the weapons finish in 1.2s or
    // less. Scaling the animator (which drives nothing but this viewmodel) to fit is what
    // stops the hands still working the action after the gun is already loaded.
    const float ReloadStateSeconds = 1.84f;

    Transform cam;
    Transform model;          // the instantiated viewmodel root
    Animator animator;
    Transform muzzle;         // "muzzelSpawn", a bone child of the gun mesh
    GameObject flash;         // one persistent flash, toggled — not spawned per shot
    Camera overlay;
    PlayerMotor motor;

    bool automatic;           // which model is loaded, so Equip can skip a redundant reload
    bool loaded;
    bool aiming;
    float aim01;              // 0 = rest, 1 = scoped
    float recoilUntil;
    float flashUntil;
    float meleeUntil;

    public void Build(Transform aimCamera, PlayerMotor playerMotor)
    {
        if (aimCamera == null || cam != null) return;
        cam = aimCamera;
        motor = playerMotor;
        mirrorToOtherSide = GameSettings.GunOnLeft != ArtSitsOnLeft;
        SetUpOverlayCamera();
    }

    // Put the weapon on the requested side. Rebuilds the model because the mirror is baked into
    // its scale, and flipping the scale of a live skinned mesh mid-animation is the kind of
    // thing that looks fine until the one frame it does not.
    public void SetGunOnLeft(bool onLeft)
    {
        bool want = onLeft != ArtSitsOnLeft;
        if (mirrorToOtherSide == want) return;
        mirrorToOtherSide = want;
        if (loaded)
        {
            bool wasShown = model != null && model.gameObject.activeSelf;
            LoadModel(automatic);
            Equip(automatic, wasShown);
        }
    }

    // A second camera stacked on the player's, drawing nothing but the viewmodel layer with a
    // 1cm near plane. Without it the gun is just geometry in the world and clips through
    // walls; with it the world can never intersect it. URP calls this a camera stack.
    void SetUpOverlayCamera()
    {
        var baseCam = cam.GetComponent<Camera>();
        if (baseCam == null) baseCam = cam.GetComponentInChildren<Camera>();
        if (baseCam == null) return;

        var go = new GameObject("ViewmodelCamera");
        go.transform.SetParent(cam, false);
        overlay = go.AddComponent<Camera>();
        overlay.clearFlags = CameraClearFlags.Depth;
        overlay.cullingMask = 1 << ViewmodelLayer;
        overlay.nearClipPlane = 0.01f;
        overlay.farClipPlane = 10f;      // nothing on this layer is ever further away
        // Its own FOV, unaffected by the sniper's scope zoom: magnifying the target is the
        // point of a scope, magnifying your own hands is not.
        overlay.fieldOfView = 55f;
        overlay.depth = baseCam.depth + 1;

        try
        {
            overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            baseCam.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
            baseCam.cullingMask &= ~(1 << ViewmodelLayer); // world camera must not draw it twice
        }
        catch (System.Exception)
        {
            // Not URP, or stacking unavailable on this renderer. Fall back to drawing the
            // viewmodel on the world camera — it will clip into geometry, which is worse than
            // an overlay and far better than an invisible weapon.
            Destroy(go);
            overlay = null;
            baseCam.cullingMask |= 1 << ViewmodelLayer;
        }
    }

    // Loads (once per model) and shows the viewmodel for the weapon now in hand. `show` is
    // false for the melee weapons, which keep KnifeView's primitives — those have their own
    // shapes, and the pack's sword animations are not wired into its controller.
    public void Equip(bool isAutomatic, bool show)
    {
        if (!show) { if (model != null) model.gameObject.SetActive(false); return; }

        if (!loaded || automatic != isAutomatic)
        {
            LoadModel(isAutomatic);
            automatic = isAutomatic;
        }
        if (model == null) return;
        model.gameObject.SetActive(true);
        // Replay the takeout from the top. On the first equip this is what the controller was
        // already going to do — Character_GUnTakeout is its default state — so it costs
        // nothing there and gives a real swap animation on every equip after.
        if (animator != null && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled)
            animator.Play(TakeoutState, 0, 0f);
    }

    void LoadModel(bool isAutomatic)
    {
        if (cam == null) return;
        if (model != null) Destroy(model.gameObject);
        model = null; animator = null; muzzle = null; flash = null;

        var prefab = Resources.Load<GameObject>(isAutomatic ? AutoModel : SemiModel);
        if (prefab == null) { loaded = true; return; }  // pack missing; everything else works

        // Instantiated under an INACTIVE holder on purpose. The prefab carries the pack's
        // GunScript, whose Awake() does FindGameObjectWithTag("Player").GetComponent<
        // MouseLookScript>() and would throw the instant it woke. A component in an inactive
        // hierarchy never wakes, so it can be stripped before it ever runs.
        var holder = new GameObject("ViewmodelHolder");
        holder.SetActive(false);
        var inst = Instantiate(prefab, holder.transform);

        foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) DestroyImmediate(mb);
        // The pack's own gun audio: this project routes every sound through PlayerAudio so it
        // is 3D and audible to opponents. A viewmodel-local AudioSource would be heard by you
        // and nobody else, which is exactly the information leak footsteps were removed over.
        foreach (var a in inst.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        foreach (var c in inst.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);

        muzzle = FindDeep(inst.transform, "muzzelSpawn");
        animator = inst.GetComponentInChildren<Animator>(true);
        if (animator != null) animator.applyRootMotion = false; // the camera owns our position

        SetLayer(inst.transform, ViewmodelLayer);

        inst.transform.SetParent(cam, false);
        inst.transform.localPosition = Pose(RestPos);
        inst.transform.localRotation = Quaternion.identity;
        // Mirrored on the MODEL, not just the position. Moving the gun across the screen without
        // flipping it shows you the wrong face of the receiver — the ejection port and charging
        // handle end up on the inside, which reads as a different (and broken) weapon. Unity
        // flips triangle winding for a negative-determinant transform, so the mesh still lights
        // correctly; only the geometry is handed the other way.
        if (mirrorToOtherSide)
        {
            var s = inst.transform.localScale;
            inst.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }
        model = inst.transform;
        Destroy(holder);

        BuildFlash();
        loaded = true;
    }

    // One flash, built once and toggled. The pack instantiates a fresh prefab per shot and
    // destroys it 0.8s later; at the SMG's fire rate that is a steady stream of garbage for a
    // quad that is on screen for one frame.
    void BuildFlash()
    {
        if (muzzle == null) return;
        var prefab = Resources.Load<GameObject>(MuzzleFlash);
        if (prefab == null) return;

        var holder = new GameObject("FlashHolder");
        holder.SetActive(false);
        flash = Instantiate(prefab, holder.transform);
        foreach (var mb in flash.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) DestroyImmediate(mb);   // strips the pack's DestroyAfterTimeParticle
        SetLayer(flash.transform, ViewmodelLayer);
        flash.transform.SetParent(muzzle, false);
        flash.transform.localPosition = Vector3.zero;
        flash.SetActive(false);
        Destroy(holder);
    }

    public void Fire()
    {
        recoilUntil = Time.time + recoilRecover;
        if (flash != null)
        {
            // Re-rolled each shot so a burst does not look like one frame repeated.
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.transform.localScale = Vector3.one * Random.Range(0.85f, 1.25f);
            flash.SetActive(true);
            flashUntil = Time.time + 0.045f;
        }
    }

    // A short pulse, not a hold. Every edge into the melee state is condition-only, so the
    // bool just has to survive a few frames — held any longer than the clip's 0.53s exit and
    // the state re-enters itself the moment it tries to leave.
    public void Melee() => meleeUntil = Time.time + 0.12f;
    public void SetAiming(bool on) => aiming = on;

    // Third person turns the viewmodel off. It is drawn by an overlay camera pinned to the
    // view rather than positioned in the world, so from behind the player it would hang in
    // mid-air in front of the lens instead of in their hands.
    // Only the overlay camera is touched. It is the sole thing that draws the viewmodel layer —
    // the world camera has that layer stripped from its mask — so switching it off hides the
    // viewmodel completely, and Equip keeps sole ownership of whether a gun is in hand at all.
    // Reaching in to toggle the model here would put a rifle back in a knife player's hands.
    public void SetShown(bool on)
    {
        if (overlay != null) overlay.enabled = on;
    }

    // Whether there is actually a gun on screen right now. False for the melee weapons, and
    // false when the art pack is not installed — both cases where KnifeView's blade is the
    // only thing that can show a swing.
    public bool Visible => model != null && model.gameObject.activeSelf;

    // Reload is polled from the controller rather than pulsed, so a reload cancelled by death
    // or a weapon swap cannot leave the hands winding a magazine forever. `seconds` is the
    // weapon's own reload time; the animator is stretched to match it, which is safe because
    // this Animator drives the viewmodel and nothing else.
    public void SetReloading(bool on, float seconds)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool(PReload, on);
        animator.speed = on && seconds > 0.05f
            ? Mathf.Clamp(ReloadStateSeconds / seconds, 0.25f, 4f)
            : 1f;
    }

    void LateUpdate()   // after the camera has moved, or the model swims when you flick
    {
        if (flash != null && flash.activeSelf && Time.time >= flashUntil) flash.SetActive(false);
        if (model == null || !model.gameObject.activeSelf) return;

        // Pose: rest <-> aim, plus a decaying kick straight back down the barrel.
        aim01 = Mathf.MoveTowards(aim01, aiming ? 1f : 0f,
                                  Time.deltaTime / Mathf.Max(0.01f, AimTime));
        Vector3 pos = Vector3.Lerp(Pose(RestPos), Pose(AimPos), aim01);
        float kick = recoilUntil > Time.time
            ? (recoilUntil - Time.time) / Mathf.Max(0.001f, recoilRecover) : 0f;
        pos += new Vector3(0f, kick * recoilKick * 0.35f, -kick * recoilKick);
        model.localPosition = pos;

        if (animator == null || animator.runtimeAnimatorController == null) return;
        float speed = motor != null ? motor.Speed : 0f;

        // Above the cutoff the viewmodel stops reporting that it is MOVING, so the walk and run
        // cycles drop out and the weapon holds its idle pose. Same reasoning as the body's own
        // cutoff: the pack's cycles depict a person on foot, and at 20 m/s the bob is a jog
        // animation played over a grapple swing.
        //
        // Done by lying about SPEED rather than by freezing the animator, because reload, melee
        // and aim ride the same controller — stopping it outright would strand a reload
        // half-finished, which is the one gun state a player must be able to read.
        bool tooFastToBob = idleAboveSpeed > 0f && speed > idleAboveSpeed;
        if (tooFastToBob) speed = 0f;

        animator.SetFloat(PWalk, speed > 0.5f ? 2f : 0f);
        // 5, not 6. The controller's edges read this as a band, not a flag: entering the run
        // pose wants > 4, but LEAVING the melee state and — more importantly — the AnyState
        // edge into the reload both require < 6. At 6 a player moving fast could neither
        // finish a swing nor play a reload animation at all.
        //
        // Suppressed outright while reloading. Player_Reload -> Player_Run carries no exit
        // time, so a fast player entered and left the reload pose on alternate frames. The
        // pack's controller never expected anyone to reload at sprint speed; here that is
        // most reloads.
        bool reloading = animator.GetBool(PReload);
        animator.SetInteger(PMax, !reloading && speed >= runPoseSpeed ? 5 : 3);
        animator.SetBool(PAim, aiming);
        animator.SetBool(PMelee, Time.time < meleeUntil);
    }

    // Every authored pose runs through here, so the side is decided in exactly one place and a
    // future pose cannot be added that forgets to mirror.
    Vector3 Pose(Vector3 p) => mirrorToOtherSide ? new Vector3(-p.x, p.y, p.z) : p;

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}
