using System.Text;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// The name a player chose, synced to everyone.
//
// Until now every player was "Red" or "Blue" — fine for reading a scoreboard, useless for
// playing with people you know. Colours still carry the visual identity in the arena; the name
// carries the social one, so both are kept and the colour becomes the fallback rather than the
// only option.
//
// The name is set by the OWNER and written by the SERVER, never written directly by a client:
// a SyncVar a client could write is a SyncVar a client can write anything into, including
// somebody else's name.
public class PlayerIdentity : NetworkBehaviour
{
    public const int MaxNameLength = 16;

    readonly SyncVar<string> playerName = new SyncVar<string>(string.Empty);

    // Falls back to the colour name, so a player who never typed anything is still referable.
    public string Name => string.IsNullOrEmpty(playerName.Value)
        ? PlayerColors.NameFor(OwnerId)
        : playerName.Value;

    public bool HasCustomName => !string.IsNullOrEmpty(playerName.Value);
    public Color Tint => PlayerColors.For(OwnerId);

    string submitted;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;
        GameSettings.Load();
        Submit();
    }

    // Renaming from the pause menu should take effect without rejoining. Polled rather than
    // event-driven because the name is a plain static that several screens write — a change
    // notification would be a third thing to keep in sync, and this is one string compare.
    void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (GameSettings.PlayerName == submitted) return;
        Submit();
    }

    void Submit()
    {
        submitted = GameSettings.PlayerName;
        SubmitName(submitted ?? string.Empty);
    }

    [ServerRpc]
    void SubmitName(string requested) => playerName.Value = Sanitise(requested);

    // Names arrive from a client, so they are untrusted input and get treated like it.
    //
    // Length is capped because a 400-character name is a nameplate that covers the screen.
    // Angle brackets go because IMGUI styles can have rich text enabled, and a name containing
    // markup would otherwise let one player restyle — or hide — text on everyone else's HUD.
    // Control characters go because a newline in a name breaks every single-line label it is
    // drawn into.
    public static string Sanitise(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(MaxNameLength);
        foreach (char c in raw)
        {
            if (sb.Length >= MaxNameLength) break;
            if (c == '<' || c == '>') continue;
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }
}
