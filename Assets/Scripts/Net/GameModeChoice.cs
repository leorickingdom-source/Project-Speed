// Game mode options set on the connect screen. Only the HOST's values matter — MatchManager
// reads these in OnStartServer and syncs the result, so a client toggling them locally has no
// effect on the match it joins.
public static class GameModeChoice
{
    // 0 = pure deathmatch, 1 = deathmatch + pickups, 2 = oddball.
    // An index rather than the old Pickups bool because the mode list stopped being binary.
    // Oddball implies pickups: contesting a carrier is a war of attrition, and attrition
    // without health on the map is just "whoever shot first, eventually".
    public const int PureDeathmatch = 0;
    public const int PickupsDeathmatch = 1;
    public const int Oddball = 2;
    public const int Flashpoint = 3;
    public const int CaptureTheFlag = 4;
    public const int Count = 5;

    public static int ModeIndex = PureDeathmatch;

    // Kept for the pickup objects' benefit: they only care whether map resources exist.
    public static bool Pickups => ModeIndex != PureDeathmatch;

    public static string Describe(int i)
    {
        switch (i)
        {
            case PureDeathmatch: return "Mode: Pure Deathmatch";
            case PickupsDeathmatch: return "Mode: Health + Armour";
            case Oddball: return "Mode: Oddball";
            case Flashpoint: return "Mode: Flashpoint";
            case CaptureTheFlag: return "Mode: Capture the Flag";
            default: return "Mode: ?";
        }
    }
}
