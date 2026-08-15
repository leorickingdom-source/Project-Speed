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
    // Index into Names / SlotIndices below — NOT directly into WeaponController.weapons.
    // 0 = Revolver, the default starter now that the Rifle is shelved.
    public static int WeaponIndex = 0;

    // Rifle and SMG SHELVED 2026-07-26: the semi-autos are the picks that feel rewarding, and
    // the two full-autos were the ones that could win a fight without ever committing to a
    // shot. Both weapons stay in WeaponController.weapons at their original slots (1 and 3) —
    // this is a MENU change only, so nothing that stores a weapons[] index moves, the pickup
    // and objective slots keep their numbers, and restoring either is putting its name and
    // slot back into these two arrays.
    // Knife SHELVED 2026-08-06: melee became a universal action on its own key (see
    // WeaponController.TryQuickMelee) rather than a loadout that traded away your gun. Shelved
    // the same way the Rifle and SMG were -- the weapon keeps its slot in weapons[], so the
    // oddball swap, the viewmodel and every stored index still mean what they meant.
    public static readonly string[] Names = { "Revolver", "Sniper", "Shotgun", "Rocket" };

    // Where each choice lives in WeaponController.weapons. Mapped rather than assumed, so a
    // shelved entry — or another appended weapon — cannot silently shift what a menu position
    // means.
    static readonly int[] SlotIndices =
    {
        0,                                  // Revolver
        // 1,                               // Rifle    — shelved
        2,                                  // Sniper
        // 3,                               // SMG      — shelved
        4,                                  // Shotgun
        // WeaponController.KnifeIndex,     — shelved, melee is universal now
        WeaponController.RocketIndex,
    };

    // The actual weapons[] index for the current pick. Falls back to the Revolver, which is
    // the one slot guaranteed to be on the menu.
    public static int SelectedSlot =>
        (WeaponIndex >= 0 && WeaponIndex < SlotIndices.Length) ? SlotIndices[WeaponIndex] : 0;

    // Range identity, so the pick is informed. Damage numbers are close across the set —
    // what actually separates them is WHERE that damage lands.
    //
    // Switches on the weapons[] SLOT, not the menu position: menu positions move every time
    // something is shelved, and a description silently attached to the wrong gun is the kind
    // of bug nobody reports because it only lies on the connect screen. Shelved entries keep
    // their lines, ready for the day they come back.
    public static string Describe(int menuIndex)
    {
        int slot = (menuIndex >= 0 && menuIndex < SlotIndices.Length) ? SlotIndices[menuIndex] : -1;
        switch (slot)
        {
            case 0: return "Revolver - 6 rounds, 3-shot kill to 40m, tapers past that. Miss and you feel it.";
            case 1: return "Rifle - all-rounder. Full damage to 45m, tapers to 70% at 90m.";
            case 2: return "Sniper - only 40% damage under 10m, full past 25m. Deadly at range, helpless if rushed.";
            case 3: return "SMG - close-mid pressure. Full to 20m, down to 40% by 45m.";
            case 4: return "Shotgun - brutal inside 8m, nearly harmless by 20m. You must close.";
            case WeaponController.KnifeIndex:
                return "Knife - ONE HIT KILLS, reach 3.5m, no gun at all. Pure movement build: nothing at range, unanswerable up close.";  // shelved
            case WeaponController.RocketIndex:
                return "Rocket - 4 tubes, 2.6s reload, 5m splash, +40 for a DIRECT hit. Shoot your own feet to launch: the fastest way to build speed, paid for in health.";
            default: return "";
        }
    }

    public static string CurrentName =>
        (WeaponIndex >= 0 && WeaponIndex < Names.Length) ? Names[WeaponIndex] : "?";
}
