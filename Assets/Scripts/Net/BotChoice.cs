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

    // Scales bot damage and rate of fire. A TESTING knob first and a difficulty setting second.
    //
    // At full strength three bots put out roughly 36 damage a second of contact gnawing plus
    // perfectly-aimed projectiles every 1.6s with no reaction time — about four seconds to kill
    // a full-health player. That is fine as an opponent and useless as a sparring partner: you
    // cannot test a weapon's feel or a movement route while being deleted by the scenery.
    //
    // Applied as a runtime multiplier rather than by editing each bot's serialized fields,
    // because the bots in the scenes carry their own values and changing the script defaults
    // would not touch them.
    public const float Practice = 0.35f;   // sparring partner: hits, does not threaten
    public const float Normal = 0.7f;
    public const float Full = 1f;

    public static float Difficulty = Practice;

    public static string Describe(int n) => n <= 0 ? "Bots: off" : $"Bots: {n}";

    public static string DescribeDifficulty(float d)
    {
        if (d <= Practice + 0.01f) return "Practice";
        if (d <= Normal + 0.01f) return "Normal";
        return "Full";
    }

    // Cycles Practice -> Normal -> Full -> Practice.
    public static float NextDifficulty(float d)
    {
        if (d <= Practice + 0.01f) return Normal;
        if (d <= Normal + 0.01f) return Full;
        return Practice;
    }
}
