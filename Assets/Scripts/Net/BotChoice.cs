// How many bots the host wants in the match. Set on the connect screen.
//
// Only the HOST's value matters — MatchManager reads it in OnStartServer and syncs the result,
// exactly like GameModeChoice.Pickups, so a client turning bots up locally cannot add enemies
// to a match it is joining.
//
// The bots themselves are permanent scene objects; this decides how many of them are in play.
// See SimpleBot.botIndex — slot 0 is the first bot enabled, so 1 means Bot1 only.
public static class BotChoice
{
    // Arena and Stacks each ship three bot slots.
    public const int Max = 3;

    // Default 0: a bot wandering into a first multiplayer match nobody asked for reads as a
    // bug. Single-player practice is where they earn their place, so it is an explicit choice.
    public static int Count = 0;

    public static string Describe(int n) => n <= 0 ? "Bots: off" : $"Bots: {n}";
}
