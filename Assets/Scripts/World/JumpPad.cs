using UnityEngine;

// Launch pad — DATA ONLY. The pad does not push anybody: PlayerMotor.CheckJumpPad sweeps for
// pads from inside the deterministic Step() and reads these numbers off whatever it hits.
//
// That inversion is the whole fix. As a component with its own FixedUpdate this pad mutated
// velocity outside the sim, which meant the launch was never predicted — the server applied
// it on its tick, the client had not, and reconciliation snapped it back, so online a pad
// fired or did not at random. It also sampled a single overlap per tick (a fast player crosses
// half a metre in one) and built that overlap from `bounds.extents` — already world-axis-
// aligned — while also passing `transform.rotation`, so every rotated pad tested a volume that
// was not the pad.
//
// Put this on a flat box; its collider is the pad surface, and it stays solid so you can stand
// on it. Tilt the transform to aim the horizontal part of the launch.
[RequireComponent(typeof(Collider))]
public class JumpPad : MonoBehaviour
{
    [Tooltip("Vertical launch speed, m/s. Applied as a FLOOR (see PlayerMotor.PadBoost), so " +
             "arriving fast on the way down still leaves you going exactly this fast up.")]
    public float upForce = 20f;
    [Tooltip("Horizontal speed added along the pad's +Z. Tilt the pad to aim it.")]
    public float forwardForce = 0f;

    // The horizontal half of the launch, in world space.
    public Vector3 Launch => transform.forward * forwardForce;
}
