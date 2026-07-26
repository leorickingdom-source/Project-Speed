using UnityEngine;
using UnityEngine.InputSystem;

// Polls the new Input System directly (project Active Input Handling = Input System).
// Exposes raw intent; PlayerMotor/MouseLook read these each tick/frame.
//
// WHICH key does what is not decided here — Keybinds owns that, so a player can remap and so
// the controls card cannot drift from what this actually reads. Only look and scroll are still
// read from the device directly: they are axes, not buttons, and there is nothing to remap.
public class InputReader : MonoBehaviour
{
    public Vector2 Move { get; private set; }      // x = strafe, y = forward (-1..1)
    public bool JumpHeld { get; private set; }
    public Vector2 LookDelta { get; private set; } // mouse pixels this frame
    public Vector2 Scroll { get; private set; }    // wheel delta (reel rope)
    public bool FireHeld { get; private set; }     // reserved (weapons, P4)
    public bool GrappleHeld { get; private set; }  // grapple button held
    public bool CrouchHeld { get; private set; }   // ctrl / C = crouch + slide

    [Tooltip("Look source, baked into the command so the sim has no hidden facing input.")]
    public MouseLook look;

    [Tooltip("Weapon source, used ONLY to tell a swing apart from a shot. The sim never sees " +
             "the weapon itself — it sees the one bit that movement cares about.")]
    public WeaponController weapon;

    bool jumpBuffered;
    bool dashBuffered;
    bool meleeBuffered;

    void Awake()
    {
        if (look == null) look = GetComponent<MouseLook>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
        Keybinds.Load(); // idempotent; this component can wake before any menu does
    }

    void Update()
    {
        // While the rebinder is capturing, every press is a candidate binding. Letting it also
        // reach the sim means the click that binds fire also fires, and the key that binds
        // forward also buffers a jump for whenever the panel closes.
        // The connect screen and the rebinder both take the keyboard away from the game: a
        // press there is a menu interaction, not a movement command.
        if (KeybindsUI.Open || ConnectUI.MenuOpen)
        {
            Move = Vector2.zero;
            JumpHeld = CrouchHeld = FireHeld = GrappleHeld = false;
            LookDelta = Vector2.zero;
            Scroll = Vector2.zero;
            jumpBuffered = dashBuffered = meleeBuffered = false;
            return;
        }

        float x = (Keybinds.Held(GameAction.MoveRight) ? 1f : 0f)
                  - (Keybinds.Held(GameAction.MoveLeft) ? 1f : 0f);
        float y = (Keybinds.Held(GameAction.MoveForward) ? 1f : 0f)
                  - (Keybinds.Held(GameAction.MoveBack) ? 1f : 0f);
        Move = new Vector2(x, y);

        JumpHeld = Keybinds.Held(GameAction.Jump);
        if (Keybinds.Pressed(GameAction.Jump)) jumpBuffered = true;
        if (Keybinds.Pressed(GameAction.Dash)) dashBuffered = true;

        FireHeld = Keybinds.Held(GameAction.Fire);
        // Buffered like jump, and for the same reason: a tap between two fixed ticks would
        // otherwise be dropped and the swing would keep its rope by luck of the frame timing.
        if (Keybinds.Pressed(GameAction.Fire) && weapon != null && weapon.CurrentIsMelee)
            meleeBuffered = true;
        GrappleHeld = Keybinds.Held(GameAction.Grapple);
        CrouchHeld = Keybinds.Held(GameAction.Crouch);

        var mouse = Mouse.current;
        LookDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        Scroll = mouse != null ? mouse.scroll.ReadValue() : Vector2.zero;
        // Pause is owned by GameMenu (cursor + timescale). Nothing to do here.
    }

    // Consumed by FixedUpdate so a single tap isn't dropped between frames.
    public bool ConsumeJump()
    {
        if (jumpBuffered) { jumpBuffered = false; return true; }
        return false;
    }

    public bool ConsumeDash()
    {
        if (dashBuffered) { dashBuffered = false; return true; }
        return false;
    }

    public bool ConsumeMelee()
    {
        if (meleeBuffered) { meleeBuffered = false; return true; }
        return false;
    }

    // Build one tick of movement intent for the motor (consumes the jump buffer).
    // This is the single seam between raw devices and the deterministic sim.
    public InputCmd Sample() => new InputCmd
    {
        move = Move,
        jumpHeld = JumpHeld,
        jumpPressed = ConsumeJump(),
        crouch = CrouchHeld,
        grapple = GrappleHeld,
        dashPressed = ConsumeDash(),
        meleePressed = ConsumeMelee(),
        yaw = look != null ? look.Yaw : 0f,
        pitch = look != null ? look.Pitch : 0f,
    };
}
