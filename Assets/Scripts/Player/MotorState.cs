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
    public int airJumpsUsed;      // DoubleJump budget spent this airtime
    public float airTime;         // seconds continuously airborne — gates the air-only verbs
    public int wallJumpsUsed;     // WallJump budget spent this airtime
    public Vector3 lastWallNormal; // surface of the last wall kick, so the same wall cannot be re-used

    // Jump pads are detected INSIDE the sim (PlayerMotor.CheckJumpPad) rather than by the pad
    // pushing on the player from its own FixedUpdate, so the launch is predicted and replayed
    // like every other verb — which means its refire window is sim state too.
    public float padCooldownLeft;

    // Grapple lives on GrappleHook but shapes velocity every tick through ApplyTo, so it has
    // to reconcile with the rest or a corrected client would replay with the rope in the
    // wrong state — reeling when the server says it let go, or vice versa.
    public bool grappleAttached;
    public Vector3 grappleAnchor;
    public bool grappleHeld;      // edge-detect state; without it a replay can re-fire a press
    public float grappleTimeLeft; // seconds of hook remaining; counts down every attached tick
    // Rope length. The constraint is built from it, so a client replaying without it would be
    // held to a different sphere than the server and drift through every tick of a swing.
    public float grappleRopeLength;
    public float grappleCooldownLeft; // seconds until the rope may fire again
}
