using UnityEngine;
using UnityEngine.InputSystem;

// Polls the new Input System directly (project Active Input Handling = Input System).
// Exposes raw intent; PlayerMotor/MouseLook read these each tick/frame.
public class InputReader : MonoBehaviour
{
    public Vector2 Move { get; private set; }      // x = strafe, y = forward (-1..1)
    public bool JumpHeld { get; private set; }
    public Vector2 LookDelta { get; private set; } // mouse pixels this frame
    public Vector2 Scroll { get; private set; }    // wheel delta (reel rope)
    public bool FireHeld { get; private set; }     // reserved (weapons, P4)
    public bool GrappleHeld { get; private set; }  // right mouse = grapple
    public bool CrouchHeld { get; private set; }   // ctrl / C = crouch + slide

    bool jumpBuffered;
    bool dashBuffered;

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (kb == null)
        {
            Move = Vector2.zero;
            JumpHeld = false;
            CrouchHeld = false;
            LookDelta = Vector2.zero;
            return;
        }

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Move = new Vector2(x, y);

        JumpHeld = kb.spaceKey.isPressed;
        if (kb.spaceKey.wasPressedThisFrame) jumpBuffered = true;
        if (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame)
            dashBuffered = true;

        FireHeld = mouse != null && mouse.leftButton.isPressed;
        GrappleHeld = mouse != null && mouse.rightButton.isPressed;
        CrouchHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed || kb.cKey.isPressed;
        LookDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        Scroll = mouse != null ? mouse.scroll.ReadValue() : Vector2.zero;
        // Esc is owned by GameMenu (pause / cursor). Nothing to do here.
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
    };
}
