using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// The player's hit points. Takes damage through IDamageable (bots, other players), dies and
// respawns at the spawn point with a short invulnerability window. No passive regen by
// default (arena-style); set regenPerSec > 0 for a Halo-style recharge.
//
// SERVER-AUTHORITATIVE once networked: HP lives in a SyncVar that only the server writes,
// so every client sees the same health and a client cannot heal itself by editing memory.
// Offline (not spawned) it keeps full local authority, so single-player still works exactly
// as before — that's what HasAuthority encodes.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [Tooltip("BASE max HP, before passives. Read MaxHp for the effective value. 150 keeps the " +
             "sniper a 2-shot body kill (100 dmg) while a headshot (200) still one-shots — so " +
             "aim is rewarded and a body hit leaves a real counterplay window.")]
    public float maxHp = 150f;
    [Tooltip("Extra max HP from the Vitality passive. 40 (not 50) on purpose: at 190 a sniper " +
             "body shot is still a 2-shot and a headshot still one-shots, both with margin. " +
             "50 would land you on exactly 200, where BOTH of those sit at zero margin and any " +
             "later tweak to sniper damage or headMultiplier silently flips lethality.")]
    public float vitalityBonusHp = 40f;
    [Tooltip("HP the Bloodrush passive returns for a kill. This game has NO regen of any kind, " +
             "so a won fight still walks you toward death by attrition and the only sustain is " +
             "a pickup someone else is standing on. Speed-healing was rejected because it was " +
             "passive regen wearing a condition; a kill is not — it costs an opponent, which is " +
             "the only price this game accepts for health. 30 of 150 is a fight's worth of chip " +
             "damage back, not a reset: it rewards finishing what you started without making a " +
             "winning player unkillable.")]
    public float bloodrushHeal = 30f;
    public float respawnDelay = 1.5f;
    public float spawnInvuln = 2f;         // no damage for this long after (re)spawn

    [Header("Regen (0 = off)")]
    public float regenPerSec = 0f;
    public float regenDelay = 3f;          // idle time after damage before regen starts

    // SPEED HEALING REMOVED (playtest: "unkillable now"). The idea was that movement should
    // be the only sustain outside pickups; in practice this game's whole design pushes
    // players ABOVE the threshold almost permanently — bhop, slide, grapple and pads all
    // exist to keep you fast — so the condition was not a condition, it was passive regen
    // with extra steps, and passive regen is the thing this game deliberately does not have.
    //
    // Kept as a note rather than a disabled field: the fix for "reward speed" is not a
    // smaller number here (a slower always-on regen is still always-on), it is a reward that
    // costs the opponent something, e.g. the existing Momentum passive.
    //
    // The HUD's SpeedHealing tint went with it.

    [Tooltip("Where to respawn. Defaults to the player's start pose.")]
    public Transform spawnPoint;

    [Header("Bounds")]
    [Tooltip("Fall below this world Y and you die + respawn. Floor top is y=0, so -10 kills a " +
             "pit fall in ~0.7s instead of the ~2s the old -25 took, while leaving a brief " +
             "window to grapple back out.")]
    public float killY = -10f;
    [Tooltip("Die if you get farther than this from arena center on X or Z — the SIDE " +
             "out-of-bounds, so being launched over a wall by a grapple/dash/pad resets you at " +
             "once instead of arcing out and waiting for the long fall. Square half-extent " +
             "because the arena is square; walls sit at 45.5, so 48 clears them with margin. " +
             "0 = off.")]
    public float killDist = 48f;

    // The single source of truth for health. Only the server assigns it once spawned;
    // clients receive it and react through OnChange.
    readonly SyncVar<float> hp = new SyncVar<float>();

    public float Hp => hp.Value;
    // Derived rather than stored, so it can never disagree with the synced HP.
    public bool Alive => hp.Value > 0f;
    public bool Invulnerable => Time.time < invulnUntil;

    // Who may mutate health: the server when networked, ourselves when running offline.
    bool HasAuthority => !IsSpawned || IsServerStarted;

    // Effective ceiling = base + passives. Everything that clamps or refills reads THIS,
    // never the raw maxHp field, so equipping Vitality can't leave you capped at the base.
    public float MaxHp => maxHp + (passives != null && passives.Has(PassiveType.Vitality)
        ? vitalityBonusHp : 0f);

    PlayerMotor motor;
    PassiveLoadout passives;
    PlayerArmour armour;                              // optional — null means no armour pool
    DeathCam deathCam;                                // owner-only; null on remote players
    FishNet.Component.Spawning.PlayerSpawner spawner; // spawn point source, found lazily
    Vector3 spawnPos;
    Quaternion spawnRot;
    float invulnUntil;
    float lastDamageTime = -999f;
    float reviveAt;

    // Where the last shot came from, recorded on THIS client (see PlayerNetwork.ShowDamageFrom,
    // which already sends it for the damage indicator). Kept locally rather than synced because
    // the only consumer is the local death camera, and the message already exists — a second
    // "who killed you" RPC would be the same fact travelling twice.
    Vector3 lastAttackerPos;
    float lastAttackerAt = -999f;
    string lastAttackerName;
    // Live reference, so the death camera can FOLLOW them rather than hold on the spot they
    // fired from. Goes null on its own if they despawn, which the camera handles.
    Transform lastAttackerTransform;

    // The SERVER's own record of who last hurt this player. Separate from the client-side one
    // above because they answer different questions from different machines: that one aims the
    // local death camera, this one decides what the kill feed says. The server cannot read the
    // client's copy, and the client must not be trusted to report its own killer.
    FishNet.Object.NetworkObject serverAttacker;
    float serverAttackerAt = -999f;
    // How that last recorded hit landed — so the feed can say HOW, not just who.
    KillKind serverAttackerKind;

    // The visual capsule, hidden while dead so the corpse spawned in its place is the only
    // body on screen — a corpse falling out of a still-standing player reads as a clone.
    // Renderers are gathered at death time, not cached at Awake: the head cap is added later
    // (PlayerNetwork.OnStartClient), and a cached list would leave it floating over the corpse.
    Transform bodyT;
    Renderer[] hiddenRends;
    bool[] hiddenPrev;

    // Colliders switched off while dead. A corpse that still blocks bullets reads as an
    // INVINCIBLE body: shots stop on it, no damage happens (Damage refuses when !Alive), and
    // from the shooter's side an enemy is standing there eating everything. Reported straight
    // from playtest. The visual corpse is layer 2 (Ignore Raycast) and already excluded from
    // weapon masks; this is the real player capsule, which was not.
    Collider[] deadCols;
    bool[] deadColsPrev;

    // Name of whoever last hit us, for the death screen. Null when nobody did.
    public string LastAttackerName => HasFreshAttacker ? lastAttackerName : null;

    [Tooltip("How long a recorded attacker stays valid for the death camera. Past this the " +
             "death was probably a pit fall or someone else entirely, and pointing the camera " +
             "at a stale position is worse than not pointing it anywhere.")]
    public float attackerMemory = 5f;

    // Local countdown, started when this client sees health hit zero. Not synced: respawnDelay
    // is identical on every machine, so the only difference is one-way latency, and a shared
    // clock is not worth an RPC for a number that is read as "about a second and a half".
    float localRespawnAt;

    // Seconds until this player is back, for the HUD. 0 when alive.
    public float RespawnCountdown => Alive ? 0f : Mathf.Max(0f, localRespawnAt - Time.time);

    // Called on the victim's own client from the damage report.
    public void RecordAttacker(Vector3 worldPos, string attackerName, Transform attacker)
    {
        lastAttackerPos = worldPos;
        lastAttackerName = attackerName;
        lastAttackerTransform = attacker;
        lastAttackerAt = Time.time;

        // The killing report can arrive AFTER the death itself: the health SyncVar and this
        // RPC travel separately, and nothing guarantees their order. When that happens the
        // death camera has already started facing nothing — hand it the killer late rather
        // than leaving it staring at a wall for the whole respawn.
        if (!Alive && deathCam != null && deathCam.enabled)
            deathCam.Retarget(attacker, worldPos);
    }

    // Called on the SERVER from PlayerNetwork.ReportHit, before the damage lands, so that Die
    // can name a killer without every damage path having to remember to announce one.
    public void RecordServerAttacker(FishNet.Object.NetworkObject attacker, KillKind kind)
    {
        serverAttacker = attacker;
        serverAttackerAt = Time.time;
        serverAttackerKind = kind;
    }

    bool HasFreshAttacker => Time.time - lastAttackerAt <= attackerMemory;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        passives = GetComponent<PassiveLoadout>(); // optional — null means no passives equipped
        armour = GetComponent<PlayerArmour>();
        deathCam = GetComponent<DeathCam>();
        bodyT = transform.Find("Body");
        if (spawnPoint != null) { spawnPos = spawnPoint.position; spawnRot = spawnPoint.rotation; }
        else { spawnPos = transform.position; spawnRot = transform.rotation; }
        hp.Value = MaxHp;
        invulnUntil = Time.time + spawnInvuln;
        // Every client reacts to health changes locally (freeze on death, unfreeze on
        // respawn) even though only the server decides them.
        hp.OnChange += OnHpChanged;
    }

    void OnDestroy() => hp.OnChange -= OnHpChanged;

    void OnHpChanged(float prev, float next, bool asServer)
    {
        if (prev > 0f && next <= 0f) ApplyDeadState();
        else if (prev <= 0f && next > 0f) ApplyAliveState();
    }

    public void Damage(float amount)
    {
        if (!Alive || Invulnerable || amount <= 0f) return;
        // Clients never write health — they ask the server (see PlayerNetwork.ReportHit).
        if (!HasAuthority) return;
        // Armour first. Every damage source in the game funnels through here — hitscan, splash,
        // bot contact, the void — so this is the one place it has to be applied.
        if (armour != null) amount = armour.Absorb(amount);
        hp.Value = Mathf.Max(0f, hp.Value - amount);
        lastDamageTime = Time.time;
        if (hp.Value <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (!Alive || amount <= 0f || !HasAuthority) return;
        hp.Value = Mathf.Min(MaxHp, hp.Value + amount);
    }

    void Update()
    {
        // Death, respawn timing, out-of-bounds and regen are all authority decisions; clients
        // just render the result. Without this every client would run its own respawn clock.
        if (!HasAuthority) return;

        if (!Alive)
        {
            if (Time.time >= reviveAt) Respawn();
            return;
        }

        // Out of bounds -> instant death (invuln doesn't save you from the void). Vertical:
        // fell through the pit or off an edge. Horizontal: launched past the perimeter walls.
        //
        // The map's own MapBounds wins over the fields below, which are shared by every map
        // through the player prefab — Arena's 48 would kill a player mid-arena on a 150m one.
        var bounds = MapBounds.Current;
        float limitDist = bounds != null ? bounds.killDistance : killDist;
        float limitY = bounds != null ? bounds.killY : killY;

        Vector3 p = transform.position;
        if (p.y < limitY || (limitDist > 0f && (Mathf.Abs(p.x) > limitDist || Mathf.Abs(p.z) > limitDist)))
        {
            hp.Value = 0f;
            Die();
            return;
        }

        if (regenPerSec > 0f && hp.Value < MaxHp && Time.time - lastDamageTime >= regenDelay)
            hp.Value = Mathf.Min(MaxHp, hp.Value + regenPerSec * Time.deltaTime);
    }

    // Authority-side death: schedule the revive. The visible freeze is applied by
    // ApplyDeadState via the SyncVar callback, so it happens on every client, not just here.
    void Die()
    {
        reviveAt = Time.time + respawnDelay;
        // Counted here, not in the shooter's path, so deaths with no killer — pit falls and
        // out-of-bounds — are recorded too. Runs under authority; Die() is authority-only.
        var score = GetComponent<PlayerScore>();
        if (score != null) score.AddDeath();

        // The single announce site. Every death funnels through here, so the feed cannot miss
        // one and cannot double-report it. A stale attacker resolves to null rather than being
        // credited: falling into the pit a few seconds after a firefight is not that player's
        // kill, and the feed saying otherwise would be worse than saying nothing.
        var killer = (Time.time - serverAttackerAt <= attackerMemory) ? serverAttacker : null;
        KillFeed.Announce(killer, NetworkObject,
            killer != null ? serverAttackerKind : KillKind.Normal);

        // Credit and confirm from HERE rather than the hitscan RPC, so every damage source
        // that records an attacker — hitscan, rocket splash — produces a counted kill and a
        // kill cue. Self-kills are a death with no credit; rocketing yourself into the pit
        // should cost, not pay.
        if (killer != null && killer != NetworkObject)
        {
            var killerScore = killer.GetComponent<PlayerScore>();
            if (killerScore != null) killerScore.AddKill();
            var net = killer.GetComponent<PlayerNetwork>();
            if (net != null) net.NotifyKillConfirmed();
            // Bloodrush pays out from HERE for the same reason the score does: this is the one
            // site every lethal damage source funnels through, so the heal cannot miss a kill
            // or pay twice for one. Self-kills are excluded by the check above — rocketing
            // yourself into the pit should not heal you.
            var killerHealth = killer.GetComponent<PlayerHealth>();
            if (killerHealth != null) killerHealth.RewardKillHeal();
            var match = FindAnyObjectByType<MatchManager>();
            if (match != null) match.CheckForWinner();
        }

        serverAttacker = null;
        serverAttackerAt = -999f;
        serverAttackerKind = KillKind.Normal;

        ApplyDeadState();
    }

    // Called on the KILLER by the victim's Die(), which only ever runs under authority — so
    // this is already server-side and writes the SyncVar the same way damage does. Clamped to
    // MaxHp, which reads the effective ceiling, so Bloodrush and Vitality stack correctly
    // rather than the heal overshooting into a value the next damage tick would snap back.
    //
    // No-ops without the passive, so the caller does not have to know what the killer picked.
    public void RewardKillHeal()
    {
        if (!HasAuthority || !Alive) return;
        if (passives == null || !passives.Has(PassiveType.Bloodrush)) return;
        if (bloodrushHeal <= 0f || hp.Value >= MaxHp) return;
        hp.Value = Mathf.Min(MaxHp, hp.Value + bloodrushHeal);
    }

    void Respawn()
    {
        PickSpawn(out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
        // Armour is earned per life, not banked across them — otherwise the pickup stops being
        // contested the moment one player has it.
        if (armour != null) armour.ClearOnRespawn();
        hp.Value = MaxHp; // drives ApplyAliveState everywhere through OnChange
        invulnUntil = Time.time + spawnInvuln;
    }

    // Re-picks a spawn each death rather than reusing the one cached at Awake. A fixed
    // respawn point is trivially camped: kill someone and you already know where they will
    // reappear. Chooses the point FURTHEST from the nearest living opponent, which is the
    // standard anti-camp rule and needs no extra state.
    void PickSpawn(out Vector3 pos, out Quaternion rot)
    {
        pos = spawnPos;
        rot = spawnRot;

        if (spawner == null) spawner = FindAnyObjectByType<FishNet.Component.Spawning.PlayerSpawner>();
        var points = spawner != null ? spawner.Spawns : null;
        if (points == null || points.Length == 0) return;

        // Everyone else still alive. Dead players are ignored — they are about to move anyway.
        var others = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);

        float bestScore = float.MinValue;
        foreach (var t in points)
        {
            if (t == null) continue;
            float nearest = float.MaxValue;
            foreach (var o in others)
            {
                if (o == null || o == this || !o.Alive) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(t.position, o.transform.position));
            }
            // No living opponents: every point scores equally, so the first wins — fine.
            if (nearest > bestScore)
            {
                bestScore = nearest;
                pos = t.position;
                rot = t.rotation;
            }
        }
    }

    // Runs on EVERY client through the SyncVar callback, not just the authority — which is why
    // the local countdown and the camera hand-off belong here rather than in Die().
    void ApplyDeadState()
    {
        localRespawnAt = Time.time + respawnDelay;

        // IDEMPOTENT, because this really does run twice on the authority: Die() calls it
        // directly AND the hp SyncVar's OnChange fires for the same write. The second pass
        // used to re-capture deadColsPrev/hiddenPrev from state the FIRST pass had already
        // turned off, so it remembered "these were disabled" — and ApplyAliveState then
        // faithfully restored them to disabled. The host came back from their first death
        // alive, mobile, and with no collider: rockets could not push them (which is how this
        // surfaced), and nothing could shoot them either, for the rest of the match.
        //
        // Guarding on the captured arrays rather than on Alive: these arrays ARE the record of
        // "the dead state is currently applied", and they are what must not be overwritten.
        if (deadCols != null) return;

        // Owner-only, and disabled on remote players by PlayerNetwork — so this is a no-op on
        // everyone else's copy of this player.
        if (deathCam != null && deathCam.enabled)
        {
            if (HasFreshAttacker) deathCam.Begin(lastAttackerTransform, lastAttackerPos);
            else deathCam.Begin(null, null);
        }

        // Swap the standing capsule for a falling one. Runs on every client via the SyncVar
        // callback, so everyone sees the same body drop. Renderer states are remembered
        // rather than assumed: the owner's own body is already hidden by PlayerNetwork, and
        // blindly re-enabling it on respawn would put a capsule over their camera.
        if (bodyT != null)
        {
            hiddenRends = bodyT.GetComponentsInChildren<Renderer>(true);
            hiddenPrev = new bool[hiddenRends.Length];
            for (int i = 0; i < hiddenRends.Length; i++)
            {
                hiddenPrev[i] = hiddenRends[i].enabled;
                hiddenRends[i].enabled = false;
            }
        }
        CorpseFx.Spawn(bodyT, HasFreshAttacker ? lastAttackerPos : (Vector3?)null);

        // Stop being solid. Runs on every client through the SyncVar callback, so the body
        // stops blocking shots on the shooter's machine too — which is the machine that
        // decides hits. Restored in ApplyAliveState BEFORE the motor unfreezes, so the
        // controller never steps a frame without its capsule.
        deadCols = GetComponentsInChildren<Collider>(true);
        deadColsPrev = new bool[deadCols.Length];
        for (int i = 0; i < deadCols.Length; i++)
        {
            deadColsPrev[i] = deadCols[i].enabled;
            deadCols[i].enabled = false;
        }

        if (motor == null) return;
        motor.velocity = Vector3.zero;
        motor.Frozen = true; // freeze the sim until respawn (runtime flag, never serialized)
    }

    void ApplyAliveState()
    {
        localRespawnAt = 0f;
        lastAttackerAt = -999f; // a new life does not inherit the last one's killer
        lastAttackerTransform = null;

        if (deathCam != null && deathCam.enabled) deathCam.End();

        if (hiddenRends != null)
        {
            for (int i = 0; i < hiddenRends.Length; i++)
                if (hiddenRends[i] != null) hiddenRends[i].enabled = hiddenPrev[i];
            hiddenRends = null;
            hiddenPrev = null;
        }

        // Solid again — before the motor unfreezes below, or it would step once with no capsule.
        if (deadCols != null)
        {
            for (int i = 0; i < deadCols.Length; i++)
                if (deadCols[i] != null) deadCols[i].enabled = deadColsPrev[i];
            deadCols = null;
            deadColsPrev = null;
        }

        // Fresh mags every life. Also clears a reload that was mid-flight at death — see
        // WeaponController.ResetForRespawn for the sniper trap this closes.
        var weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ResetForRespawn();

        if (motor == null) return;
        motor.velocity = Vector3.zero;
        motor.Frozen = false;
    }
}
