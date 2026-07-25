// Map picked on the connect screen. Host-only in effect: the server loads it as a FishNet
// GLOBAL scene, which every client then loads as part of joining, so a client cannot end up
// in a different map from the one it is playing in.
public static class MapChoice
{
    public static readonly string[] Names = { "Arena", "Stacks", "Expanse" };

    // Index into Names. Arena default — it is the entry scene and the more forgiving space.
    public static int Index = 0;

    public static string Selected =>
        (Index >= 0 && Index < Names.Length) ? Names[Index] : Names[0];

    public static string Describe(int i)
    {
        switch (i)
        {
            case 0: return "Arena - 90x90, open, long sightlines and a lethal central pit. Favours range.";
            case 1: return "Stacks - 56x56, vertical, solid centre pillar. Short fights, no cross-map shots.";
            case 2: return "Expanse - 150x150, four plateaus and a raised centre. Built for Flashpoint: long races, pads everywhere.";
            default: return "";
        }
    }
}
