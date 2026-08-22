using UnityEngine;

// The third-person player: a rigged humanoid where there used to be a coloured capsule.
//
// A capsule tells you WHERE someone is and nothing else. It has no front, so it cannot tell
// you which way they are facing — in an arena shooter that is the single most valuable thing
// to read off an enemy, because it is the difference between flanking someone and walking
// into their crosshair. A humanoid answers it from the silhouette alone, at any range.
//
// Animated procedurally, with no clips and no AnimatorController. That is not a placeholder
// for "real" animation — it is the same choice every other visual in this project already
// makes (KnifeView, CorpseFx, BlastFx, the head caps), and it buys two things clips do not:
// the pose is a pure function of motor state, so it is exactly as correct on a remote player
// as on your own, and it costs ZERO bandwidth. PlayerAudio derives jumps and landings the
// same way and for the same reason.
//
// Bones are driven directly through the Humanoid avatar's GetBoneTransform, so swapping in
// Mixamo clips later means adding an AnimatorController and deleting Pose() — the rig,
// prefab, colouring and crouch handling below all stay exactly as they are.
//
// The COLLIDER is untouched. This class changes what a player looks like and nothing about
// what a shot hits: hitscan, the headshot band and the crouch capsule all still run against
// the capsule PlayerMotor has always driven, which is simply no longer rendered.
public class PlayerBody : MonoBehaviour
{
    // Mixamo's own mannequin. Switched from the Kevin Iglesias dummy because the clips are
    // NATIVE to this skeleton — no retargeting step at all, so none of the retarget error that
    // had feet sinking and hips landing at the wrong height. Costs ~28k verts against 8.5k.
    const string PrefabPath = "X Bot";

    // Layer 10, added to TagManager for the animated hitboxes below.
    public const int HitboxLayer = 10;

    // True once any body in the scene has built hitboxes along its skeleton. WeaponController
    // reads it to decide whether the player capsule is still a thing bullets talk to — see
    // WeaponController.HitMask. Static because the question is about the SCENE, not about one
    // player: the moment rigs exist, shooting a capsule would beat shooting a head.
    public static bool RigHitboxesInUse { get; private set; }

    [Header("Hitboxes")]
    [Tooltip("Build colliders along the skeleton so shots are tested against the body you can " +
             "actually see, instead of a capsule that ignores what the body is doing. Off " +
             "falls back to the root capsule and the old top-of-bounds headshot band.")]
    public bool rigHitboxes = true;
    public float headRadius = 0.115f;
    public float torsoRadius = 0.17f;
    public float armRadius = 0.055f;
    public float legRadius = 0.085f;

    [Header("Stride")]
    [Tooltip("Stride cycles per metre travelled. Phase advances with DISTANCE, not time, so " +
             "the legs cannot skate: at double the speed you take double the steps rather " +
             "than the same steps twice as fast.")]
    public float stridesPerMetre = 0.42f;
    [Tooltip("Peak hip swing in degrees at full stride.")]
    public float legSwing = 38f;
    [Tooltip("Peak arm swing, counter-phase to the legs.")]
    public float armSwing = 26f;

    [Header("Arms")]
    [Tooltip("Degrees the upper arms are rotated DOWN out of the rig's bind pose. The model is " +
             "authored in a T-pose — arms straight out sideways — so without this every player " +
             "runs around like a scarecrow, which is exactly how it first looked. Solved against " +
             "the rig rather than guessed: Z is the axis that lowers the arm and it mirrors per " +
             "side, while X swings it forward and back.")]
    public float armRestDown = 72f;
    [Tooltip("Resting elbow bend. Straight arms read as a mannequin however well the shoulders " +
             "swing.")]
    public float elbowBend = 22f;
    [Tooltip("Speed at which the stride reaches full amplitude. Below it the swing scales " +
             "down, so a player edging around a corner does not sprint on the spot.")]
    public float fullStrideSpeed = 9f;

