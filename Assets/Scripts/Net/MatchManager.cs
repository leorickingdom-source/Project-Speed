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
    [Tooltip("Round length in seconds. The kill limit ends a stomp early; this ends the " +
             "stalemates — two cautious players on a big map could previously go forever. " +
             "3 minutes, per playtest: long enough to come back from 0-3, short enough that " +
             "a bad loadout pick is not a life sentence.")]
    public float roundSeconds = 180f;
    [Tooltip("Seconds the winner banner shows before scores reset and the next round starts.")]
    public float postMatchSeconds = 6f;

    // Sentinels for winnerId: InProgress means the round is running; Draw means time ran out
    // with the top score shared. Anything >= 0 is the winning OwnerId.
    const int InProgress = -1;
    const int Draw = -2;

    readonly SyncVar<int> winnerId = new SyncVar<int>(InProgress);

    // Whole seconds left in the round. Server-written once a second — syncing a countdown as
    // int-on-change costs one message per second instead of one per tick, and a late joiner
    // gets the true remaining time for free, which a locally-started timer would get wrong.
    // -1 = no timed round running (offline practice has no clock).
    readonly SyncVar<int> secondsLeft = new SyncVar<int>(-1);

    float roundEndAt; // server-only, the authoritative deadline

    // Game mode, decided by whoever hosts and synced so clients cannot disagree about whether
    // map resources exist or what winning means. Values are GameModeChoice constants.
    readonly SyncVar<int> gameMode = new SyncVar<int>(GameModeChoice.PureDeathmatch);

    // Bot count, same deal: the host decides, everyone agrees. SimpleBot reads this to know
    // whether its slot is in play.
    readonly SyncVar<int> botCount = new SyncVar<int>(0);
    readonly SyncVar<float> botDifficulty = new SyncVar<float>(BotChoice.Practice);

    // ---- Oddball ------------------------------------------------------------------------
    // All state lives HERE as SyncVars, with purely local visuals derived from it on every
    // client — no new networked scene object, no prefab registration, nothing to forget in a
    // map. Any scene can host oddball; drop an "OddballSpawn" marker to place the ball, or
    // let the ground probe pick a spot.

    [Header("Oddball")]
    [Tooltip("Points that win the round. At 1 point a second plus carrier-kill bonuses, 75 is " +
             "reachable in a 180s round by holding well OR by hunting the holder well — the " +
             "two routes are meant to be roughly competitive with each other.")]
    public int oddballTarget = 75;
    [Tooltip("Points per second of holding the ball.")]
    public int pointsPerHeldSecond = 1;
    [Tooltip("Points for killing whoever is carrying. This is what makes the chase a way to " +
             "SCORE rather than merely a way to stop someone else scoring — without it, every " +
             "player who is not holding the ball is doing unpaid work. 8 is worth about eight " +
             "seconds of holding: significant, but no substitute for ever picking the ball up.")]
    public int carrierKillPoints = 8;
    [Tooltip("Seconds a dropped ball lies where it fell before returning to its spawn. Long " +
             "enough to fight over the body, short enough that a ball punted into a corner " +
             "does not hide the objective for the rest of the round.")]
    public float ballReturnSeconds = 15f;
    public float ballPickupRadius = 1.8f;

    // OwnerId of the carrier, or -1 while the ball is loose at ballPos.
    readonly SyncVar<int> carrierId = new SyncVar<int>(-1);
    readonly SyncVar<Vector3> ballPos = new SyncVar<Vector3>();

    float nextBallSecondAt;   // server: next whole-second credit for the carrier
    float ballReturnAt;       // server: when a loose ball goes home
    Vector3 ballSpawn;
    bool ballSpawnResolved;
    GameObject ballVisual;    // local on every client, driven from the SyncVars

    // ---- Rocket pickup ------------------------------------------------------------------
    [Header("Rocket pickup")]
    [Tooltip("Rockets granted per collection. Four at 90 splash is a short, loud reign.")]
    public int rocketGrant = 4;
    [Tooltip("Seconds before the launcher returns. Power-weapon rhythm: worth timing, not " +
             "worth camping — half the armour timer.")]
    public float rocketRespawnSeconds = 30f;
    public float rocketPickupRadius = 1.6f;

    readonly SyncVar<bool> rocketAvailable = new SyncVar<bool>(false);
    float rocketReadyAt;
    Vector3 rocketSpawn;
    bool rocketSpawnResolved;
    GameObject rocketVisual;

    public bool IsCarrier(int ownerId) => ownerId >= 0 && carrierId.Value == ownerId;

    // ---- Capture the Flag -----------------------------------------------------------------
    // FFA CTF, not team CTF. Teams would mean team assignment, team colours, team spawns,
    // friendly fire rules and a team scoreboard — a different game's worth of plumbing on a
    // codebase where every system assumes free-for-all. So: ONE flag in the middle, and every
    // player has their own base to run it to. Same tension as team CTF (one carrier, everyone
    // else hunting) with none of the team machinery, and it still works at two players.

    [Header("Capture the Flag")]
    [Tooltip("Captures that win the round.")]
    public int flagTarget = 3;
    [Tooltip("Radius to pick up a loose flag, and to score at your own base.")]
    public float flagRadius = 2.5f;
    [Tooltip("Seconds a dropped flag waits before returning to the middle. Longer than the " +
             "oddball's: a flag dropped halfway home is the most contested object in the mode " +
             "and cutting that fight short would waste the run that created it.")]
    public float flagReturnSeconds = 20f;

    readonly SyncVar<int> flagCarrierId = new SyncVar<int>(-1);
    readonly SyncVar<Vector3> flagPos = new SyncVar<Vector3>();

    float flagReturnAt;
    Vector3 flagHome;
    bool flagHomeResolved;
    GameObject flagVisual;
    GameObject baseVisual;

    public bool CtfMode => gameMode.Value == GameModeChoice.CaptureTheFlag;
    public bool IsFlagCarrier(int ownerId) => ownerId >= 0 && flagCarrierId.Value == ownerId;

    // ---- Scan cache ---------------------------------------------------------------------
    // Oddball, Flashpoint and the rocket pickup each used to sweep the scene with
    // FindObjectsByType EVERY frame, and the HUD did it again on every OnGUI pass (which runs
    // more than once per frame). Players do not join between frames; scanning at 4Hz and
    // sharing the result removes a handful of full-scene searches per frame for no behaviour
    // change. Entries can go null between refreshes, so every consumer still null-checks.
    PlayerHealth[] playersCache = System.Array.Empty<PlayerHealth>();
    PlayerScore[] scoresCache = System.Array.Empty<PlayerScore>();
    float nextScanAt;

    void RefreshScans()
    {
        if (Time.unscaledTime < nextScanAt) return;
        nextScanAt = Time.unscaledTime + 0.25f;
        playersCache = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        scoresCache = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
    }

    PlayerHealth[] Players { get { RefreshScans(); return playersCache; } }
    PlayerScore[] Scores { get { RefreshScans(); return scoresCache; } }

    // ---- Flashpoint ---------------------------------------------------------------------
    // One point active at a time; the sole living occupant fills a capture meter; a finished
    // capture scores and the point JUMPS to the anchor furthest away, turning the round into
    // race -> fight -> race. Same architecture as oddball: SyncVars on this object, local
    // visuals derived from them, optional "FlashSpawn_*" markers with a probe fallback.

    [Header("Flashpoint")]
    [Tooltip("Points that win the round. Earned by SECONDS HELD, not by completed captures: " +
             "an 8-second all-or-nothing meter paid nothing for seven seconds of holding a " +
             "contested point, so being pushed off at the last moment was worth exactly as " +
             "much as never going. Per-second scoring pays for the work as you do it.")]
    public int flashpointTarget = 60;
    [Tooltip("Points per second of holding the point alone. Named apart from the oddball's " +
             "rate so the two modes can be tuned independently.")]
    public int flashPointsPerSecond = 1;
    [Tooltip("Seconds before the point relocates. The move is what keeps this from being a " +
             "single siege: it forces a race, which is where the movement game lives.")]
    public float pointMoveSeconds = 30f;
    [Tooltip("Capture zone radius. 5 -> 10 -> 15 across playtests. At 5 the zone was smaller " +
             "than one dash, so a contester either landed inside a knife-fight or overshot and " +
             "stopped contesting by accident. 15 is a PLACE: wide enough to hold cover inside " +
             "it, to deny from its edge, and for two players to fight without both standing on " +
             "the same square metre — which is what makes contesting a play, not a coin-flip.")]
    public float captureRadius = 15f;

    // Where the active point is; who is capturing (-1 nobody); frozen-by-contest flag; and
    // capture progress in whole percent — synced as an int so it costs ~12 messages a second
    // during a capture instead of one per tick.
    readonly SyncVar<Vector3> flashPos = new SyncVar<Vector3>();
    readonly SyncVar<int> flashCapturerId = new SyncVar<int>(-1);
    readonly SyncVar<bool> flashContested = new SyncVar<bool>(false);
    // Whole seconds until the point moves, so every client can show the same countdown.
    readonly SyncVar<int> flashMoveIn = new SyncVar<int>(0);

    float nextFlashSecondAt;                  // server: next whole-second credit
    float pointMoveAt;                        // server: when the point relocates
    readonly System.Collections.Generic.List<Vector3> flashAnchors = new System.Collections.Generic.List<Vector3>();
    bool flashAnchorsResolved;
    GameObject flashVisual;

    public bool FlashpointMode => gameMode.Value == GameModeChoice.Flashpoint;

    public bool MatchOver => winnerId.Value != InProgress;
    public bool PickupsEnabled => gameMode.Value != GameModeChoice.PureDeathmatch;
    public bool OddballMode => gameMode.Value == GameModeChoice.Oddball;
    public int BotCount => botCount.Value;
    public float BotDifficulty => botDifficulty.Value;

    public override void OnStartServer()
    {
        base.OnStartServer();
        gameMode.Value = GameModeChoice.ModeIndex; // the host's connect-screen choice
        botCount.Value = Mathf.Clamp(BotChoice.Count, 0, BotChoice.Max);
        botDifficulty.Value = Mathf.Clamp(BotChoice.Difficulty, 0.05f, 1f);
        StartRoundClock();
        ResetOddball();
        ResetFlashpoint();
        ResetFlag();
        rocketAvailable.Value = true;
    }

    void StartRoundClock()
    {
        roundEndAt = Time.time + roundSeconds;
        secondsLeft.Value = Mathf.CeilToInt(roundSeconds);
    }

    float resetAt;

    // Anchors are probed against the LOADED map, and the map arrives after the server starts
    // (LoadChosenMap runs on the started callback). Any cached anchor from before that load —
    // or from a previous map — is a point in a scene that no longer exists.
    void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        ballSpawnResolved = false;
        rocketSpawnResolved = false;
        flashAnchorsResolved = false;
        // NetPresence FIRST, then IsSpawned, then IsServerStarted — each guard covers a
        // failure the next one cannot.
        //
        // IsSpawned before IsServerStarted: this callback fires during the scene load, which
        // on a client joining mid-flight happens before FishNet has initialised this
        // behaviour, and IsServerStarted dereferences the NetworkManager cache that
        // initialisation sets.
        //
        // HasNetworkManager before BOTH: IsSpawned is not the safe question it looks like.
        // Press Play with a MAP scene open instead of the boot scene and there is no
        // NetworkManager anywhere, so IsSpawned itself throws inside FishNet — an NRE per
        // scene load, from the guard that was supposed to prevent one. Costs nothing in a
        // real match, where the manager always exists by the time a map loads.
        if (NetPresence.HasNetworkManager && IsSpawned && IsServerStarted)
        {
            ResetOddball();
            ResetFlashpoint();
        }
    }

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
        // Objective rounds are won by the objective — kills there are means, not score.
        if (OddballMode || FlashpointMode || CtfMode) return;

        foreach (var p in Scores)
        {
            if (p == null || p.Kills < killLimit) continue;
            winnerId.Value = p.OwnerId;
            resetAt = Time.time + postMatchSeconds;
            return;
        }
    }

    void Update()
    {
        // Visuals run on every machine — they are derived state, like the pickup's bob.
        UpdateOddballVisual();
        UpdateRocketVisual();
        UpdateFlashVisual();
        UpdateFlagVisual();

        // Only the server runs the clocks; clients just render the SyncVars. IsSpawned is
        // checked first for the same reason as in OnSceneLoaded — Update runs from the frame
        // the scene loads, before FishNet has initialised this behaviour.
        if (!IsSpawned || !IsServerStarted) return;

        if (!MatchOver)
        {
            // Tick the round clock down, one whole second at a time.
            int s = Mathf.Max(0, Mathf.CeilToInt(roundEndAt - Time.time));
            if (s != secondsLeft.Value) secondsLeft.Value = s;

            ServerTickOddball();
            ServerTickRocketPickup();
            ServerTickFlashpoint();
            ServerTickFlag();

            if (s <= 0) EndByTime();
            return;
        }

        if (Time.time < resetAt) return;

        foreach (var p in Scores)
            if (p != null) p.ResetScore();

        winnerId.Value = InProgress;
        StartRoundClock();
        ResetOddball();
        ResetFlashpoint();
        ResetFlag();
        rocketAvailable.Value = true;
    }

    // ---- Oddball server logic -------------------------------------------------------------

    void ResetOddball()
    {
        carrierId.Value = -1;
        ballPos.Value = BallSpawn();
        ballReturnAt = 0f; // 0 = "at spawn", no return countdown running
    }

    void ServerTickOddball()
    {
        if (!OddballMode) return;

        if (carrierId.Value >= 0)
        {
            var carrier = FindPlayerByOwner(carrierId.Value);

            // Carrier died or left: the ball drops where they fell and the clock to go home
            // starts. Dropping (not resetting) is what makes killing the carrier worth the
            // trip — the ball is RIGHT THERE, contested, for everyone the fight attracted.
            if (carrier == null || !carrier.Alive)
            {
                ballPos.Value = carrier != null ? carrier.transform.position + Vector3.up * 0.5f
                                                : BallSpawn();
                carrierId.Value = -1;
                ballReturnAt = Time.time + ballReturnSeconds;
                return;
            }

            // Credit one held second at a time. Whole seconds, because the scoreboard, the
            // target and the HUD all speak in seconds — sub-second credit is spurious precision.
            if (Time.time >= nextBallSecondAt)
            {
                nextBallSecondAt += 1f;
                var score = carrier.GetComponent<PlayerScore>();
                if (score != null)
                {
                    score.AddOddballPoints(pointsPerHeldSecond);
                    CheckOddballWin(score, carrierId.Value);
                }
            }
            return;
        }

        // Loose ball: overdue -> home; otherwise anyone alive standing on it picks it up.
        if (ballReturnAt > 0f && Time.time >= ballReturnAt)
        {
            ballPos.Value = BallSpawn();
            ballReturnAt = 0f;
            return;
        }

        foreach (var hp in Players)
        {
            if (hp == null || !hp.Alive) continue;
            if ((hp.transform.position - ballPos.Value).sqrMagnitude >
                ballPickupRadius * ballPickupRadius) continue;
            var nob = hp.GetComponent<FishNet.Object.NetworkObject>();
            if (nob == null) continue;

            carrierId.Value = nob.OwnerId;
            nextBallSecondAt = Time.time + 1f;
            return;
        }
    }

    void CheckOddballWin(PlayerScore score, int ownerId)
    {
        if (MatchOver || score.OddballPoints < oddballTarget) return;
        winnerId.Value = ownerId;
        resetAt = Time.time + postMatchSeconds;
    }

    // Called by PlayerHealth.Die once the server has resolved a killer. Killing the carrier is
    // the second way to score in oddball — the reason a player with no ball still has a job.
    public void ReportKill(PlayerScore killerScore, int victimOwnerId)
    {
        if (!IsServerStarted || !OddballMode || MatchOver) return;
        if (killerScore == null || carrierId.Value != victimOwnerId) return;

        killerScore.AddOddballPoints(carrierKillPoints);
        CheckOddballWin(killerScore, killerScore.OwnerId);
    }

    PlayerHealth FindPlayerByOwner(int ownerId)
    {
        var players = Players;
        for (int i = 0; i < players.Length; i++)
        {
            var hp = players[i];
            if (hp == null) continue;
            var nob = hp.GetComponent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.OwnerId == ownerId) return hp;
        }
        return null;
    }

    // ---- Rocket pickup server logic ---------------------------------------------------------

    void ServerTickRocketPickup()
    {
        if (!PickupsEnabled) return; // power weapon belongs to the pickup economy

        if (!rocketAvailable.Value)
        {
            if (Time.time >= rocketReadyAt) rocketAvailable.Value = true;
            return;
        }

        Vector3 at = RocketSpawn();
        foreach (var hp in Players)
        {
            if (hp == null || !hp.Alive) continue;
            if ((hp.transform.position - at).sqrMagnitude >
                rocketPickupRadius * rocketPickupRadius) continue;
            var net = hp.GetComponent<PlayerNetwork>();
            var nob = hp.GetComponent<FishNet.Object.NetworkObject>();
            if (net == null || nob == null || nob.Owner == null) continue;

            net.GrantRockets(nob.Owner, rocketGrant);
            rocketAvailable.Value = false;
            rocketReadyAt = Time.time + rocketRespawnSeconds;
            return;
        }
    }

    // ---- Flashpoint server logic ------------------------------------------------------------

    void ResetFlashpoint()
    {
        flashCapturerId.Value = -1;
        flashContested.Value = false;
        nextFlashSecondAt = 0f;
        pointMoveAt = Time.time + pointMoveSeconds;
        flashMoveIn.Value = Mathf.CeilToInt(pointMoveSeconds);
        var anchors = FlashAnchors();
        if (anchors.Count > 0) flashPos.Value = anchors[0];
    }

    void ServerTickFlashpoint()
    {
        if (!FlashpointMode) return;

        // Relocation clock runs whether or not anyone is standing on it — a point nobody
        // contests should still move on, or a stalemate parks the mode in one corner.
        int moveIn = Mathf.Max(0, Mathf.CeilToInt(pointMoveAt - Time.time));
        if (moveIn != flashMoveIn.Value) flashMoveIn.Value = moveIn;
        if (moveIn <= 0) MovePoint();

        // Who is standing on it. Occupancy is 3D distance so a deck two floors up does not
        // hold the point through the ceiling.
        PlayerHealth sole = null;
        int count = 0;
        foreach (var hp in Players)
        {
            if (hp == null || !hp.Alive) continue;
            if ((hp.transform.position - flashPos.Value).sqrMagnitude >
                captureRadius * captureRadius) continue;
            count++;
            sole = hp;
        }

        if (count != 1)
        {
            // Empty or contested: nobody banks anything. Contested-means-frozen is what makes
            // walking into the circle a real play even when you cannot win the fight.
            flashContested.Value = count > 1;
            if (flashCapturerId.Value != -1) flashCapturerId.Value = -1;
            return;
        }

        flashContested.Value = false;
        var nob = sole.GetComponent<FishNet.Object.NetworkObject>();
        int id = nob != null ? nob.OwnerId : -1;
        if (id != flashCapturerId.Value)
        {
            flashCapturerId.Value = id;
            nextFlashSecondAt = Time.time + 1f;   // a new holder waits a full second to bank
            return;
        }

        if (Time.time < nextFlashSecondAt) return;
        nextFlashSecondAt += 1f;

        var score = sole.GetComponent<PlayerScore>();
        if (score == null) return;
        score.AddFlashpoint(flashPointsPerSecond);
        if (!MatchOver && score.Flashpoints >= flashpointTarget)
        {
            winnerId.Value = id;
            resetAt = Time.time + postMatchSeconds;
        }
    }

    // Send the point somewhere else, biased far, and clear the hold so the new location is
    // genuinely up for grabs.
    void MovePoint()
    {
        var anchors = FlashAnchors();
        Vector3 here = flashPos.Value;
        var far = new System.Collections.Generic.List<Vector3>();
        float furthest = 0f;
        foreach (var a in anchors) furthest = Mathf.Max(furthest, (a - here).sqrMagnitude);
        foreach (var a in anchors)
            if ((a - here).sqrMagnitude >= furthest * 0.35f) far.Add(a);
        if (far.Count > 0) flashPos.Value = far[Random.Range(0, far.Count)];

        flashCapturerId.Value = -1;
        flashContested.Value = false;
        pointMoveAt = Time.time + pointMoveSeconds;
        flashMoveIn.Value = Mathf.CeilToInt(pointMoveSeconds);
    }

    // Anchor set for the roaming point. "FlashSpawn_*" markers when the map places them;
    // otherwise probed candidates between the arena centre and each player spawn, which on
    // both shipped maps yields a usable ring of grounded spots.
    System.Collections.Generic.List<Vector3> FlashAnchors()
    {
        if (flashAnchorsResolved) return flashAnchors;
        flashAnchors.Clear();
        // Only remembered once it actually found somewhere — see the note above BallSpawn for
        // why caching a failed probe puts objectives at the world origin for a whole match.
        flashAnchorsResolved = true;

        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.name.StartsWith("FlashSpawn"))
                flashAnchors.Add(t.position + Vector3.up * 0.5f);
        if (flashAnchors.Count >= 2) return flashAnchors;
        flashAnchors.Clear();

        var spawner = FindAnyObjectByType<FishNet.Component.Spawning.PlayerSpawner>();
        var points = spawner != null ? spawner.Spawns : null;
        if (points != null && points.Length > 0)
        {
            Vector3 centre = Vector3.zero;
            int n = 0;
            foreach (var t in points) { if (t != null) { centre += t.position; n++; } }
            if (n > 0)
            {
                centre /= n;
                // 0.85 toward each spawn: the spawn ring sits at the map edges, so lerping
                // only halfway pulled every anchor into the middle and roughly halved the
                // distance between consecutive points.
                foreach (var t in points)
                {
                    if (t == null) continue;
                    Vector3 candidate = Vector3.Lerp(centre, t.position, 0.85f);
                    if (Physics.Raycast(candidate + Vector3.up * 12f, Vector3.down,
                            out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                        flashAnchors.Add(hit.point + Vector3.up * 0.5f);
                }
            }
        }

        if (flashAnchors.Count == 0)
        {
            flashAnchors.Add(Vector3.up);
            flashAnchorsResolved = false;   // pure fallback — try again once the map is up
        }
        return flashAnchors;
    }

    // Ring on the ground plus a tall beacon, so "where is the point" is answerable from
    // anywhere on the map without a minimap. Gold while open or being held, red while
    // contested — the colour IS the state, readable at any distance.
    void UpdateFlashVisual()
    {
        bool want = FlashpointMode && !MatchOver;
        if (flashVisual == null && want)
        {
            flashVisual = new GameObject("Flashpoint");

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(flashVisual.transform, false);
            ring.transform.localScale = new Vector3(captureRadius * 2f, 0.05f, captureRadius * 2f);

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(flashVisual.transform, false);
            beacon.transform.localPosition = Vector3.up * 20f;
            beacon.transform.localScale = new Vector3(0.6f, 20f, 0.6f);

            // Sprites/Default for the same reason BlastFx uses it: unlit and alpha-respecting
            // without URP surface surgery.
            Shader sh = Shader.Find("Sprites/Default");
            foreach (var r in flashVisual.GetComponentsInChildren<Renderer>())
                if (sh != null) r.material = new Material(sh);
        }
        if (flashVisual == null) return;
        if (!want) { flashVisual.SetActive(false); return; }
        if (!flashVisual.activeSelf) flashVisual.SetActive(true);

        flashVisual.transform.position = flashPos.Value;

        Color c = flashContested.Value
            ? new Color(1f, 0.35f, 0.3f, 0.4f)      // contested — red
            : new Color(1f, 0.8f, 0.3f, 0.35f);     // open or held — gold
        // Breathe a little so it reads as live, not level geometry. Beats faster in the last
        // five seconds before the point moves, which is the cue to start running.
        float rate = flashMoveIn.Value <= 5 ? 8f : 3f;
        c.a *= 0.8f + 0.2f * Mathf.Sin(Time.time * rate);
        foreach (var r in flashVisual.GetComponentsInChildren<Renderer>())
        {
            var col = c;
            if (r.name == "Beacon") col.a *= 0.35f;  // faint pillar, loud ring
            r.material.color = col;
        }
    }

    // ---- Capture the Flag server logic ------------------------------------------------------

    void ResetFlag()
    {
        flagCarrierId.Value = -1;
        flagPos.Value = FlagHome();
        flagReturnAt = 0f;
    }

    void ServerTickFlag()
    {
        if (!CtfMode) return;

        if (flagCarrierId.Value >= 0)
        {
            var carrier = FindPlayerByOwner(flagCarrierId.Value);

            // Dropped where they fell. The flag staying put is what makes killing a carrier
            // near their base worth doing — you can pick it up and run the other way.
            if (carrier == null || !carrier.Alive)
            {
                flagPos.Value = carrier != null ? carrier.transform.position + Vector3.up * 0.5f
                                                : FlagHome();
                flagCarrierId.Value = -1;
                flagReturnAt = Time.time + flagReturnSeconds;
                return;
            }

            // Home with it?
            Vector3 home = BaseFor(flagCarrierId.Value);
            if ((carrier.transform.position - home).sqrMagnitude <= flagRadius * flagRadius)
            {
                var score = carrier.GetComponent<PlayerScore>();
                if (score != null)
                {
                    score.AddFlagCapture();
                    if (!MatchOver && score.FlagCaptures >= flagTarget)
                    {
                        winnerId.Value = flagCarrierId.Value;
                        resetAt = Time.time + postMatchSeconds;
                    }
                }
                ResetFlag();
            }
            return;
        }

        if (flagReturnAt > 0f && Time.time >= flagReturnAt)
        {
            flagPos.Value = FlagHome();
            flagReturnAt = 0f;
            return;
        }

        foreach (var hp in Players)
        {
            if (hp == null || !hp.Alive) continue;
            if ((hp.transform.position - flagPos.Value).sqrMagnitude > flagRadius * flagRadius) continue;
            var nob = hp.GetComponent<FishNet.Object.NetworkObject>();
            if (nob == null) continue;
            flagCarrierId.Value = nob.OwnerId;
            flagReturnAt = 0f;
            return;
        }
    }

    // Each player's delivery point: their own slot in the spawn ring, chosen by OwnerId so
    // every machine derives the same base for the same player with nothing extra on the wire.
    Vector3 BaseFor(int ownerId)
    {
        var spawner = FindAnyObjectByType<FishNet.Component.Spawning.PlayerSpawner>();
        var points = spawner != null ? spawner.Spawns : null;
        if (points == null || points.Length == 0) return FlagHome();

        // Count only live entries — a stale array after a map change would index into nulls.
        int live = 0;
        for (int i = 0; i < points.Length; i++) if (points[i] != null) live++;
        if (live == 0) return FlagHome();

        int want = ((ownerId % live) + live) % live;
        int seen = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null) continue;
            if (seen == want) return points[i].position + Vector3.up * 0.5f;
            seen++;
        }
        return FlagHome();
    }

    Vector3 FlagHome()
    {
        if (!flagHomeResolved)
            flagHomeResolved = TryResolveAnchor("FlagSpawn", 0.0f, out flagHome);
        return flagHome;
    }

    // The flag, plus a marker over YOUR base so "where do I take this" needs no map screen.
    void UpdateFlagVisual()
    {
        bool want = CtfMode && !MatchOver;

        if (flagVisual == null && want)
        {
            flagVisual = new GameObject("Flag");
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(flagVisual.transform, false);
            pole.transform.localScale = new Vector3(0.12f, 1.1f, 0.12f);
            pole.transform.localPosition = Vector3.up * 1.1f;
            TintLocal(pole, new Color(0.9f, 0.9f, 0.95f), 1.2f);

            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "Cloth";
            Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(flagVisual.transform, false);
            cloth.transform.localScale = new Vector3(0.9f, 0.6f, 0.05f);
            cloth.transform.localPosition = new Vector3(0.5f, 1.8f, 0f);
            TintLocal(cloth, new Color(1f, 0.85f, 0.25f), 2f);

            baseVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseVisual.name = "MyBase";
            Destroy(baseVisual.GetComponent<Collider>());
            baseVisual.transform.localScale = new Vector3(flagRadius * 2f, 0.05f, flagRadius * 2f);
            TintLocal(baseVisual, new Color(0.4f, 1f, 0.5f), 1.6f);
        }
        if (flagVisual == null) return;
        if (!want)
        {
            flagVisual.SetActive(false);
            if (baseVisual != null) baseVisual.SetActive(false);
            return;
        }
        if (!flagVisual.activeSelf) flagVisual.SetActive(true);

        if (flagCarrierId.Value >= 0)
        {
            var carrier = FindPlayerByOwner(flagCarrierId.Value);
            if (carrier != null)
                flagVisual.transform.position = carrier.transform.position + Vector3.up * 1.2f;
        }
        else
        {
            flagVisual.transform.position = flagPos.Value;
        }
        flagVisual.transform.Rotate(Vector3.up, 70f * Time.deltaTime, Space.World);

        // Only the local player's base is drawn: everyone running to a different corner is the
        // mode's whole shape, and drawing all of them would be four rings of noise.
        if (baseVisual != null)
        {
            int me = LocalOwnerId();
            bool showBase = me >= 0;
            if (baseVisual.activeSelf != showBase) baseVisual.SetActive(showBase);
            if (showBase) baseVisual.transform.position = BaseFor(me);
        }
    }

    static void TintLocal(GameObject go, Color c, float emission)
    {
        var m = go.GetComponent<Renderer>().material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
    }

    // OwnerId of the player on THIS machine, or -1 on a server with no local player.
    int LocalOwnerId()
    {
        foreach (var hp in Players)
        {
            if (hp == null) continue;
            var net = hp.GetComponent<PlayerNetwork>();
            if (net != null && net.IsOwner) return net.OwnerId;
        }
        return -1;
    }

    // ---- Anchor points ----------------------------------------------------------------------

    // A named marker wins; otherwise probe for standable ground near the middle of the spawn
    // ring. The probe exists because "the centre of the map" is a pit on Arena and a pillar on
    // Stacks — the naive answer is wrong on both shipped maps.
    // NOTE on the caching below: a FAILED probe must never be cached. The first Update after
    // a map load can easily beat SpawnPointBinder to the rebind, leaving PlayerSpawner.Spawns
    // full of destroyed Transforms from the previous scene — the probe then finds nothing and
    // falls back to (0,1,0). Caching that answer put the rocket pickup and the oddball at the
    // world origin for the whole match, which on Expanse is INSIDE the centre platform and on
    // Arena is down the pit: both invisible, which is exactly what the playtest reported.
    // Retrying next frame costs one raycast and fixes itself the moment the binder runs.
    Vector3 BallSpawn()
    {
        if (!ballSpawnResolved)
            ballSpawnResolved = TryResolveAnchor("OddballSpawn", 0.35f, out ballSpawn);
        return ballSpawn;
    }

    Vector3 RocketSpawn()
    {
        // Biased further out than the ball so the two objectives never share a doorway.
        if (!rocketSpawnResolved)
            rocketSpawnResolved = TryResolveAnchor("RocketSpawn", 0.65f, out rocketSpawn);
        return rocketSpawn;
    }

    // Returns false when it could only guess, so the caller knows not to remember the answer.
    bool TryResolveAnchor(string markerName, float outwardBias, out Vector3 result)
    {
        result = Vector3.up;

        var marker = GameObject.Find(markerName);
        if (marker != null)
        {
            result = marker.transform.position + Vector3.up * 0.9f;
            return true;
        }

        var spawner = FindAnyObjectByType<FishNet.Component.Spawning.PlayerSpawner>();
        var points = spawner != null ? spawner.Spawns : null;
        if (points == null || points.Length == 0) return false;

        Vector3 centre = Vector3.zero;
        int n = 0;
        foreach (var t in points) { if (t != null) { centre += t.position; n++; } }
        if (n == 0) return false; // stale array from the previous scene — try again next frame
        centre /= n;

        // Walk candidates from centre-ish outward until one has ground under it.
        foreach (var t in points)
        {
            if (t == null) continue;
            Vector3 candidate = Vector3.Lerp(centre, t.position, outwardBias);
            if (Physics.Raycast(candidate + Vector3.up * 12f, Vector3.down, out RaycastHit hit,
                    30f, ~0, QueryTriggerInteraction.Ignore))
            {
                result = hit.point + Vector3.up * 0.9f;
                return true;
            }
        }

        // Every probe missed (all-void map?) — a spawn point is at least standable.
        foreach (var t in points)
            if (t != null) { result = t.position + Vector3.up * 0.9f; return true; }
        return false;
    }

    // ---- Local visuals ----------------------------------------------------------------------

    // The ball every client sees: floats over the carrier's head, or bobs where it lies.
    // Purely cosmetic — position is derived fresh every frame from synced state, so it can
    // lag or pop but never disagree about WHO has it or WHERE it rests.
    void UpdateOddballVisual()
    {
        bool want = OddballMode && !MatchOver;
        if (ballVisual == null && want)
        {
            ballVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballVisual.name = "Oddball";
            Destroy(ballVisual.GetComponent<Collider>());
            ballVisual.transform.localScale = Vector3.one * 0.55f;
            var m = ballVisual.GetComponent<Renderer>().material;
            Color c = new Color(0.55f, 0.2f, 0.9f); // violet — no player colour comes close
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f); // lit like the pickups: an objective is ON
            }
        }
        if (ballVisual == null) return;
        if (!want) { ballVisual.SetActive(false); return; }
        if (!ballVisual.activeSelf) ballVisual.SetActive(true);

        if (carrierId.Value >= 0)
        {
            var carrier = FindPlayerByOwner(carrierId.Value);
            if (carrier != null)
            {
                // Above the head: the carrier must be readable across the arena — that beacon
                // is the pressure that makes carrying a commitment.
                ballVisual.transform.position = carrier.transform.position + Vector3.up * 2.6f;
                ballVisual.transform.Rotate(Vector3.up, 180f * Time.deltaTime, Space.World);
                return;
            }
        }

        ballVisual.transform.position = ballPos.Value
            + Vector3.up * (0.2f + Mathf.Sin(Time.time * 2f) * 0.15f);
        ballVisual.transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
    }

    void UpdateRocketVisual()
    {
        bool want = PickupsEnabled && rocketAvailable.Value && !MatchOver;
        if (rocketVisual == null && want)
        {
            rocketVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rocketVisual.name = "RocketPickup";
            Destroy(rocketVisual.GetComponent<Collider>());
            // Lying on its side, rocket-ish. Distinct SILHOUETTE from the sphere pickups —
            // shape survives distance and colour blindness, hue does not.
            rocketVisual.transform.localScale = new Vector3(0.3f, 0.7f, 0.3f);
            var m = rocketVisual.GetComponent<Renderer>().material;
            Color c = new Color(1f, 0.5f, 0.15f); // matches the projectile it grants
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 1.6f);
            }
        }
        if (rocketVisual == null) return;
        if (!want) { rocketVisual.SetActive(false); return; }
        if (!rocketVisual.activeSelf) rocketVisual.SetActive(true);

        rocketVisual.transform.position = RocketSpawn()
            + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.15f);
        rocketVisual.transform.rotation =
            Quaternion.Euler(90f, Time.time * 60f % 360f, 0f); // horizontal, slowly sweeping
    }

    // Time ran out with nobody at the limit: the mode's own score decides — kills in
    // deathmatch, held seconds in oddball. A shared top score is announced as a draw rather
    // than handed to whoever sorts first — being robbed of a win by array order would be
    // worse than splitting it.
    void EndByTime()
    {
        int best = int.MinValue, bestId = Draw;
        bool tied = false;
        foreach (var p in Scores)
        {
            if (p == null) continue;
            int s = OddballMode ? p.OddballPoints
                  : FlashpointMode ? p.Flashpoints
                  : CtfMode ? p.FlagCaptures
                  : p.Kills;
            if (s > best) { best = s; bestId = p.OwnerId; tied = false; }
            else if (s == best) tied = true;
        }

        winnerId.Value = tied ? Draw : bestId;
        resetAt = Time.time + postMatchSeconds;
    }

    void OnGUI()
    {
        if (banner == null)
        {
            banner = new GUIStyle(GUI.skin.label)
            { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            banner.normal.textColor = new Color(1f, 0.9f, 0.4f);
            sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }

        if (!MatchOver)
        {
            DrawRoundClock();
            return;
        }

        float w = 600f, cx = (Screen.width - w) * 0.5f, cy = Screen.height * 0.22f;
        var win = new GUIStyle(banner);
        bool draw = winnerId.Value == Draw;
        if (!draw) win.normal.textColor = PlayerColors.For(winnerId.Value);
        GUI.Label(new Rect(cx, cy, w, 62f), draw ? "TIME — DRAW" : $"{WinnerName()} WINS", win);

        DrawFinalScores(cx, cy + 74f, w);

        float left = Mathf.Max(0f, localResetAt - Time.time);
        GUI.Label(new Rect(cx, cy + 74f + FinalScoresHeight() + 12f, w, 30f),
            $"next round in {Mathf.CeilToInt(left)}...", sub);
    }

    // m:ss at the top centre. Small and out of the crosshair's way — it matters at the round's
    // ends, not during a fight. Goes red for the last 30 seconds, when it starts changing
    // decisions: a leader plays safe, a chaser has to force something NOW.
    void DrawRoundClock()
    {
        int s = secondsLeft.Value;
        if (s < 0) return; // no timed round running (offline practice)
        if (GameMenu.IsPaused || KeybindsUI.Open) return;

        var clock = new GUIStyle(sub) { fontSize = 24, fontStyle = FontStyle.Bold };
        clock.normal.textColor = s <= 30 ? new Color(1f, 0.4f, 0.35f)
                                         : new Color(1f, 1f, 1f, 0.85f);
        GUI.Label(new Rect((Screen.width - 120f) * 0.5f, 10f, 120f, 30f),
            $"{s / 60}:{s % 60:00}", clock);

        // The objective line: who to chase, or where to run. One glance, no scoreboard.
        if (OddballMode)
        {
            var line = new GUIStyle(sub) { fontSize = 17, fontStyle = FontStyle.Bold };
            string text;
            if (carrierId.Value >= 0)
            {
                var carrier = FindPlayerByOwner(carrierId.Value);
                var score = carrier != null ? carrier.GetComponent<PlayerScore>() : null;
                int held = score != null ? score.OddballPoints : 0;
                bool mine = carrier != null && carrier.GetComponent<PlayerNetwork>() != null
                            && carrier.GetComponent<PlayerNetwork>().IsOwner;
                line.normal.textColor = PlayerColors.For(carrierId.Value);
                text = mine ? $"YOU HAVE THE BALL   {held} / {oddballTarget}   (swing it)"
                            : $"{(score != null ? score.Label : "someone")} has the ball   {held} / {oddballTarget}";
            }
            else
            {
                line.normal.textColor = new Color(0.75f, 0.55f, 1f); // the ball's violet
                text = "BALL FREE — grab it";
            }
            GUI.Label(new Rect((Screen.width - 500f) * 0.5f, 40f, 500f, 26f), text, line);
        }

        if (CtfMode)
        {
            var line = new GUIStyle(sub) { fontSize = 17, fontStyle = FontStyle.Bold };
            string text;
            if (flagCarrierId.Value >= 0)
            {
                var carrier = FindPlayerByOwner(flagCarrierId.Value);
                var score = carrier != null ? carrier.GetComponent<PlayerScore>() : null;
                var net = carrier != null ? carrier.GetComponent<PlayerNetwork>() : null;
                bool mine = net != null && net.IsOwner;
                line.normal.textColor = PlayerColors.For(flagCarrierId.Value);
                text = mine
                    ? "YOU HAVE THE FLAG — run it to your green base"
                    : $"{(score != null ? score.Label : "someone")} has the flag — cut them off";
            }
            else
            {
                line.normal.textColor = new Color(1f, 0.85f, 0.25f);
                text = "FLAG LOOSE — grab it";
            }
            GUI.Label(new Rect((Screen.width - 560f) * 0.5f, 40f, 560f, 26f), text, line);
        }

        if (FlashpointMode)
        {
            var line = new GUIStyle(sub) { fontSize = 17, fontStyle = FontStyle.Bold };
            string text;
            if (flashContested.Value)
            {
                line.normal.textColor = new Color(1f, 0.45f, 0.4f);
                text = "POINT CONTESTED — nobody scores";
            }
            else if (flashCapturerId.Value >= 0)
            {
                var holder = FindPlayerByOwner(flashCapturerId.Value);
                var score = holder != null ? holder.GetComponent<PlayerScore>() : null;
                var pn = holder != null ? holder.GetComponent<PlayerNetwork>() : null;
                bool mine = pn != null && pn.IsOwner;
                int pts = score != null ? score.Flashpoints : 0;
                line.normal.textColor = PlayerColors.For(flashCapturerId.Value);
                text = mine
                    ? $"HOLDING   {pts} / {flashpointTarget}   ·  moves in {flashMoveIn.Value}s"
                    : $"{(score != null ? score.Label : "someone")} holding   {pts} / {flashpointTarget}  ·  moves in {flashMoveIn.Value}s";
            }
            else
            {
                line.normal.textColor = new Color(1f, 0.85f, 0.4f);
                text = $"POINT OPEN — stand on it  ·  moves in {flashMoveIn.Value}s";
            }
            GUI.Label(new Rect((Screen.width - 620f) * 0.5f, 40f, 620f, 26f), text, line);
        }
    }

    float FinalScoresHeight()
    {
        int rows = Scores.Length;
        return 36f + rows * 32f;
    }

    // The round ends and the scores vanish a moment later. Without this the only record of how
    // it actually went is a banner naming one player — you never see whether you were second by
    // a kill or last by ten, which is most of what makes a short round worth replaying.
    void DrawFinalScores(float x, float y, float w)
    {
        var all = Scores;
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
        foreach (var p in Scores)
            if (p != null && p.OwnerId == winnerId.Value) return p.Label;
        return PlayerColors.NameFor(winnerId.Value);
    }
}
