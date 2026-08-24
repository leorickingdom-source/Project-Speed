using UnityEngine;

// Turns a dead player's humanoid into a physics ragdoll.
//
// Replaces the falling capsule CorpseFx used to spawn, which stopped reading as a death the
// moment players got bodies: the humanoid froze mid-stride where it stood while a plain capsule
// toppled over beside it. Two figures, one of them clearly the player and clearly still upright.
//
// Built from a CLONE of the body rather than the body itself. The live model belongs to a
// player who is about to respawn wearing it, and handing it to the physics engine means either
// rebuilding it every life or unpicking joints and rigidbodies afterwards. The clone is a
// throwaway: it starts in the exact pose the player died in, falls over, and is destroyed.
//
// Colliders are built here rather than borrowed from the hitbox rig, even though the shapes are
// the same. The rig only exists on REMOTE players, so borrowing it would leave a corpse with no
// collision on the machine of the player who actually died — the one guaranteed to be watching
// it, from the death camera.
public static class Ragdoll
{
    // Same layer the capsule corpse used: a body on the floor must never eat a live shot.
    const int IgnoreRaycastLayer = 2;

    // Roughly a human's distribution over the parts we simulate. The total lands near the 70kg
    // the capsule corpse used, so the shove below still reads the same.
    const float HipsMass = 15f, SpineMass = 15f, HeadMass = 5f;
    const float UpperArmMass = 3f, LowerArmMass = 2f;
    const float UpperLegMass = 8f, LowerLegMass = 4f;

    // One bone of the ragdoll: which transform, what it hangs off, how heavy, how fat.
    struct Part
    {
        public HumanBodyBones bone, end, parent;
        public float mass, radius;
        public bool sphere;
    }

    // `rig` supplies the radii so the corpse is exactly as wide as the target was a moment ago.
    // `awayFrom` is the killer's position when known — the body falls away from the shot, the
    // detail that makes the physics read as caused rather than random.
    public static GameObject Spawn(PlayerBody rig, Transform model, Vector3? awayFrom,
                                   float impulse, float spin, float lifetime)
    {
        if (rig == null || model == null) return null;

        var anim = model.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) return null;   // generic rig: caller keeps the capsule

        var clone = Object.Instantiate(model.gameObject);
        clone.name = "Corpse";
        clone.transform.SetPositionAndRotation(model.position, model.rotation);
        clone.transform.localScale = model.lossyScale;
        clone.SetActive(true);                            // the owner's own model is hidden

        // The clone copied the animator mid-pose. Left running it would fight every joint, so
        // the pose is frozen as-is and physics takes the body from there.
        var cloneAnim = clone.GetComponent<Animator>();
        if (cloneAnim != null) cloneAnim.enabled = false;

        // Bones are resolved through the SOURCE animator and matched onto the clone by name.
        // Asking the clone's own animator would mean trusting it to have bound its avatar in
        // the same frame it was activated — and for the player who actually died, the model it
        // was cloned from was inactive. The source has been animating all match; it knows.
        var map = MapBones(anim, clone.transform);
        if (map.Count == 0) { Object.Destroy(clone); return null; }

        // Hitboxes came along in the clone on remote players. They are the wrong thing to leave
        // lying around answering raycasts, and their colliders would bind to the rigidbodies
        // added below. Destroy is deferred to the end of the frame, so they are switched off
        // now and cleaned up after — a frame of doubled colliders is a frame of wrong physics.
        foreach (var hb in clone.GetComponentsInChildren<Hitbox>(true))
        {
            var stale = hb.GetComponent<Collider>();
            if (stale != null) stale.enabled = false;
            Object.Destroy(hb.gameObject);
        }

        // The live body is hidden before this is called, and Instantiate copied that state.
        // A corpse nobody can see is not a corpse, so the clone gets its renderers back.
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = IgnoreRaycastLayer;

        var parts = Layout(rig);
        var bodies = new System.Collections.Generic.Dictionary<HumanBodyBones, Rigidbody>();

        // Two passes. Every rigidbody has to exist before any joint can point at one, and a
        // CharacterJoint whose connectedBody is still null pins itself to the world instead —
        // a corpse nailed to the air where it died.
        foreach (var p in parts)
        {
            Transform t;
            if (!map.TryGetValue(p.bone, out t) || t == null) continue;
            AddCollider(t, p, map);
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = p.mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            bodies[p.bone] = rb;
        }

        foreach (var p in parts)
        {
            Rigidbody self, parent;
            if (!bodies.TryGetValue(p.bone, out self)) continue;
            if (p.bone == HumanBodyBones.Hips) continue;              // the root hangs off nothing
            if (!bodies.TryGetValue(p.parent, out parent)) continue;

            var j = self.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.enablePreprocessing = false;   // preprocessing lets a stretched joint explode
            j.swingAxis = Vector3.up;
            j.lowTwistLimit = new SoftJointLimit { limit = -20f };
            j.highTwistLimit = new SoftJointLimit { limit = 20f };
            j.swing1Limit = new SoftJointLimit { limit = 40f };
            j.swing2Limit = new SoftJointLimit { limit = 25f };
        }

