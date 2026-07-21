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

    // Game mode, decided by whoever hosts and synced so clients cannot disagree about whether
    // map resources exist.
    readonly SyncVar<bool> pickupsEnabled = new SyncVar<bool>(false);

    // Bot count, same deal: the host decides, everyone agrees. SimpleBot reads this to know
    // whether its slot is in play.
    readonly SyncVar<int> botCount = new SyncVar<int>(0);
    readonly SyncVar<float> botDifficulty = new SyncVar<float>(BotChoice.Practice);

    public bool MatchOver => winnerId.Value >= 0;
    public bool PickupsEnabled => pickupsEnabled.Value;
    public int BotCount => botCount.Value;
    public float BotDifficulty => botDifficulty.Value;

    public override void OnStartServer()
    {
        base.OnStartServer();
        pickupsEnabled.Value = GameModeChoice.Pickups; // the host's connect-screen choice
        botCount.Value = Mathf.Clamp(BotChoice.Count, 0, BotChoice.Max);
        botDifficulty.Value = Mathf.Clamp(BotChoice.Difficulty, 0.05f, 1f);
    }

    float resetAt;

    // Client-side copy of the same clock. resetAt is only ever written by the server, so a
    // client reading it would count down from zero. postMatchSeconds is identical everywhere,
    // so starting a local timer the moment the winner syncs is correct to within one trip.
    float localResetAt;

    GUIStyle banner, sub;

    void Awake() => winnerId.OnChange += OnWinnerChanged;

    void OnDestroy() => winnerId.OnChange -= OnWinnerChanged;

    void OnWinnerChanged(int prev, int next, bool asServer)
    {
        if (next >= 0) localResetAt = Time.time + postMatchSeconds;
    }

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

        float w = 600f, cx = (Screen.width - w) * 0.5f, cy = Screen.height * 0.22f;
        var win = new GUIStyle(banner);
        win.normal.textColor = PlayerColors.For(winnerId.Value);
        GUI.Label(new Rect(cx, cy, w, 62f), $"{WinnerName()} WINS", win);

        DrawFinalScores(cx, cy + 74f, w);

        float left = Mathf.Max(0f, localResetAt - Time.time);
        GUI.Label(new Rect(cx, cy + 74f + FinalScoresHeight() + 12f, w, 30f),
            $"next round in {Mathf.CeilToInt(left)}...", sub);
    }

    float FinalScoresHeight()
    {
        int rows = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None).Length;
        return 36f + rows * 32f;
    }

    // The round ends and the scores vanish a moment later. Without this the only record of how
    // it actually went is a banner naming one player — you never see whether you were second by
    // a kill or last by ten, which is most of what makes a short round worth replaying.
    void DrawFinalScores(float x, float y, float w)
    {
        var all = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
        if (all.Length == 0) return;
        System.Array.Sort(all, (a, b) => b.Kills.CompareTo(a.Kills));

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x, y, w, FinalScoresHeight()), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var head = new GUIStyle(sub) { alignment = TextAnchor.MiddleLeft, fontSize = 16 };
        head.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        GUI.Label(new Rect(x + 20f, y + 6f, w * 0.6f, 26f), "PLAYER", head);
        GUI.Label(new Rect(x + w - 220f, y + 6f, 90f, 26f), "KILLS", head);
        GUI.Label(new Rect(x + w - 120f, y + 6f, 90f, 26f), "DEATHS", head);

        float ry = y + 34f;
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p == null) continue;

            var name = new GUIStyle(sub)
            { alignment = TextAnchor.MiddleLeft, fontSize = 20, fontStyle = FontStyle.Bold };
            name.normal.textColor = p.Tint;
            var num = new GUIStyle(name) { alignment = TextAnchor.MiddleLeft };

            // The winner's row is lit rather than just first, so the result reads without
            // having to compare two numbers.
            if (p.OwnerId == winnerId.Value)
            {
                GUI.color = new Color(1f, 0.9f, 0.4f, 0.14f);
                GUI.DrawTexture(new Rect(x + 8f, ry - 2f, w - 16f, 30f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            GUI.Label(new Rect(x + 20f, ry, w * 0.6f, 28f), $"{i + 1}.  {p.Label}", name);
            GUI.Label(new Rect(x + w - 220f, ry, 90f, 28f), p.Kills.ToString(), num);
            GUI.Label(new Rect(x + w - 120f, ry, 90f, 28f), p.Deaths.ToString(), num);
            ry += 32f;
        }
    }

    // winnerId is an OwnerId, not a name — the name lives on that player's PlayerIdentity, so
    // it has to be looked up. Falls back to the colour if they left before the banner showed.
    string WinnerName()
    {
        foreach (var p in FindObjectsByType<PlayerScore>(FindObjectsSortMode.None))
            if (p != null && p.OwnerId == winnerId.Value) return p.Label;
        return PlayerColors.NameFor(winnerId.Value);
    }
}
