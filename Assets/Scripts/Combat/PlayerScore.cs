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

    // Stable, readable label. ClientId is assigned by the server so it agrees everywhere.
    public string Label => $"Player {OwnerId}";

    [Header("Scoreboard")]
    public bool showScoreboard = true;
    public int fontSize = 16;

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

    void OnGUI()
    {
        // Only the local player draws it, or every player in the scene would stack a copy.
        if (!showScoreboard || !IsOwner) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            mine = new GUIStyle(style) { fontStyle = FontStyle.Bold };
            mine.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }

        var all = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
        float x = Screen.width - 200f, y = 120f;
        GUI.Label(new Rect(x, y, 200f, 22f), "SCORE   K / D", mine);
        y += 24f;

        // Highest kills first so the leader is always on top.
        System.Array.Sort(all, (a, b) => b.Kills.CompareTo(a.Kills));
        foreach (var p in all)
        {
            if (p == null) continue;
            GUI.Label(new Rect(x, y, 200f, 20f),
                $"{p.Label}   {p.Kills} / {p.Deaths}", p.IsOwner ? mine : style);
            y += 20f;
        }
    }
}
