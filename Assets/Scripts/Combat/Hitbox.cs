using UnityEngine;

// Marks a collider that is part of a player's ANIMATED hitbox rig, and says which body part it
// is. Attached to the colliders PlayerBody builds along the humanoid skeleton.
//
// The point of the marker is the head. Before this, a headshot was "the hit landed in the top
// 28% of the target's capsule" — a rule, not a place. It had to be a rule, because a capsule
// has no head to aim at. With a collider that IS the head, the question stops being geometric
// and becomes identity: you hit the head box or you did not.
//
// That also retires a class of bug this project kept running into. The band was a FRACTION of
// a capsule that changes height, so it moved when you crouched, and it did not move the same
// way the visible body did — the crouch clips stood 38cm outside it, the slide 68cm. A hitbox
// bolted to the skull cannot disagree with the skull.
public class Hitbox : MonoBehaviour
{
    public enum Part { Head, Torso, Pelvis, Arm, Leg }

    public Part part;

    [Tooltip("Damage multiplier for this part, applied on top of the weapon's own numbers. " +
             "Limbs read below 1 in most shooters; this ships at 1 so that swapping a capsule " +
             "for a skeleton changes WHERE you have to aim without also changing how much " +
             "every weapon does. Tune it as a deliberate balance pass, not as a side effect.")]
    public float damageScale = 1f;

    public bool IsHead => part == Part.Head;
}
