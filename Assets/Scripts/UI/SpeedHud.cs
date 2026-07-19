using UnityEngine;

// Minimal on-screen readout for tuning movement feel. Replace with real UI later.
public class SpeedHud : MonoBehaviour
{
    public PlayerMotor motor;
    public PlayerHealth health;
    public WeaponController weapon;
    public MomentumDamage momentum;
    GUIStyle style;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (motor == null) motor = FindAnyObjectByType<PlayerMotor>();
        if (health == null && motor != null) health = motor.GetComponent<PlayerHealth>();
        if (health == null) health = FindAnyObjectByType<PlayerHealth>();
        if (weapon == null && motor != null) weapon = motor.GetComponent<WeaponController>();
        if (weapon == null) weapon = FindAnyObjectByType<WeaponController>();
        if (momentum == null && motor != null) momentum = motor.GetComponent<MomentumDamage>();
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
        // Momentum multiplier is appended rather than given its own row, so the fixed
        // Y offsets below don't all have to shift. Hidden at 1.00x (passive off / too slow).
        string dmg = momentum != null && momentum.Scale > 1.001f
            ? $"    DMG x{momentum.Scale:0.00}" : "";
        GUI.Label(new Rect(14, 100, 600, 28), $"flow  x{motor.flow:0.00}    [{stance}]{dmg}", style);

        // Health.
        if (health != null)
        {
            string hpText = health.Alive
                ? $"HP  {health.Hp:0}{(health.Invulnerable ? "  (invuln)" : "")}"
                : "DEAD — respawning";
            GUI.Label(new Rect(14, 132, 600, 28), hpText, style);
        }

        // Current weapon + ammo.
        if (weapon != null)
        {
            string ammo = weapon.Reloading ? "reloading..." : $"{weapon.CurrentAmmo}/{weapon.CurrentMag}";
            GUI.Label(new Rect(14, 162, 600, 28), $"WEAPON  {weapon.CurrentName}   {ammo}   [R] reload", style);
        }

        // Center crosshair dot (aim point for the grapple).
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
    }
}
