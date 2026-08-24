using UnityEngine;
using System.Collections.Generic;

// Plays a recorded clip back as PLAY OF THE GAME at the end of a match.
//
// Playback drives ghosts, not the real players. Two reasons, and the second is the one that
// forces it: the live objects are still simulating and would fight every write, and the player
// who earned the highlight may already have disconnected — their object gone, which is exactly
// when you most want to show what they did. So each recorded body is rebuilt from scratch.
//
// The ghost is the same trick PlayerBodyPreview uses: a DISABLED PlayerMotor is just a bag of
// public fields, and PlayerBody reads nothing else. Set position, yaw, velocity, grounded,
// height and sliding, and the full clip set replays itself — walk, run, backpedal, slide,
// airborne, the speed cutoff. No animation was ever recorded because none needed to be.
//
// Honest limit: this is the recording client's VIEW of the match. Remote players arrived
// predicted and interpolated, so two players can see slightly different versions of the same
// play. Making it identical for everyone means server-authoritative recording, which costs
// bandwidth this deliberately does not spend.
public class ReplayPlayer : MonoBehaviour
{
    [Tooltip("How far behind the highlighted player the camera sits.")]
    public float distance = 4.5f;
    public float height = 1.8f;
    [Tooltip("How fast the camera catches up. Low enough to lag slightly behind a fast player, " +
             "which is what makes speed read on camera rather than looking like a locked mount.")]
    public float follow = 6f;
    [Tooltip("Playback rate. Slightly under 1 gives the eye time to read a fast play without " +
             "turning it into slow motion.")]
    public float rate = 0.85f;
    public LayerMask blockMask = ~0;

    public static ReplayPlayer Instance { get; private set; }
    public bool Playing { get; private set; }

    MatchRecorder.Clip clip;
    float t;
    Camera cam;
    TracerRenderer tracers;
    int nextShot;
    readonly Dictionary<EntityId, Transform> ghosts = new Dictionary<EntityId, Transform>();
    readonly Dictionary<EntityId, PlayerMotor> ghostMotors = new Dictionary<EntityId, PlayerMotor>();
    readonly List<Camera> silenced = new List<Camera>();
    readonly List<AudioListener> mutedListeners = new List<AudioListener>();
    GameObject rig;
    GUIStyle bannerStyle, subStyle;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        blockMask &= ~(1 << PlayerBody.HitboxLayer);
        blockMask &= ~(1 << 2);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public static ReplayPlayer Ensure()
    {
        if (Instance != null) return Instance;
        var found = FindAnyObjectByType<ReplayPlayer>();
        if (found != null) return Instance = found;
        return new GameObject("ReplayPlayer").AddComponent<ReplayPlayer>();
    }

    public void Play(MatchRecorder.Clip c)
    {
        if (Playing || c == null || c.frames.Count < 2) return;
        clip = c;
        Playing = true;
        t = c.Start;
        nextShot = 0;

        rig = new GameObject("ReplayRig");
        BuildGhosts();

        // The live world keeps simulating underneath — stopping it would desync a match that may
        // still be settling its final score. It is simply not rendered or heard.
        foreach (var c2 in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c2.enabled && c2.gameObject.activeInHierarchy) { c2.enabled = false; silenced.Add(c2); }
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (l.enabled) { l.enabled = false; mutedListeners.Add(l); }

        var camGo = new GameObject("ReplayCamera");
        camGo.transform.SetParent(rig.transform, false);
        cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        camGo.AddComponent<AudioListener>();
        tracers = rig.AddComponent<TracerRenderer>();
    }

    public void Stop()
    {
        if (!Playing) return;
        Playing = false;
        foreach (var c2 in silenced) if (c2 != null) c2.enabled = true;
        silenced.Clear();
        foreach (var l in mutedListeners) if (l != null) l.enabled = true;
        mutedListeners.Clear();
        ghosts.Clear(); ghostMotors.Clear();
        if (rig != null) Destroy(rig);
    }

    void BuildGhosts()
    {
        var seen = new HashSet<EntityId>();
        foreach (var f in clip.frames)
            for (int i = 0; i < f.count; i++) seen.Add(f.samples[i].id);

        foreach (var id in seen)
        {
            var go = new GameObject("Ghost_" + id);
            go.transform.SetParent(rig.transform, false);
            var motor = go.AddComponent<PlayerMotor>();
            motor.enabled = false;                 // a bag of fields, nothing more
            ghostMotors[id] = motor;
            // No hitboxes: nothing shoots a replay, and colliders inside a ghost would only give
            // the live world something new to trip over.
            PlayerBody.Attach(go.transform, id == clip.starId ? clip.starTint : Color.grey,
                              true, hitboxes: false);
            ghosts[id] = go.transform;
        }
    }

