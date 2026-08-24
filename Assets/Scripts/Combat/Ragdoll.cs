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
                                   float impulse, float spin, float lifetime, Vector3 inheritVelocity)
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
            // Loose limits — the point is flop. Tight ones (the old ±20/40/25) held the body
            // close to its death pose so the drag barely bent it, which read as stiff even with
            // the launch localised. Wider swings let the torso jackknife and the limbs trail.
            j.lowTwistLimit = new SoftJointLimit { limit = -40f };
            j.highTwistLimit = new SoftJointLimit { limit = 40f };
            j.swing1Limit = new SoftJointLimit { limit = 65f };
            j.swing2Limit = new SoftJointLimit { limit = 45f };
        }

        Shove(bodies, clone.transform.position, awayFrom, impulse, spin, inheritVelocity);
        Object.Destroy(clone, lifetime);
        return clone;
    }

    // How hard death reads scales with how the player died, and the biggest input is free:
    // the victim's own velocity. A sprinter's corpse should tumble forward, a rocketed one
    // should fly — and it already can, because Explosion.AddImpulse put the blast INTO that
    // velocity before death, so inheriting it captures momentum and explosive knockback in one
    // number, identically on every client with no networking. Hitscan adds no knockback, so
    // those deaths carry only the running speed, which is the honest answer.
    const float SpeedPunch = 0.04f;   // directional pop grows with speed (+100% at 25 m/s)
    const float SpinPunch  = 0.10f;   // and spin grows faster, so a fast death tumbles wild
    const float LiftPerSpeed = 0.12f; // a little loft off the floor, so speed throws it UP too
    const float LiftCap = 4f;
    const float BaseLift = 2.5f;      // upward pop on EVERY death, so a hit lifts off the ground
                                      // instead of sliding along it — reads as struck, not shoved

    // A ragdoll flops because ONE link is shoved and the joints drag the rest out of shape. The
    // launch therefore lands on the torso alone — not on every part. Shoving all eleven with the
    // same force (what this used to do) gives them identical motion, so no joint ever flexes and
    // the body flies rigid as a plank: the "stiff" look. Only the inherited velocity is uniform,
    // because a living body really was all moving together; everything after it is localised.
    static void Shove(System.Collections.Generic.Dictionary<HumanBodyBones, Rigidbody> bodies,
                      Vector3 at, Vector3? awayFrom, float impulse, float spin, Vector3 inheritVelocity)
    {
        // Away from the killer is the way the bullet was travelling, so the body is thrown along
        // the shot rather than in a direction of its own.
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

        float speed = new Vector2(inheritVelocity.x, inheritVelocity.z).magnitude;
        float pop  = impulse * (1f + speed * SpeedPunch);
        float whip = spin    * (1f + speed * SpinPunch);
        float lift = BaseLift + Mathf.Min(speed * LiftPerSpeed, LiftCap);
        Vector3 spinAxis = (Vector3.Cross(Vector3.up, dir) + Random.insideUnitSphere * 0.4f).normalized;

        // Living momentum, uniform and legitimate — the whole body was travelling together.
        foreach (var kv in bodies) kv.Value.linearVelocity = inheritVelocity;

        Rigidbody hips  = Get(bodies, HumanBodyBones.Hips);
        Rigidbody chest = Get(bodies, HumanBodyBones.Spine);
        Rigidbody head  = Get(bodies, HumanBodyBones.Head);

        // The whole-body launch goes on the hips — the root mass — so it carries everyone with
        // it through the joints instead of each limb being flung on its own.
        Rigidbody core = hips ?? chest;
        if (core != null)
        {
            core.AddForce(dir * pop + Vector3.up * lift, ForceMode.VelocityChange);
            core.AddTorque(spinAxis * whip, ForceMode.VelocityChange);
        }
        // Upper body LEADS: the chest and head get an extra shove so they whip out ahead of the
        // hips, which is the fold that makes a hit look like a hit.
        if (chest != null) chest.AddForce(dir * pop * 0.6f + Vector3.up * lift * 0.4f, ForceMode.VelocityChange);
        if (head  != null) head.AddForce(dir * pop * 0.5f, ForceMode.VelocityChange);

        // A small random spin per part so the limbs flail independently instead of trailing in a
        // frozen T-pose — this is the difference between a ragdoll and a mannequin on a wire.
        foreach (var kv in bodies)
            kv.Value.AddTorque(Random.insideUnitSphere * whip * 0.35f, ForceMode.VelocityChange);
    }

    static Rigidbody Get(System.Collections.Generic.Dictionary<HumanBodyBones, Rigidbody> bodies,
                         HumanBodyBones b)
    {
        Rigidbody rb;
        return bodies.TryGetValue(b, out rb) ? rb : null;
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
