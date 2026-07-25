// How a hit landed. Travels from the shooter's client, through the server's damage report,
// into the kill feed — so the feed can say HOW someone died, not just that they did.
//
// An enum rather than a pile of bools (headshot? melee? next one?): the states are mutually
// exclusive, and a bool per kind would let a caller claim a melee headshot.
public enum KillKind
{
    Normal,
    Headshot,
    Melee,
}
