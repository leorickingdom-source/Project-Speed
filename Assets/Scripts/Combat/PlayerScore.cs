using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Deathmatch scoring. Kills and deaths are SyncVars the SERVER alone writes, so every
// client sees the same scoreboard and a client cannot inflate its own score.
//
// The scoreboard is drawn by the owner only, but it reads every PlayerScore in the scene —
// remote players' components still exist and still receive their synced values even though
// their input and camera are disabled.
public class PlayerScore : NetworkBehaviour
{
    readonly SyncVar<int> kills = new SyncVar<int>();
    readonly SyncVar<int> deaths = new SyncVar<int>();

    public int Kills => kills.Value;
    public int Deaths => deaths.Value;

    // The player's chosen name when they set one, otherwise the colour — which is still the
    // right fallback, because "Red 3/1" is legible mid-match in a way that "Player 0" is not.
    public string Label
    {
        get
        {
            if (identity == null) identity = GetComponent<PlayerIdentity>();
            return identity != null ? identity.Name : PlayerColors.NameFor(OwnerId);
        }
    }

    public Color Tint => PlayerColors.For(OwnerId);

    PlayerIdentity identity;
    MatchManager match;

    [Header("Scoreboard")]
    public bool showScoreboard = true;
    public int fontSize = 20;

    GUIStyle style, mine;

    // Server-only mutators. Called from PlayerNetwork.ReportHit (kill) and PlayerHealth
    // (death), both of which already run under server authority.
    public void AddKill()
    {
        if (IsServerStarted) kills.Value++;
    }

    public void AddDeath()
    {
        if (IsServerStarted) deaths.Value++;
    }

    // Called by MatchManager between rounds.
    public void ResetScore()
    {
        if (!IsServerStarted) return;
        kills.Value = 0;
        deaths.Value = 0;
    }

    void OnGUI()
    {
        // Only the local player draws it, or every player in the scene would stack a copy.
        if (!showScoreboard || !IsOwner || GameMenu.IsPaused || KeybindsUI.Open) return;

        // Stands down while the round is over: MatchManager puts a full final scoreboard on
        // screen, and two scoreboards saying the same thing in different corners is noise.
        if (match == null) match = FindAnyObjectByType<MatchManager>();
        if (match != null && match.MatchOver) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            mine = new GUIStyle(style) { fontStyle = FontStyle.Bold };
            mine.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }

        var all = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
        // Wider than it was: a 16-character name plus "12 / 9" no longer fits in 200px, and a
        // clipped name is worse than no name at all.
        const float w = 320f;
        float x = Screen.width - w - 12f, y = 150f;
        GUI.Label(new Rect(x, y, w, 26f), "SCORE   K / D", mine);
        y += 28f;

        // Highest kills first so the leader is always on top.
        System.Array.Sort(all, (a, b) => b.Kills.CompareTo(a.Kills));
        foreach (var p in all)
        {
            if (p == null) continue;
            // Row is drawn in that player's own colour, so the scoreboard and the arena agree.
            var row = new GUIStyle(p.IsOwner ? mine : style);
            row.normal.textColor = p.Tint;
            GUI.Label(new Rect(x, y, w, 25f),
                $"{(p.IsOwner ? "> " : "")}{p.Label}   {p.Kills} / {p.Deaths}", row);
            y += 25f;
        }
    }
}
