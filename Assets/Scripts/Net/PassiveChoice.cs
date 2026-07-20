// The passive picked on the connect screen. Unlike LoadoutChoice this is only the LOCAL
// intent — PassiveLoadout sends it to the server on spawn and the server owns it from then
// on, because passives change the simulation and a local-only value would desync.
public static class PassiveChoice
{
    public static PassiveType Selected = PassiveType.None;

    // Order shown on the connect screen.
    public static readonly PassiveType[] Options =
    {
        PassiveType.None,
        PassiveType.Vitality,
        PassiveType.Momentum,
        PassiveType.Featherweight,
        PassiveType.Dash,
        PassiveType.Highground,
        PassiveType.Camper,
    };

    // One line each — enough to pick without reading code.
    public static string Describe(PassiveType t)
    {
        switch (t)
        {
            case PassiveType.None: return "No passive.";
            case PassiveType.Vitality: return "+40 max HP (190). Survive a sniper body shot with room to spare.";
            case PassiveType.Momentum: return "Damage rises with speed, up to +40% at 16 m/s. Holds ~1s after you slow.";
            case PassiveType.Featherweight: return "20% narrower hitbox. Harder to hit; changes no damage numbers.";
            case PassiveType.Dash: return "Shift to burst-move. Brings you up to 18 m/s, 1.5s cooldown.";
            case PassiveType.Highground: return "Damage rises with altitude, up to +40% at 10m. Decks and pad apexes.";
            case PassiveType.Camper: return "2x damage while nearly still, gone above 5 m/s. Yes, really.";
            default: return "";
        }
    }
}
