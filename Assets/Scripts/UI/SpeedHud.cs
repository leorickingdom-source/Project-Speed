using System.Collections.Generic;
using UnityEngine;

// Minimal on-screen readout for tuning movement feel. Replace with real UI later.
public class SpeedHud : MonoBehaviour
{
    public PlayerMotor motor;
    public PowerupReceiver receiver;
    GUIStyle style;
    readonly List<PowerupSlot> active = new();

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (motor == null) motor = FindAnyObjectByType<PlayerMotor>();
        if (receiver == null && motor != null) receiver = motor.GetComponent<PowerupReceiver>();
        if (receiver == null) receiver = FindAnyObjectByType<PowerupReceiver>();
    }

    void OnGUI()
    {
        if (motor == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(14, 10, 600, 28), $"SPEED  {motor.Speed:0.0} m/s", style);
        GUI.Label(new Rect(14, 40, 600, 28), $"grounded  {motor.grounded}", style);
        GUI.Label(new Rect(14, 70, 600, 28),
            $"vel  x{motor.velocity.x:0.0}  y{motor.velocity.y:0.0}  z{motor.velocity.z:0.0}", style);
        string stance = motor.sliding ? "SLIDE" : motor.crouching ? "crouch" : "stand";
        GUI.Label(new Rect(14, 100, 600, 28), $"flow  x{motor.flow:0.00}    [{stance}]", style);

        // Active power-ups + countdowns.
        if (receiver != null)
        {
            receiver.GetActive(active);
            float y = 132f;
            foreach (var s in active)
            {
                GUI.Label(new Rect(14, y, 600, 28), $"{s.Type}  {s.Remaining:0.0}s", style);
                y += 30f;
            }
        }

        // Center crosshair dot (aim point for the grapple).
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
    }
}