    [Header("Lean")]
    [Tooltip("Degrees of forward lean at full stride speed. Sells momentum, and reads as " +
             "intent — a leaning player is committed to a direction.")]
    public float leanDegrees = 12f;
    [Tooltip("How fast the lean follows a change of direction. Low enough that a strafe " +
             "flick does not snap the whole body sideways.")]
    public float leanLerp = 8f;

    [Header("Air")]
    [Tooltip("Knee tuck while airborne. A player in the air holds a distinctly different " +
             "shape from one on the ground, which is what makes a rocket jump readable.")]
    public float airTuck = 42f;

    [Header("Crouch")]
    [Tooltip("Metres the hips drop at full crouch. Less than the capsule's full 1m of travel " +
             "because the knee bend below accounts for the rest — together they land the head " +
             "inside the crouched capsule instead of hovering above it.")]
    public float crouchHipDrop = 0.70f;
    [Tooltip("Hip flexion at full crouch. With crouchKneeFlex this decides where the feet end " +
             "up: too little and the legs are longer than the space under the dropped hips, so " +
             "the feet punch through the floor; too much and the model hangs off the ground.")]
    public float crouchHipFlex = 40f;
    [Tooltip("Knee flexion at full crouch.")]
    public float crouchKneeFlex = 30f;
    [Tooltip("Exponent on how early the legs fold relative to the hips dropping. The hips must " +
             "track the capsule LINEARLY or the head leaves the collider partway through the " +
             "crouch — a head you can see and cannot shoot, which is the exact mismatch the " +
             "capsule crouch was written to avoid. The fold therefore has to lead: below 1 it " +
             "front-loads, keeping the feet on the floor while the hips are still on their way " +
             "down. 1 = fold and drop together, which sinks a shin through the ground.")]
    [Range(0.1f, 1f)] public float crouchFoldLead = 0.5f;

    [Header("Clips")]
    [Tooltip("Seconds to cross between the clip pose and the procedural one. The two are " +
             "blended rather than switched: a hard cutover pops, and the cutover happens in " +
             "the middle of a fight — the moment you start backpedalling out of one.")]
    public float clipBlendTime = 0.18f;
    [Tooltip("Speed of the fastest locomotion clip, in m/s — the outer ring of the blend tree. " +
             "That is the RUN set now (4.58, measured off its own root motion); the sprint clips " +
             "were removed. Both the parameter clamp and the playback-rate scaling hang off " +
             "this, so it has to match whatever the controller's outer ring actually is.")]
    public float topClipSpeed = 4.58f;
    [Tooltip("Ceiling on clip playback rate. A grapple hits 28 m/s, six times the run clip's " +
             "4.58; letting the rate follow that turns the animation into a blur, so it caps " +
             "and accepts foot sliding at speeds no cycle was ever authored for. Note the " +
             "consequence: topClipSpeed * this is the fastest HONEST ground speed (about 10 " +
             "m/s), and between there and runCutoffSpeed the feet do slide.")]
    public float maxPlaybackRate = 2.2f;
    [Tooltip("Ground speed above which the run cycle STOPS and the body holds a planted speed " +
             "pose instead. There is no honest way to animate stepping this far past the run " +
             "clip's own 4.58 m/s — the legs are covering ground no stride could, so cycling " +
             "them faster only reads as running on the spot. Held legs at speed read as momentum " +
             "carrying you, which is what is actually happening. Drop this toward 10 to hand over " +
             "exactly where playback rate runs out; 0 disables the cutoff entirely.")]
    public float runCutoffSpeed = 14f;

    const string ControllerPath = "PlayerLocomotion";   // Assets/Mixamo/Resources
    static readonly int PMoveX = Animator.StringToHash("moveX");
    static readonly int PMoveZ = Animator.StringToHash("moveZ");
    static readonly int PCrouch = Animator.StringToHash("crouch");
    static readonly int PAir = Animator.StringToHash("airborne");
    static readonly int PSlide = Animator.StringToHash("sliding");
    static readonly int PFast = Animator.StringToHash("fastMove");

