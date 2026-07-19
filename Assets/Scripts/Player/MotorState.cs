using UnityEngine;

// A complete snapshot of PlayerMotor's mutable simulation state — everything Step() reads
// that persists across ticks. Reconciliation restores one of these (the server's
// authoritative value) and then replays buffered inputs, so it MUST hold every field that
// affects a future step; miss one and client and server drift.
//
// Plain struct on purpose: no networking dependency. When FishNet lands, its reconcile
// data either wraps this or adds the tick field alongside it — the sim state itself stays
// stack-agnostic.
//
// NOT included, deliberately: Radius / hasDash (resolved from the passive loadout, not
// per-tick state) and the head/camera local offset (cosmetic, recomputed from height every
// step). Grapple state (Attached/Anchor) lives on GrappleHook and reconciles separately —
// a follow-up, not part of the core motor snapshot.
[System.Serializable]
public struct MotorState
{
    public Vector3 position;      // transform.position — the motor is the sole mover
    public Vector3 velocity;
    public bool grounded;
    public Vector3 groundNormal;
    public bool crouching;
    public bool sliding;
    public float height;          // current (lerped) capsule height
    public float flow;            // accessible-momentum multiplier
    public float slideBoostCooldown;
    public float dashCooldownLeft;
    public float dashGraceLeft;
}
