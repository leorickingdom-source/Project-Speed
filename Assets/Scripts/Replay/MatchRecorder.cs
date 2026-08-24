using System.Collections.Generic;
using UnityEngine;

// Rolling record of the match, and the judge of which few seconds of it were the best.
//
// The expensive half of a replay system is normally the animation: you have to capture what
// every limb was doing. Not here. PlayerBody derives its entire pose from motor state —
// velocity, grounded, height, sliding — so replaying those four numbers reproduces the walk,
// the slide, the airborne tuck and the speed cutoff exactly, for free. What gets recorded is
// therefore tiny: where a player was, which way they faced, and how they were moving.
//
// It costs NO bandwidth. Every client already receives everyone's motor state through
// prediction, kills arrive at KillFeed.AddLocal on every client, and tracers arrive at
// TracerRenderer.Show on every client. So each machine records what it already knows and
// replays it locally. The honest limit of that is stated in ReplayPlayer: this is the recording
// client's VIEW of the match, not server ground truth, so two players can see slightly
// different versions of the same play.
public class MatchRecorder : MonoBehaviour
{
    public const int MaxPlayers = 12;

    [Tooltip("Seconds of match held in the rolling buffer. Only has to outlast one candidate " +
             "window — a highlight is copied out of the ring the moment its post-roll has " +
             "elapsed, so this is not how far back a good play can be remembered.")]
    public float bufferSeconds = 20f;
    [Tooltip("Seconds captured BEFORE the kill that triggered the highlight — the approach, " +
             "which is usually the part worth watching.")]
    public float preRoll = 6f;
    [Tooltip("Seconds captured after it, so the clip does not cut on the kill itself.")]
    public float postRoll = 2.5f;
    [Tooltip("Samples per second. The replay interpolates between them, so this trades memory " +
             "against how sharp a fast direction change looks. 30 is well under the tick rate.")]
    public float sampleRate = 30f;

    [Header("What counts as a good play")]
    public float headshotWeight = 1.6f;
    public float meleeWeight = 2.2f;
    [Tooltip("Multiplier for a kill scored while off the ground. Rewards the thing this game is " +
             "actually about — arriving somewhere by rocket or rope and killing on the way past.")]
    public float airborneWeight = 1.5f;

    public struct BodySample
    {
        // EntityId, not int: Unity 6 deprecated the int form and it is only ever used as an
        // identity key here, never as a number.
        public EntityId id;
        public Vector3 pos;
        public float yaw;
        public Vector3 vel;
        public bool grounded;
        public bool sliding;
        public float height;
    }

    public struct ShotEvent { public float t; public Vector3 from, to; public Color col; }

    struct KillEvent { public float t; public EntityId killerId; public KillKind kind; public bool airborne; }
    struct Candidate { public float centre; public float readyAt; }

    // Preallocated and reused on every lap of the ring, so a match that runs for ten minutes
    // allocates exactly as much as one that runs for ten seconds.
    public class Frame
    {
        public float time;
        public int count;
        public BodySample[] samples = new BodySample[MaxPlayers];
    }

    public class Clip
    {
        public List<Frame> frames = new List<Frame>();
        public List<ShotEvent> shots = new List<ShotEvent>();
        public EntityId starId;
        public string starName = "";
        public Color starTint = Color.white;
        public float score;
        public int kills;
        public float Start => frames.Count > 0 ? frames[0].time : 0f;
        public float End => frames.Count > 0 ? frames[frames.Count - 1].time : 0f;
    }

    public static MatchRecorder Instance { get; private set; }

    Frame[] ring;
    int head = -1, filled;
    float nextSampleAt;
    readonly List<ShotEvent> shots = new List<ShotEvent>();
    readonly List<KillEvent> kills = new List<KillEvent>();
    readonly List<Candidate> pending = new List<Candidate>();

    readonly List<PlayerMotor> motors = new List<PlayerMotor>();
    float nextScanAt;

    // Identity captured live, because by the time a clip plays the player may have disconnected
    // and their object be gone — the whole reason playback uses ghosts rather than the real ones.
    readonly Dictionary<EntityId, string> names = new Dictionary<EntityId, string>();
    readonly Dictionary<EntityId, Color> tints = new Dictionary<EntityId, Color>();