        Shove(bodies, clone.transform.position, awayFrom, impulse, spin);
        Object.Destroy(clone, lifetime);
        return clone;
    }

    // The shove goes on every part, not just the hips: pushing one bone of a jointed chain
    // drags the rest along behind it, which reads as the body being yanked rather than struck.
    static void Shove(System.Collections.Generic.Dictionary<HumanBodyBones, Rigidbody> bodies,
                      Vector3 at, Vector3? awayFrom, float impulse, float spin)
    {
        Vector3 dir;
        if (awayFrom.HasValue)
        {
            dir = at - awayFrom.Value;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Random.insideUnitSphere.normalized;
        }
        else
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            dir = new Vector3(r.x, 0f, r.y);
        }

        foreach (var kv in bodies)
        {
            kv.Value.AddForce(dir * impulse, ForceMode.VelocityChange);
            // Perpendicular to the fall, so the body topples the way it was pushed instead of
            // spinning on the spot like a top.
            kv.Value.AddTorque(Vector3.Cross(Vector3.up, dir) * spin, ForceMode.VelocityChange);
        }
    }

    // Same shapes PlayerBody.BuildHitboxes lays down, built the same way and for the same
    // reasons: an oriented CHILD of the bone rather than a collider on the bone itself, because
    // nothing guarantees a rig's bones point down any particular local axis, and a capsule
    // aimed by its own transform is correct on every rig. A child with no Rigidbody of its own
    // belongs to the nearest one above it, which is the bone's.
    static void AddCollider(Transform t, Part p,
                            System.Collections.Generic.Dictionary<HumanBodyBones, Transform> map)
    {
        if (p.sphere)
        {
            var head = new GameObject("RD_" + p.bone);
            head.layer = IgnoreRaycastLayer;
            head.transform.SetParent(t, false);
            var s = head.AddComponent<SphereCollider>();
            s.radius = p.radius;
            s.center = new Vector3(0f, p.radius * 0.85f, 0f);
            return;
        }

        Transform endT;
        if (!map.TryGetValue(p.end, out endT) || endT == null) return;

        // Local, for the same reason PlayerBody.Bone measures locally: the child inherits the
        // bone's scale and height is read in local units.
        float len = t.InverseTransformPoint(endT.position).magnitude;
        if (len < 0.01f) return;

        var go = new GameObject("RD_" + p.bone);
        go.layer = IgnoreRaycastLayer;
        go.transform.SetParent(t, false);
        go.transform.rotation = Quaternion.LookRotation(endT.position - t.position, t.up);
        var c = go.AddComponent<CapsuleCollider>();
        c.direction = 2;                               // Z, the axis just aimed down the bone
        c.radius = p.radius;
        c.height = Mathf.Max(len + p.radius, p.radius * 2f);
        c.center = new Vector3(0f, 0f, len * 0.5f);
    }

    // Rig bone names are unique within a skeleton, which is what makes the hop from the live
    // body to its clone safe.
    static System.Collections.Generic.Dictionary<HumanBodyBones, Transform> MapBones(
        Animator source, Transform cloneRoot)
    {
        var byName = new System.Collections.Generic.Dictionary<string, Transform>();
        foreach (var t in cloneRoot.GetComponentsInChildren<Transform>(true))
            byName[t.name] = t;

        var d = new System.Collections.Generic.Dictionary<HumanBodyBones, Transform>();
        foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (b == HumanBodyBones.LastBone) continue;
            var src = source.GetBoneTransform(b);
            if (src == null) continue;
            Transform t;
            if (byName.TryGetValue(src.name, out t)) d[b] = t;
        }
        return d;
    }

    static Part[] Layout(PlayerBody rig)
    {
        return new[]
        {
            new Part { bone = HumanBodyBones.Hips,  end = HumanBodyBones.Spine, parent = HumanBodyBones.Hips,
                       mass = HipsMass,  radius = rig.torsoRadius * 0.95f },
            new Part { bone = HumanBodyBones.Spine, end = HumanBodyBones.Neck,  parent = HumanBodyBones.Hips,
                       mass = SpineMass, radius = rig.torsoRadius },
            new Part { bone = HumanBodyBones.Head,  parent = HumanBodyBones.Spine,
                       mass = HeadMass,  radius = rig.headRadius, sphere = true },

            new Part { bone = HumanBodyBones.LeftUpperArm,  end = HumanBodyBones.LeftLowerArm, parent = HumanBodyBones.Spine,
                       mass = UpperArmMass, radius = rig.armRadius },
            new Part { bone = HumanBodyBones.LeftLowerArm,  end = HumanBodyBones.LeftHand,     parent = HumanBodyBones.LeftUpperArm,
                       mass = LowerArmMass, radius = rig.armRadius * 0.9f },
            new Part { bone = HumanBodyBones.RightUpperArm, end = HumanBodyBones.RightLowerArm, parent = HumanBodyBones.Spine,
                       mass = UpperArmMass, radius = rig.armRadius },
            new Part { bone = HumanBodyBones.RightLowerArm, end = HumanBodyBones.RightHand,     parent = HumanBodyBones.RightUpperArm,
                       mass = LowerArmMass, radius = rig.armRadius * 0.9f },

            new Part { bone = HumanBodyBones.LeftUpperLeg,  end = HumanBodyBones.LeftLowerLeg, parent = HumanBodyBones.Hips,
                       mass = UpperLegMass, radius = rig.legRadius },
            new Part { bone = HumanBodyBones.LeftLowerLeg,  end = HumanBodyBones.LeftFoot,     parent = HumanBodyBones.LeftUpperLeg,
                       mass = LowerLegMass, radius = rig.legRadius * 0.85f },
            new Part { bone = HumanBodyBones.RightUpperLeg, end = HumanBodyBones.RightLowerLeg, parent = HumanBodyBones.Hips,
                       mass = UpperLegMass, radius = rig.legRadius },
            new Part { bone = HumanBodyBones.RightLowerLeg, end = HumanBodyBones.RightFoot,     parent = HumanBodyBones.RightUpperLeg,
                       mass = LowerLegMass, radius = rig.legRadius * 0.85f },
        };
    }
}