    void LateUpdate()
    {
        if (!Playing) return;
        t += Time.deltaTime * Mathf.Max(0.05f, rate);

        if (t > clip.End) { Stop(); return; }

        // Bracket the current time and interpolate. Samples are 30/s; a player crossing the
        // arena covers most of a metre between two of them, so stepping frame-to-frame instead
        // of interpolating would visibly stutter exactly on the fast plays worth watching.
        MatchRecorder.Frame a = clip.frames[0], b = clip.frames[0];
        for (int i = 0; i < clip.frames.Count - 1; i++)
        {
            if (clip.frames[i].time <= t && clip.frames[i + 1].time >= t)
            { a = clip.frames[i]; b = clip.frames[i + 1]; break; }
        }
        float span = Mathf.Max(0.0001f, b.time - a.time);
        float k = Mathf.Clamp01((t - a.time) / span);

        foreach (var kv in ghosts) kv.Value.gameObject.SetActive(false);

        for (int i = 0; i < a.count; i++)
        {
            var sa = a.samples[i];
            if (!ghosts.TryGetValue(sa.id, out var tr)) continue;
            var sb = sa;
            for (int j = 0; j < b.count; j++) if (b.samples[j].id == sa.id) { sb = b.samples[j]; break; }

            tr.gameObject.SetActive(true);
            tr.position = Vector3.Lerp(sa.pos, sb.pos, k);
            tr.rotation = Quaternion.Slerp(Quaternion.Euler(0f, sa.yaw, 0f),
                                           Quaternion.Euler(0f, sb.yaw, 0f), k);
            var m = ghostMotors[sa.id];
            m.velocity = Vector3.Lerp(sa.vel, sb.vel, k);
            m.grounded = sa.grounded;
            m.sliding = sa.sliding;
            m.height = Mathf.Lerp(sa.height, sb.height, k);
        }

        FireShotsUpTo(t);
        AimCamera();
    }

    // Tracers are replayed as events rather than sampled, because a shot is instantaneous — a
    // sampled version would either miss it entirely or smear it across two frames.
    void FireShotsUpTo(float now)
    {
        while (nextShot < clip.shots.Count && clip.shots[nextShot].t <= now)
        {
            var s = clip.shots[nextShot++];
            if (tracers != null) tracers.Show(s.from, s.to, s.col, 0.12f);
        }
    }

    void AimCamera()
    {
        if (cam == null || !ghosts.TryGetValue(clip.starId, out var star) || star == null) return;
        Vector3 focus = star.position + Vector3.up * 1.2f;
        Vector3 want = focus - star.forward * distance + Vector3.up * height;

        Vector3 dir = want - focus;
        float dist = dir.magnitude;
        if (dist > 0.01f && Physics.SphereCast(focus, 0.3f, dir / dist, out RaycastHit hit,
                                               dist, blockMask, QueryTriggerInteraction.Ignore))
            want = focus + dir / dist * Mathf.Max(0.5f, hit.distance - 0.1f);

        var ct = cam.transform;
        ct.position = Vector3.Lerp(ct.position, want, 1f - Mathf.Exp(-follow * Time.deltaTime));
        ct.rotation = Quaternion.Slerp(ct.rotation, Quaternion.LookRotation(focus - ct.position),
                                       1f - Mathf.Exp(-follow * 1.5f * Time.deltaTime));
    }

    void OnGUI()
    {
        if (!Playing || clip == null) return;
        if (bannerStyle == null)
        {
            bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.UpperCenter };
            subStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperCenter };
        }
        bannerStyle.normal.textColor = Color.white;
        subStyle.normal.textColor = clip.starTint;
        GUI.Label(new Rect(0f, 28f, Screen.width, 44f), "PLAY OF THE GAME", bannerStyle);
        GUI.Label(new Rect(0f, 74f, Screen.width, 28f),
            clip.starName + "   ·   " + clip.kills + (clip.kills == 1 ? " kill" : " kills"), subStyle);
    }
}