    public Clip Best { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        int cap = Mathf.Max(8, Mathf.CeilToInt(bufferSeconds * sampleRate));
        ring = new Frame[cap];
        for (int i = 0; i < cap; i++) ring[i] = new Frame();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Created on demand so no scene has to remember to place it — the same pattern ImpactFx uses.
    public static MatchRecorder Ensure()
    {
        if (Instance != null) return Instance;
        var found = FindAnyObjectByType<MatchRecorder>();
        if (found != null) return Instance = found;
        return new GameObject("MatchRecorder").AddComponent<MatchRecorder>();
    }

    // LateUpdate: after the motors have moved for the frame, so a sample is never half a step
    // behind the body it describes.
    void LateUpdate()
    {
        if (Time.time >= nextScanAt) { Rescan(); nextScanAt = Time.time + 0.5f; }
        if (Time.time >= nextSampleAt) { Capture(); nextSampleAt = Time.time + 1f / Mathf.Max(1f, sampleRate); }
        Materialise();
        Trim();
    }

    void Rescan()
    {
        motors.Clear();
        motors.AddRange(FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None));
        foreach (var m in motors)
        {
            if (m == null) continue;
            var id = m.transform.GetEntityId();
            var ident = m.GetComponent<PlayerIdentity>();
            var net = m.GetComponent<PlayerNetwork>();
            names[id] = ident != null ? ident.Name : "Player";
            tints[id] = net != null ? PlayerColors.For(net.OwnerId) : Color.grey;
        }
    }

    void Capture()
    {
        head = (head + 1) % ring.Length;
        if (filled < ring.Length) filled++;
        var f = ring[head];
        f.time = Time.time;
        f.count = 0;
        foreach (var m in motors)
        {
            if (m == null || f.count >= MaxPlayers) continue;
            f.samples[f.count++] = new BodySample
            {
                id = m.transform.GetEntityId(),
                pos = m.transform.position,
                yaw = m.transform.eulerAngles.y,
                vel = m.velocity,
                grounded = m.grounded,
                sliding = m.sliding,
                height = m.height,
            };
        }
    }

    // ---- events, called from the two places every client already learns about them ----

    public static void RecordShot(Vector3 from, Vector3 to, Color col)
    {
        var r = Instance;
        if (r == null) return;
        r.shots.Add(new ShotEvent { t = Time.time, from = from, to = to, col = col });
    }

    public static void RecordKill(Transform killer, KillKind kind)
    {
        var r = Instance;
        if (r == null || killer == null) return;
        var motor = killer.GetComponentInParent<PlayerMotor>();
        r.kills.Add(new KillEvent
        {
            t = Time.time,
            killerId = motor != null ? motor.transform.GetEntityId() : killer.GetEntityId(),
            kind = kind,
            airborne = motor != null && !motor.grounded,
        });
        // A kill opens a candidate window. It cannot be judged yet — the post-roll has not
        // happened — so it is queued and evaluated once it has.
        r.pending.Add(new Candidate { centre = Time.time, readyAt = Time.time + r.postRoll });
    }

    // ---- scoring ----

    void Materialise()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (Time.time < pending[i].readyAt) continue;
            Evaluate(pending[i].centre);
            pending.RemoveAt(i);
        }
    }

    void Evaluate(float centre)
    {
        float a = centre - preRoll, b = centre + postRoll;

        // Best single player inside the window, not the busiest window overall: a play of the
        // game is one person's, and two players trading kills across the map is not a highlight.
        var tally = new Dictionary<EntityId, float>();
        var counts = new Dictionary<EntityId, int>();
        foreach (var k in kills)
        {
            if (k.t < a || k.t > b) continue;
            float w = 1f;
            if (k.kind == KillKind.Headshot) w *= headshotWeight;
            if (k.kind == KillKind.Melee) w *= meleeWeight;
            if (k.airborne) w *= airborneWeight;
            tally.TryGetValue(k.killerId, out float cur);
            tally[k.killerId] = cur + w;
            counts.TryGetValue(k.killerId, out int c);
            counts[k.killerId] = c + 1;
        }
        if (tally.Count == 0) return;

        EntityId starId = default; float best = 0f;
        foreach (var kv in tally) if (kv.Value > best) { best = kv.Value; starId = kv.Key; }
        if (Best != null && best <= Best.score) return;   // the reigning clip is still better

        var clip = new Clip { starId = starId, score = best, kills = counts[starId] };
        names.TryGetValue(starId, out string n); clip.starName = string.IsNullOrEmpty(n) ? "Player" : n;
        tints.TryGetValue(starId, out Color t); clip.starTint = t == default ? Color.white : t;

        // Copied OUT of the ring, so the clip survives the buffer wrapping past it. This is what
        // lets a great play in the second minute still win at the end of a ten minute match.
        for (int i = 0; i < filled; i++)
        {
            var f = ring[(head - i + ring.Length) % ring.Length];
            if (f.time < a || f.time > b) continue;
            var copy = new Frame { time = f.time, count = f.count };
            System.Array.Copy(f.samples, copy.samples, f.count);
            clip.frames.Add(copy);
        }
        clip.frames.Sort((x, y) => x.time.CompareTo(y.time));
        foreach (var s in shots) if (s.t >= a && s.t <= b) clip.shots.Add(s);
        if (clip.frames.Count < 2) return;   // nothing to interpolate between

        Best = clip;
    }

    // Events outlive the ring only as far as the longest window needs them.
    void Trim()
    {
        float cutoff = Time.time - (bufferSeconds + postRoll);
        shots.RemoveAll(s => s.t < cutoff);
        kills.RemoveAll(k => k.t < cutoff);
    }

}
