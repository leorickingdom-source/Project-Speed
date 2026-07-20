// Game mode options set on the connect screen. Only the HOST's values matter — MatchManager
// reads these in OnStartServer and syncs the result, so a client toggling them locally has no
// effect on the match it joins.
public static class GameModeChoice
{
    // Health pickups on the map. Off = pure deathmatch, damage is permanent until you die.
    public static bool Pickups = false;
}
