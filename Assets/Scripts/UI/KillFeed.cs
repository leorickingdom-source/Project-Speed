using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

// "Leoric killed Blue" in the corner. Impossible before names existed; nearly free now.
//
// A deathmatch you are not personally in is currently invisible: the scoreboard tells you the
// totals but never the events, so you cannot tell whether the leader is farming one player, who
// is fighting where, or that the person who just killed you has killed three others this minute.
// The feed is how a player reads a match rather than just its result.
//
// Server decides, everyone renders. Announce is the SINGLE entry point — PlayerHealth.Die calls
// it for every death including pit falls, and PlayerNetwork calls it for bots, so there is one
// place that can be wrong rather than three.
public class KillFeed : NetworkBehaviour
{
    [Tooltip("Rows kept on screen. Older ones drop off the bottom.")]
    public int maxEntries = 5;
    [Tooltip("Seconds a row survives. Long enough to read after a fight, short enough that the " +
             "corner is empty again by the next one.")]
    public float entryLifetime = 6f;

    public int fontSize = 18;
    public float topMargin = 12f;
    public float rightMargin = 12f;
    public float rowHeight = 26f;

    struct Entry
    {
        public string killer;      // null for an environment death
        public string victim;
        public KillKind kind;      // how the killing hit landed
        public Color killerTint;
        public Color victimTint;
        public float until;
    }

    readonly List<Entry> entries = new List<Entry>();

    static KillFeed instance;
    GUIStyle style;

    void Awake() => instance = this;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // Called on the SERVER (or by whoever has authority offline). killer may be null: falling
    // into the pit is a death with no killer, and pretending otherwise would credit whoever
    // happened to shoot last. `kind` says HOW — a headshot or a melee execution is an
    // achievement, and the feed is the one place everyone in the match sees it.
    public static void Announce(NetworkObject killer, NetworkObject victim,
        KillKind kind = KillKind.Normal)
    {
        if (victim == null) return;
        if (instance == null) instance = FindAnyObjectByType<KillFeed>();
        if (instance == null) return;

        // Offline there is nobody to broadcast to, so the local list is the whole feed. This is
        // what makes the feed work in single-player practice against bots.
        if (!instance.IsSpawned || !instance.IsServerStarted)
        {
            instance.AddLocal(killer, victim, kind);
            return;
        }

        instance.BroadcastKill(killer, victim, kind);
    }

    // NetworkObject references rather than strings: the name is already on the victim's and
    // killer's PlayerIdentity, so sending it again would be the same fact twice on the wire and
    // would go stale the moment somebody renames mid-match.
    [ObserversRpc]
    void BroadcastKill(NetworkObject killer, NetworkObject victim, KillKind kind) =>
        AddLocal(killer, victim, kind);

    void AddLocal(NetworkObject killer, NetworkObject victim, KillKind kind)
    {
        if (victim == null) return;

        entries.Add(new Entry
        {
            killer = killer != null ? NameOf(killer) : null,
            victim = NameOf(victim),
            kind = killer != null ? kind : KillKind.Normal,
            killerTint = killer != null ? TintOf(killer) : Color.grey,
            victimTint = TintOf(victim),
            until = Time.time + entryLifetime,
        });

        while (entries.Count > maxEntries) entries.RemoveAt(0);
    }

    // Players carry a PlayerIdentity; bots and anything else fall back to the object name, so a
    // practice session against bots still reads properly.
    static string NameOf(NetworkObject nob)
    {
        var id = nob.GetComponent<PlayerIdentity>();
        return id != null ? id.Name : nob.gameObject.name;
    }

    static Color TintOf(NetworkObject nob)
    {
        var id = nob.GetComponent<PlayerIdentity>();
        return id != null ? id.Tint : new Color(0.75f, 0.75f, 0.78f);
    }

    void Update()
    {
        float now = Time.time;
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].until <= now) entries.RemoveAt(i);
    }

    void OnGUI()
    {
        if (entries.Count == 0 || GameMenu.IsPaused || KeybindsUI.Open) return;

        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleRight };

        float y = topMargin;
        foreach (var e in entries)
        {
            // Fades out over the last second so a row leaves rather than vanishing mid-read.
            float alpha = Mathf.Clamp01(e.until - Time.time);
            float x = Screen.width - rightMargin - 400f;

            // Drawn right-to-left in pieces so the killer and victim keep their own colours —
            // one flat string would lose the thing that makes a feed scannable at a glance.
            // Each kill kind gets its own verb: a symbol would need a legend, the word does not.
            string victim = e.victim;
            string verb = e.killer == null ? " died"
                        : e.kind == KillKind.Headshot ? " headshot "
                        : e.kind == KillKind.Melee ? " meleed "
                        : " killed ";
            string killer = e.killer ?? "";

            var vs = new GUIStyle(style); vs.normal.textColor = Fade(e.victimTint, alpha);
            var ks = new GUIStyle(style); ks.normal.textColor = Fade(e.killerTint, alpha);
            var ns = new GUIStyle(style);
            // Coloured verb on the special kills, so the row pops even when you only catch it
            // peripherally: gold for a headshot, red for a melee execution.
            Color verbTint = e.kind == KillKind.Headshot ? new Color(1f, 0.85f, 0.35f)
                           : e.kind == KillKind.Melee ? new Color(1f, 0.45f, 0.4f)
                           : new Color(1f, 1f, 1f, 0.75f);
            ns.normal.textColor = Fade(verbTint, alpha);

            float right = Screen.width - rightMargin;
            float vw = vs.CalcSize(new GUIContent(victim)).x;
            float nw = ns.CalcSize(new GUIContent(verb)).x;
            float kw = e.killer != null ? ks.CalcSize(new GUIContent(killer)).x : 0f;

            GUI.Label(new Rect(right - vw, y, vw, rowHeight), victim, vs);
            GUI.Label(new Rect(right - vw - nw, y, nw, rowHeight), verb, ns);
            if (e.killer != null)
                GUI.Label(new Rect(right - vw - nw - kw, y, kw, rowHeight), killer, ks);

            y += rowHeight;
        }
    }

    static Color Fade(Color c, float a)
    {
        c.a *= a;
        return c;
    }
}