    Transform hips, spine, chest, head;
    Transform legLU, legLL, legRU, legRL;
    Transform armLU, armLL, armRU, armRL;
    Quaternion[] bind;          // bind-pose local rotations, so every pose is an OFFSET
    Transform[] bones;
    Vector3 hipsBindPos;        // rest hip height, so the crouch drop is relative to the rig

    PlayerMotor motor;
    Transform model;
    Animator animator;          // null / no controller = pure procedural, exactly as before
    float blend = 1f;           // 1 = procedural owns the pose, 0 = the clips do
    float phase;                // stride phase in radians, advanced by distance
    float lean;                 // smoothed forward lean, degrees
    float strafeLean;           // smoothed sideways lean, degrees

    // Built at runtime like every other visual here, so it cannot be half-configured in the
    // inspector. `visible` is false for the owner: you never see your own body, and rendering
    // it would put a torso through the first-person camera.
    // `hitboxes` is deliberately separate from `visible`, because third person makes the OWNER's
    // body visible and that must not give them hitboxes. Nothing ever tests a hit against your
    // own copy of yourself — the shooter's client decides its own hits and nobody shoots
    // themselves — so the owner's colliders would be pure cost, and worse than free: they sit
    // inside the owner's own movement capsule.
    public static PlayerBody Attach(Transform playerRoot, Color colour, bool visible, bool hitboxes)
    {
        // Idempotent. Offline play builds the body from Start(); a client connecting afterwards
        // runs OnStartClient and would otherwise bolt a second humanoid onto the same player.
        var existing = playerRoot.GetComponent<PlayerBody>();
        if (existing != null)
        {
            existing.Tint(existing.model.gameObject, colour);
            existing.SetVisible(visible);
            return existing;
        }

        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null) return null;      // pack not installed — caller keeps the capsule

        var go = Instantiate(prefab, playerRoot);
        go.name = "BodyModel";
        go.transform.localPosition = Vector3.zero;   // player origin is at the feet
        go.transform.localRotation = Quaternion.identity;

