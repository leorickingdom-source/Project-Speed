// The passive picked on the connect screen. Unlike LoadoutChoice this is only the LOCAL
// intent — PassiveLoadout sends it to the server on spawn and the server owns it from then
// on, because passives change the simulation and a local-only value would desync.
public static class PassiveChoice
{
    public static PassiveType Selected = PassiveType.None;

    // Order shown on the connect screen.
    //
    // Dash SHELVED (not deleted) after playtest: with the double jump's redirect+surge the
    // two picks converged on the same job — an air-direction burst — and dash was the one
    // with a phantom-trigger history (ramps) and a cooldown to babysit. All of its motor
    // code (TryDash, dashSpeed/dashGain/dashCooldown, the Shift bind) is intact; restoring
    // it is re-adding the entry below.
    public static readonly PassiveType[] Options =
    {
        PassiveType.None,
        PassiveType.Vitality,
        // Momentum is BASELINE now — everyone's damage scales with speed, so it is not a
        // choice. It was the pillar of the game sitting in a slot people traded away for HP.
        PassiveType.Featherweight,
        // PassiveType.Dash,
        PassiveType.DoubleJump,
        // WallJump is BASELINE now — every player wall jumps, so it is not a choice.
        PassiveType.Highground,
        PassiveType.Camper,
        PassiveType.Bloodrush,
        PassiveType.Hookweaver,
        PassiveType.KineticPlating,
        PassiveType.Slipstream,
    };

    // One line each — enough to pick without reading code.
    public static string Describe(PassiveType t)
    {
        switch (t)
        {
            case PassiveType.None: return "No passive.";
            case PassiveType.Vitality: return "+40 max HP (190). Survive a sniper body shot with room to spare, and a fast revolver still needs 3.";
            case PassiveType.Momentum: return "Baseline for everyone — not a pick. Damage rises with speed, up to +25% at 16 m/s.";
            case PassiveType.Featherweight: return "20% narrower hitbox. Harder to hit; changes no damage numbers.";
            case PassiveType.Dash: return "Shift, or Space in mid-air. Burst to 18 m/s, 1.5s cooldown.";
            case PassiveType.DoubleJump: return "One extra jump in mid-air, refunded when you land. Reach and recovery.";
            case PassiveType.WallJump: return "Baseline for everyone — not a pick.";
            case PassiveType.Highground: return "Damage rises with altitude, up to +40% at 10m. Decks and pad apexes.";
            case PassiveType.Camper: return "2x damage while nearly still, gone above 5 m/s. Yes, really.";
            case PassiveType.Bloodrush: return "Kills restore 30 HP. The only sustain in the game that is not a pickup — finish fights and you keep going.";
            case PassiveType.Hookweaver: return "Hooks last 2.6s instead of 1.8, and re-hook twice as fast. Chain swings across gaps that otherwise need the floor.";
            case PassiveType.KineticPlating: return "Your own blasts hurt you half as much; the launch is unchanged. Rocket-jump twice as often for the same health.";
            case PassiveType.Slipstream: return "Air ceiling 18.5 m/s instead of 16.2. Pure travel speed: reach the fight, the pickup or the objective first. No damage bonus.";
            default: return "";
        }
    }
}
