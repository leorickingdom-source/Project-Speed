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

    public static readonly string[] Names = { "Pistol", "Rifle", "Sniper", "SMG", "Shotgun" };

    public static string CurrentName =>
        (WeaponIndex >= 0 && WeaponIndex < Names.Length) ? Names[WeaponIndex] : "?";
}
