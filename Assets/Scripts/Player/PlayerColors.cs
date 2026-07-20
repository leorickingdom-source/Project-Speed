using UnityEngine;

// Per-player identity colours, derived from OwnerId. FishNet assigns and syncs OwnerId, so
// every client computes the same colour for the same player without any extra networking.
//
// Chosen to stay separable against the arena: the map is deliberately cool and desaturated,
// so players are saturated and warm-vs-cool against each other. Red and blue first because
// they are the two most distinguishable hues for the largest number of people, including the
// most common forms of colour blindness — where red/green would not be.
public static class PlayerColors
{
    static readonly Color[] Palette =
    {
        new Color(0.95f, 0.25f, 0.25f), // red
        new Color(0.25f, 0.55f, 1.00f), // blue
        new Color(1.00f, 0.80f, 0.20f), // amber
        new Color(0.35f, 0.90f, 0.45f), // green
        new Color(0.85f, 0.40f, 0.95f), // violet
        new Color(0.30f, 0.90f, 0.90f), // cyan
    };

    public static Color For(int ownerId)
    {
        if (ownerId < 0) return Color.grey;              // unowned / server object
        return Palette[ownerId % Palette.Length];
    }

    public static string NameFor(int ownerId)
    {
        if (ownerId < 0) return "-";
        switch (ownerId % Palette.Length)
        {
            case 0: return "Red";
            case 1: return "Blue";
            case 2: return "Amber";
            case 3: return "Green";
            case 4: return "Violet";
            default: return "Cyan";
        }
    }
}
