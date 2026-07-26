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
    // Fire pressed since last tick WHILE HOLDING A MELEE WEAPON. The sim has no idea what
    // you are carrying — weapons are not part of the movement state — so the reader resolves
    // "is this a swing?" and sends the answer. It rides in the command rather than being read
    // off WeaponController so the server replaying your input reaches the same conclusion.
    public bool meleePressed;
    public float yaw;        // horizontal facing, degrees — the sim's ONLY facing input
    public float pitch;      // vertical aim, degrees (reserved for networked shooting)

    public static readonly InputCmd None = default;
}