        var pb = playerRoot.gameObject.AddComponent<PlayerBody>();
        pb.rigHitboxes &= hitboxes;
        pb.model = go.transform;
        pb.motor = playerRoot.GetComponent<PlayerMotor>();
        pb.Bind(go);
        pb.Tint(go, colour);
        go.SetActive(visible);
        return pb;
    }

    public void SetVisible(bool on)
    {
        if (model != null) model.gameObject.SetActive(on);
    }

    // Colliders bolted to the skeleton, so what a shot tests against is the pose you can see.
    //
    // Only ever built on REMOTE players, because the model itself only exists there — and that
    // happens to be exactly right. The shooter's client already decides its own hits (see
    // WeaponController.ApplyDamage and PlayerNetwork.ReportHit), so the hitboxes that matter
    // are the ones on the shooter's screen. This makes the target's on-screen body and the
    // thing the ray tests the same object, which is stronger than what the capsule offered:
    // the capsule was a fixed pill that a crouching or sliding player visibly left behind.
    void BuildHitboxes(Animator anim)
    {
        // Head first — the one that changes how the game is aimed.
        var headT = anim.GetBoneTransform(HumanBodyBones.Head);
        if (headT != null)
        {
            var go = NewBox(headT, Hitbox.Part.Head);
            var c = go.AddComponent<SphereCollider>();
            c.radius = headRadius;
            // Lifted onto the SKULL rather than sitting on the head bone, which is down at the
            // base of the neck. Aiming where the head visibly is has to be what hits it.
            c.center = new Vector3(0f, headRadius * 0.85f, 0f);
        }

        Bone(HumanBodyBones.Spine, HumanBodyBones.Neck, torsoRadius, Hitbox.Part.Torso, anim);
        Bone(HumanBodyBones.Hips, HumanBodyBones.Spine, torsoRadius * 0.95f, Hitbox.Part.Pelvis, anim);

        Bone(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, armRadius, Hitbox.Part.Arm, anim);
        Bone(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, armRadius * 0.9f, Hitbox.Part.Arm, anim);
        Bone(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, armRadius, Hitbox.Part.Arm, anim);
        Bone(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, armRadius * 0.9f, Hitbox.Part.Arm, anim);

        Bone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, legRadius, Hitbox.Part.Leg, anim);
        Bone(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, legRadius * 0.85f, Hitbox.Part.Leg, anim);
        Bone(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, legRadius, Hitbox.Part.Leg, anim);
        Bone(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, legRadius * 0.85f, Hitbox.Part.Leg, anim);

        RigHitboxesInUse = true;
    }

    // A capsule spanning two bones, parented to the first so it follows the animation for free.
    void Bone(HumanBodyBones a, HumanBodyBones b, float radius, Hitbox.Part part, Animator anim)
    {
        var ta = anim.GetBoneTransform(a);
        var tb = anim.GetBoneTransform(b);
        if (ta == null || tb == null) return;

        float len = Vector3.Distance(ta.position, tb.position);
        if (len < 0.01f) return;                       // degenerate bone pair; skip rather than
                                                       // leave a zero-height collider behind
        var go = NewBox(ta, part);
        // Pointed down the bone, so the capsule stays aligned however the limb rotates.
        go.transform.rotation = Quaternion.LookRotation(tb.position - ta.position, ta.up);
        var c = go.AddComponent<CapsuleCollider>();
        c.direction = 2;                               // Z, the axis we just aimed
        c.radius = radius;
        // Unity counts the hemispherical caps INSIDE height, so `len + 2r` would push a cap a
        // full radius past each end of the bone. On the torso that put 17cm of chest collider
        // above the neck and through the skull, and it won the headshot: a ray aimed at the
        // head reached the fatter capsule first. `len` keeps the span between the two joints,
        // with only the caps' curvature reaching past.
        // Half a radius of overhang at each end. A full radius (`len + 2r`) put 17cm of chest
        // above the neck and stole headshots; none at all (`len`) left a 10cm hole at the
        // throat that shots passed straight through. Half closes the joint without reaching
        // the skull.
        c.height = Mathf.Max(len + radius, radius * 2f);
        c.center = new Vector3(0f, 0f, len * 0.5f);
    }

    GameObject NewBox(Transform parent, Hitbox.Part part)
    {
        var go = new GameObject("HB_" + part);
        go.layer = HitboxLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<Hitbox>().part = part;
        return go;
    }

    void Bind(GameObject go)
    {
        var anim = go.GetComponentInChildren<Animator>(true);
        if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return;
        animator = anim;

        // Mixamo locomotion, retargeted through the Humanoid avatar. Optional by construction:
        // with no controller found the body poses exactly as it did before the clips existed,
        // so deleting Assets/Mixamo degrades the animation rather than breaking the player.
        var ctrl = Resources.Load<RuntimeAnimatorController>(ControllerPath);
        if (ctrl != null)
        {
            anim.runtimeAnimatorController = ctrl;
            // The clips carry root curves. PlayerMotor owns position and always has, so the
            // animation has to run in place or the body would try to walk itself off the player.
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        legLU = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        legLL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        legRU = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        legRL = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        armLU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        armLL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        armRU = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        armRL = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

        bones = new[] { hips, spine, chest, head, legLU, legLL, legRU, legRL,
                        armLU, armLL, armRU, armRL };
        // Every pose below is applied as an offset FROM the bind pose rather than as an
        // absolute rotation. Absolute rotations would bake this rig's particular rest pose
        // into the maths and break the moment the model is swapped for another humanoid.
        bind = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            bind[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
        hipsBindPos = hips != null ? hips.localPosition : Vector3.zero;

        if (rigHitboxes) BuildHitboxes(anim);
    }

    // One material on the whole character, which is what makes this drop-in: the same single
    // SetColor the capsule used still identifies a player.
    void Tint(GameObject go, Color colour)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.material;                       // instance, per player
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            m.color = colour;
            // Emission is forced off rather than assumed off. The imported material arrived
            // with _EMISSION enabled and a 0.71 grey emission colour — left over from the
            // shader it had before the URP conversion — which swamped the albedo and rendered
            // every player as the same near-white figure whatever colour they were assigned.
            // Player identity is the entire job of this tint, so it cannot depend on an
            // imported material happening to be set up correctly.
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
    }

    // LateUpdate, after PlayerMotor has finished moving the root for the frame — posing in
    // Update would leave the body one frame behind its own position at 9+ m/s.
    void LateUpdate()
    {
        if (motor == null || bind == null || model == null || !model.gameObject.activeSelf) return;
        Pose();
    }

    void Pose()
    {
        float dt = Time.deltaTime;
        float speed = motor.Speed;
        // Velocity in the body's own frame: forward/back drives the stride direction, sideways
        // drives the strafe lean. Both come off the replicated motor state, so a remote player
        // leans and strides correctly without a single extra byte on the wire.
        Vector3 local = model.InverseTransformDirection(new Vector3(motor.velocity.x, 0f, motor.velocity.z));
        // Crouch is read off the CAPSULE rather than a flag, so the visual stance can never
        // disagree with the collider a shot is tested against — including mid-transition,
        // where the capsule is between heights and so is the body.
        float stance = Mathf.Clamp01((motor.height - motor.crouchHeight)
                                     / Mathf.Max(0.01f, motor.standHeight - motor.crouchHeight));
        float crouch = 1f - stance;

        // Stride amplitude, faded out by the crouch. Partly because it is true — crouching here
        // is overwhelmingly SLIDING, and a slide is not a sequence of steps — and partly
        // because the swing fought the fold: ±38 degrees of hip added on top of a crouched leg
        // lifted the feet 30cm off the floor and pushed the skull back out of the capsule.
        float amp = Mathf.Clamp01(speed / Mathf.Max(0.01f, fullStrideSpeed)) * stance;

        // Distance-driven phase. Signed by the direction of travel so backpedalling reverses
        // the stride instead of moonwalking.
        float dir = local.z >= 0f ? 1f : -1f;
        phase += speed * dt * stridesPerMetre * Mathf.PI * 2f * dir;

        bool air = !motor.grounded;
        float swing = Mathf.Sin(phase) * legSwing * amp;
        float arm = Mathf.Sin(phase) * armSwing * amp;

        // Feed the blend tree, in REAL m/s. The tree's rings ARE the speeds the clips depict —
        // walk 1.84, run 4.58, sprint 6.83, each measured off its own root motion. The previous
        // version normalised against fullStrideSpeed (9) instead, which parked the walk clip at
        // 4 m/s and the sprint clip at 20: every clip ran at two to three times the ground speed
        // it was authored for, and the whole set skated. Walking looked least wrong only because
        // its absolute error was smallest.
        //
        // Damped, because these come off a physics velocity that jitters sub-frame and an
        // undamped tree turns that jitter into visible foot chatter.
        bool haveClips = animator != null && animator.runtimeAnimatorController != null;
        if (haveClips)
        {
            Vector2 planar = new Vector2(local.x, local.z);
            if (planar.magnitude > topClipSpeed) planar = planar.normalized * topClipSpeed;
            animator.SetFloat(PMoveX, planar.x, 0.10f, dt);
            animator.SetFloat(PMoveZ, planar.y, 0.10f, dt);
            animator.SetFloat(PCrouch, crouch, 0.08f, dt);
            animator.SetBool(PAir, air);
            // Read straight off the motor's replicated slide flag, so a remote player's slide
            // is animated from the same fact the server used to move them.
            animator.SetBool(PSlide, motor.sliding);
            // Above the fastest clip the tree has nothing left to blend towards, so the surplus
            // goes into PLAYBACK RATE — the only lever that actually keeps stride length near
            // ground speed. Capped, because a grapple reaches 28 m/s and no playback rate makes
            // a 6.8 m/s cycle read correctly at four times its speed.
            //
            // Grounded locomotion ONLY. The rate exists to match stride length to ground speed,
            // and neither the airborne hold nor the slide has a stride to match: scaling them
            // just ran a jump loop and a slide at 2x for no reason other than that the player
            // happened to be travelling fast, which is precisely when both are used.
            // Above the cutoff the legs stop stepping and hold a braced pose. Grounded only —
            // airborne and sliding already have their own held shapes and must not be overridden
            // by one that means the same thing.
            bool tooFastToRun = runCutoffSpeed > 0f && !air && !motor.sliding && speed > runCutoffSpeed;
            animator.SetBool(PFast, tooFastToRun);

            bool cycling = !air && !motor.sliding && !tooFastToRun;
            animator.speed = cycling && speed > topClipSpeed
                ? Mathf.Min(speed / topClipSpeed, maxPlaybackRate) : 1f;
        }

        // How much of the pose the PROCEDURAL path owns. The clip set covers every DIRECTION
        // the motor can move — eight of them at three speeds, plus a jump whose middle section
        // loops, which is what finally makes unbounded grapple airtime animatable. Backpedal
        // and airborne no longer need the fallback at all.
        //
        // Clips own everything they cover, which is now every state the motor has.
        //
        // They used to hand crouch and slide back to the procedural pose, because a Mixamo
        // crouch stands 1.38m and its slide 1.68m against a 1.0m crouch capsule — and a visible
        // torso outside the collider is a torso that cannot be shot. That reason is gone: the
        // hitboxes are bolted to this skeleton now, so they crouch and lie down WITH it and the
        // visual cannot disagree with what a bullet tests. The handoff was buying a lie that no
        // longer exists, at the price of the procedural slide, which read as a melted crouch.
        //
        // The one cost left is different in kind: the MOVEMENT capsule is still 1m, so a sliding
        // player's torso can pass through something low they slid under. That is a visual clip,
        // not an unfair hitbox.
        float wantBlend = haveClips ? 0f : 1f;
        blend = Mathf.MoveTowards(blend, wantBlend, dt / Mathf.Max(0.01f, clipBlendTime));
        if (haveClips) animator.enabled = blend < 0.999f;   // nothing to compute at full procedural

        // Lean into the direction of travel, smoothed so a strafe flick does not snap.
        float wantLean = amp * leanDegrees;
        float wantStrafe = Mathf.Clamp(local.x / Mathf.Max(0.01f, fullStrideSpeed), -1f, 1f) * leanDegrees * 0.6f;
        lean = Mathf.Lerp(lean, air ? wantLean * 0.4f : wantLean, dt * leanLerp);
        strafeLean = Mathf.Lerp(strafeLean, wantStrafe, dt * leanLerp);

        // The hips drop and the legs fold on DIFFERENT curves. Linear on both put 22cm of shin
        // through the floor halfway through the crouch: the drop is a straight line, the fold
        // needed to keep the feet planted is not. The drop STAYS linear — it is what keeps the
        // head inside the shrinking capsule — and the fold leads it instead.
        float dropT = crouch;
        float flexT = Mathf.Pow(crouch, crouchFoldLead);

        if (blend <= 0.001f) return;   // the clips own the body outright; nothing to layer on

        // Hips: drop and pitch with the crouch, lean with the travel.
        if (hips != null)
        {
            Apply(0, bind[0] * Quaternion.Euler(lean + crouch * 25f, 0f, -strafeLean));
            // Squatting is a hip TRANSLATION, not a squash — the whole reason a humanoid
            // cannot simply be scaled down the Y axis the way the capsule is.
            Vector3 hipTarget = hipsBindPos - new Vector3(0f, dropT * crouchHipDrop, 0f);
            hips.localPosition = blend >= 0.999f
                ? hipTarget : Vector3.Lerp(hips.localPosition, hipTarget, blend);
        }
        Apply(1, bind[1] * Quaternion.Euler(-lean * 0.3f + crouch * 12f, 0f, 0f));
        Apply(2, bind[2] * Quaternion.Euler(-lean * 0.3f, 0f, 0f));
        // Head counter-rotates the lean so it stays level. A head that pitches with the torso
        // reads as a body without one.
        Apply(3, bind[3] * Quaternion.Euler(-lean - crouch * 30f, 0f, 0f));

        if (air)
        {
            // Airborne: both knees tucked, arms out. A held shape, not a cycle — the point is
            // that "in the air" is instantly distinguishable from "running".
            SetLimb(4, 5, -airTuck, airTuck * 1.4f);
            SetLimb(6, 7, -airTuck * 0.6f, airTuck * 1.1f);
            // Arms come up and out — less of the resting down-rotation, more elbow. A body in
            // the air should not hold the same shape as one running.
            SetArm(8, 9, -30f, elbowBend * 1.8f, 0.55f);
            SetArm(10, 11, -30f, elbowBend * 1.8f, -0.55f);
            return;
        }

        // Grounded: opposed leg swing, knees bending on the back half of each stride.
        float kneeL = Mathf.Max(0f, -Mathf.Sin(phase)) * 55f * amp;
        float kneeR = Mathf.Max(0f, Mathf.Sin(phase)) * 55f * amp;
        SetLimb(4, 5, swing + flexT * crouchHipFlex, kneeL + flexT * crouchKneeFlex);
        SetLimb(6, 7, -swing + flexT * crouchHipFlex, kneeR + flexT * crouchKneeFlex);
        // Arms counter-swing the legs, which is what stops a walk reading as a shamble.
        SetArm(8, 9, -arm, elbowBend + Mathf.Abs(arm) * 0.4f, 1f);
        SetArm(10, 11, arm, elbowBend + Mathf.Abs(arm) * 0.4f, -1f);
    }

    // Arms need a second axis the legs do not. The rig rests in a T-pose, so every arm pose is
    // the swing (X) PLUS a constant rotation down to the side (Z), mirrored by `side`. Legs
    // already hang correctly in the bind pose and need only the swing.
    void SetArm(int upper, int lower, float pitch, float bend, float side)
    {
        Apply(upper, bind[upper] * Quaternion.Euler(pitch, 0f, side * armRestDown));
        Apply(lower, bind[lower] * Quaternion.Euler(bend, 0f, 0f));
    }

    // Upper limb pitches, lower limb bends. Indices are into `bones`/`bind` so a missing bone
    // on some other humanoid rig is skipped rather than throwing every frame.
    void SetLimb(int upper, int lower, float upperPitch, float lowerBend)
    {
        Apply(upper, bind[upper] * Quaternion.Euler(upperPitch, 0f, 0f));
        Apply(lower, bind[lower] * Quaternion.Euler(lowerBend, 0f, 0f));
    }

    // The one place a procedural rotation reaches a bone, so the clip/procedural crossfade is
    // expressed once instead of at fourteen call sites.
    //
    // Slerping FROM the bone's current value is what makes the blend free: by LateUpdate the
    // Animator has already written the clip pose into that same field, so the bone is literally
    // holding the other half of the mix. No second skeleton, no snapshot, no sampling the tree
    // twice — and when there are no clips at all the weight is 1 and this is a plain assignment.
    void Apply(int i, Quaternion target)
    {
        var t = bones[i];
        if (t == null) return;
        t.localRotation = blend >= 0.999f ? target : Quaternion.Slerp(t.localRotation, target, blend);
    }
}
