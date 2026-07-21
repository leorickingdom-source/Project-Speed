// The weapon this player picked on the connect screen. Static and local: it is read once
// when your player spawns and then LOCKED for the match — no mid-match swapping, so the
// choice stays a commitment rather than a counter-pick.
//
// Local by design. Hit detection is already client-authoritative (the shooter reports its
// own hit and damage), so the server does not currently need to know which weapon you hold.
// If hit validation ever moves server-side, this has to become a synced value the server
// can check against — otherwise a client could claim any weapon's damage.
public static class LoadoutChoice
{
    // Index into WeaponController.weapons. 1 = Rifle, the default starter.
    public static int WeaponIndex = 1;

    public static readonly string[] Names = { "Revolver", "Rifle", "Sniper", "SMG", "Shotgun" };

    // Range identity, so the pick is informed. Damage numbers are close across the set —
    // what actually separates them is WHERE that damage lands.
    public static string Describe(int i)
    {
        switch (i)
        {
            case 0: return "Revolver - 6 rounds, 3-shot kill to 40m, tapers past that. Miss and you feel it.";
            case 1: return "Rifle - all-rounder. Full damage to 45m, tapers to 70% at 90m.";
            case 2: return "Sniper - only 40% damage under 10m, full past 25m. Deadly at range, helpless if rushed.";
            case 3: return "SMG - close-mid pressure. Full to 20m, down to 40% by 45m.";
            case 4: return "Shotgun - brutal inside 8m, nearly harmless by 20m. You must close.";
            default: return "";
        }
    }

    public static string CurrentName =>
        (WeaponIndex >= 0 && WeaponIndex < Names.Length) ? Names[WeaponIndex] : "?";
}
