// Named power-up kinds. Only Grapple is wired this pass; the rest are reserved
// names so a new pickup is "add a case", not "add a type".
public enum PowerupType
{
    Grapple,
    Haste,
    LowGrav,
    Quad,
    RapidFire,
}

// Snapshot of one active power-up (type + seconds left). Struct so the HUD can
// read the active set with zero per-frame allocation.
public readonly struct PowerupSlot
{
    public readonly PowerupType Type;
    public readonly float Remaining;
    public PowerupSlot(PowerupType type, float remaining)
    {
        Type = type;
        Remaining = remaining;
    }
}
