using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Deathmatch win condition: first to killLimit wins, then scores reset and the next round
// starts. Deliberately short — a fast loop is worth more than a long one while the game is
// still being tuned, because you get many endings to judge instead of one.
//
// The server owns the decision. winnerId is a SyncVar so every client shows the same result
// at the same time rather than each deciding locally and disagreeing.
public class MatchManager : NetworkBehaviour
{
    [Tooltip("Kills needed to win. Low on purpose — short rounds mean more matches per " +
             "playtest and faster feedback on tuning.")]
    public int killLimit = 10;
    [Tooltip("Seconds the winner banner shows before scores reset and the next round starts.")]
    public float postMatchSeconds = 6f;

    // -1 means a round is in progress.
    readonly SyncVar<int> winnerId = new SyncVar<int>(-1);

    public bool MatchOver => winnerId.Value >= 0;

    float resetAt;
    GUIStyle banner, sub;

    // Called by the server after a kill is credited. Checks whether that ended the round.
    public void CheckForWinner()
    {
        if (!IsServerStarted || MatchOver) return;

        foreach (var p in FindObjectsByType<PlayerScore>(FindObjectsSortMode.None))
        {
            if (p == null || p.Kills < killLimit) continue;
            winnerId.Value = p.OwnerId;
            resetAt = Time.time + postMatchSeconds;
            return;
        }
    }

    void Update()
    {
        // Only the server runs the round clock; clients just render winnerId.
        if (!IsServerStarted || !MatchOver) return;
        if (Time.time < resetAt) return;

        foreach (var p in FindObjectsByType<PlayerScore>(FindObjectsSortMode.None))
            if (p != null) p.ResetScore();

        winnerId.Value = -1;
    }

    void OnGUI()
    {
        if (!MatchOver) return;

        if (banner == null)
        {
            banner = new GUIStyle(GUI.skin.label)
            { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            banner.normal.textColor = new Color(1f, 0.9f, 0.4f);
            sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }

        float w = 600f, cx = (Screen.width - w) * 0.5f, cy = Screen.height * 0.32f;
        var win = new GUIStyle(banner);
        win.normal.textColor = PlayerColors.For(winnerId.Value);
        GUI.Label(new Rect(cx, cy, w, 56f), $"{PlayerColors.NameFor(winnerId.Value)} WINS", win);
        GUI.Label(new Rect(cx, cy + 58f, w, 26f), "next round starting...", sub);
    }
}
