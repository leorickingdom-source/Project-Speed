using UnityEngine;

// One tick of movement intent — the sole input the deterministic PlayerMotor step
// consumes. Local play builds it from InputReader.Sample(); networked play would build
// it on the owning client and replay it for prediction / reconciliation. Keep it small
// and blittable so it can be serialized over the wire later.
public struct InputCmd
{
    public Vector2 move;     // x = strafe, y = forward (-1..1)
    public bool jumpHeld;    // jump button held (auto-bhop path)
    public bool jumpPressed; // jump pressed since last tick (buffered single jump)
    public bool crouch;      // crouch held (crouch / slide)
    public bool grapple;     // grapple button held
    public bool dashPressed; // dash pressed since last tick (buffered, like jumpPressed)

    public static readonly InputCmd None = default;
}
