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
    // Oddball points. Lives here beside kills/deaths because the scoreboard, the winner check
    // and the reset all already walk PlayerScores — a separate component would be a fourth
    // thing to keep in step for one int.
    //
    // POINTS rather than held seconds: seconds could only ever be earned by the one player
    // holding the ball, so everyone else was playing a mode with no way to score. Points come
    // from holding AND from killing the carrier, which turns "chase the ball" into scoring
    // behaviour instead of unpaid labour.
    readonly SyncVar<int> oddballPoints = new SyncVar<int>();
    // Flashpoint: points banked by holding the point, one per second.
    readonly SyncVar<int> flashpoints = new SyncVar<int>();
    // CTF: flags carried home.
    readonly SyncVar<int> flagCaptures = new SyncVar<int>();

    public int Kills => kills.Value;
    public int Deaths => deaths.Value;
    public int OddballPoints => oddballPoints.Value;
    public int Flashpoints => flashpoints.Value;
    public int FlagCaptures => flagCaptures.Value;

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

    // Called by MatchManager: once a second while holding, and in a lump for a carrier kill.
    public void AddOddballPoints(int amount)
    {
        if (IsServerStarted && amount > 0) oddballPoints.Value += amount;
    }

    // Called by MatchManager once per second while this player holds the flashpoint.
    public void AddFlashpoint(int amount)
    {
        if (IsServerStarted && amount > 0) flashpoints.Value += amount;
    }

    public void AddFlagCapture()
    {
        if (IsServerStarted) flagCaptures.Value++;
    }

    // Called by MatchManager between rounds.
    public void ResetScore()
    {
        if (!IsServerStarted) return;
        kills.Value = 0;
        deaths.Value = 0;
        oddballPoints.Value = 0;
        flashpoints.Value = 0;
        flagCaptures.Value = 0;
    }

    void OnGUI()
    {
        // Only the local player draws it, or every player in the scene would stack a copy.
        if (!showScoreboard || !IsOwner || GameMenu.IsPaused || KeybindsUI.Open) return;

        // Hold-to-view (Tab), the FPS convention. Always-on it was furniture — permanently
        // covering a corner to answer a question the player asks a few times a match.
        if (!Keybinds.Held(GameAction.Scoreboard)) return;

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

        bool oddball = match != null && match.OddballMode;
        bool flash = match != null && match.FlashpointMode;
        // Objective modes get one extra column and sort by IT — the mode's own score decides
        // the round, so it must also decide the ranking the player reads.
        bool ctf = match != null && match.CtfMode;
        string objHeader = oddball ? "POINTS" : flash ? "POINTS" : ctf ? "FLAGS" : null;

        var all = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);

        // Centred, like the end-of-round board (MatchManager.DrawFinalScores) — the two now
        // read as the same UI seen mid-match vs after it. A corner list made sense when it
        // was permanently on screen and had to stay out of the way; a hold-to-view overlay
        // should sit where the eyes already are.
        const float w = 520f;
        float rows = all.Length;
        float h = 44f + rows * 30f + 10f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.24f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var head = new GUIStyle(style) { fontSize = 15 };
        head.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        GUI.Label(new Rect(x + 20f, y + 8f, w * 0.5f, 24f), "PLAYER", head);
        if (objHeader != null)
            GUI.Label(new Rect(x + w - 300f, y + 8f, 90f, 24f), objHeader, head);
        GUI.Label(new Rect(x + w - 200f, y + 8f, 80f, 24f), "KILLS", head);
        GUI.Label(new Rect(x + w - 110f, y + 8f, 90f, 24f), "DEATHS", head);

        // Leader on top by the mode's own score.
        if (oddball) System.Array.Sort(all, (a, b) => b.OddballPoints.CompareTo(a.OddballPoints));
        else if (flash) System.Array.Sort(all, (a, b) => b.Flashpoints.CompareTo(a.Flashpoints));
        else if (ctf) System.Array.Sort(all, (a, b) => b.FlagCaptures.CompareTo(a.FlagCaptures));
        else System.Array.Sort(all, (a, b) => b.Kills.CompareTo(a.Kills));

        float ry = y + 38f;
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p == null) continue;

            // Your own row is backlit rather than prefixed — findable at a glance without
            // parsing text.
            if (p.IsOwner)
            {
                GUI.color = new Color(1f, 0.9f, 0.4f, 0.12f);
                GUI.DrawTexture(new Rect(x + 6f, ry - 2f, w - 12f, 28f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // Row is drawn in that player's own colour, so the scoreboard and the arena agree.
            var row = new GUIStyle(p.IsOwner ? mine : style);
            row.normal.textColor = p.Tint;
            GUI.Label(new Rect(x + 20f, ry, w * 0.5f, 26f), $"{i + 1}.  {p.Label}", row);
            if (objHeader != null)
                GUI.Label(new Rect(x + w - 300f, ry, 90f, 26f),
                    (oddball ? p.OddballPoints : flash ? p.Flashpoints : p.FlagCaptures).ToString(), row);
            GUI.Label(new Rect(x + w - 200f, ry, 80f, 26f), p.Kills.ToString(), row);
            GUI.Label(new Rect(x + w - 110f, ry, 90f, 26f), p.Deaths.ToString(), row);
            ry += 30f;
        }
    }
}
