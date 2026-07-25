using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// A contested map resource. Optional — only active when the host enables the Pickups mode.
//
// This is the layer the arena has been missing: decks, tunnels and jump pads are traversal,
// but nothing in the map was ever WORTH going to. A health pickup on a timer turns a location
// into a decision, and because there is no regen, healing is the scarcest thing in the game.
//
// Server-authoritative: the server alone decides a collection and heals, and `available` is a
// SyncVar so every client agrees on whether it is there. Polls an overlap sphere rather than
// using triggers, matching JumpPad — the player has no rigidbody, so trigger callbacks would
// need one added purely for this.
public class Pickup : NetworkBehaviour
{
    public enum Kind { Health, Armour }

    [Header("Effect")]
    public Kind kind = Kind.Health;
    [Tooltip("HP restored, or armour granted. 50 health is a third of base HP — meaningful " +
             "without erasing a fight. 50 armour is half a full plate.")]
    public float amount = 50f;

    [Header("Timing")]
    [Tooltip("Seconds before it returns. Long enough that taking it is worth remembering, " +
             "which is what makes the spot contested rather than free. Armour should sit " +
             "LONGER than health: it is worth taking at any time, so a short timer would make " +
             "holding its spawn the whole game.")]
    public float respawnSeconds = 20f;

    [Header("Look")]
    [Tooltip("Differentiate the pickup by kind at startup. Two identical spheres that do " +
             "different things is a decision the player cannot make, because they cannot tell " +
             "which is which — and colour alone does not survive peripheral vision at speed.")]
    public bool styleByKind = true;
    public Color healthColor = new Color(0.35f, 0.95f, 0.45f);
    public Color armourColor = new Color(0.40f, 0.70f, 1.00f);

    [Tooltip("Armour is drawn larger and spun on a tilted axis so its SILHOUETTE differs, not " +
             "just its hue. Shape reads at a distance and in the corner of your eye; colour does " +
             "not, and it is the cue colour-blind players lose first.")]
    public float armourScale = 1.35f;
    public float armourTilt = 35f;

    [Header("Audio")]
    [Tooltip("Heard by EVERYONE in range, not just whoever took it. A pickup going off across " +
             "the map is information: it says the armour is gone and roughly where someone is.")]
    [Range(0f, 1f)] public float volume = 0.55f;
    public float audioRange = 34f;

    [Header("Feel")]
    public float radius = 1.6f;
    public float bobHeight = 0.25f;
    public float bobSpeed = 2f;
    public float spinSpeed = 90f;

    readonly SyncVar<bool> available = new SyncVar<bool>(true);

    Renderer[] visuals;
    Vector3 basePos;
    Vector3 baseScale;
    Vector3 spinAxis = Vector3.up;
    float readyAt;
    MatchManager match;

    AudioSource audioSrc;
    AudioClip takeClip;

    // True once THIS client has actually observed the pickup sitting there. Gates the collection
    // cue so a late joiner, whose first news of the pickup is that it is already gone, does not
    // hear a phantom pickup they were not present for.
    //
    // Deliberately NOT set from the OnChange callback: `available` starts true, so it never
    // fires for the initial value and the flag would stay false through the first collection —
    // which is the one that matters. Set from Update instead, where being available is observed
    // directly rather than inferred from a transition.
    bool everSeenAvailable;

    void Awake()
    {
        visuals = GetComponentsInChildren<Renderer>(true);
        basePos = transform.position;
        baseScale = transform.localScale;
        available.OnChange += OnAvailableChanged;
        if (styleByKind) ApplyStyle();
        BuildAudio();
    }

    void ApplyStyle()
    {
        Color c = Tint;

        if (visuals != null)
        {
            foreach (var r in visuals)
            {
                if (r == null) continue;
                var m = r.material; // instance, so two pickups sharing a material stay independent
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                m.color = c;
                // Emissive so it reads as a thing that is ON, against a deliberately dull arena.
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 1.6f);
                }
            }
        }

        // Bigger and tilted for armour. This is the cue that survives distance, motion and
        // colour blindness — the other three all rest on hue.
        if (kind == Kind.Armour)
        {
            transform.localScale = baseScale * armourScale;
            spinAxis = Quaternion.Euler(armourTilt, 0f, armourTilt) * Vector3.up;
        }
        else
        {
            spinAxis = Vector3.up;
        }
    }

    Color Tint => kind == Kind.Armour ? armourColor : healthColor;

    void OnDestroy() => available.OnChange -= OnAvailableChanged;

    void OnAvailableChanged(bool prev, bool next, bool asServer)
    {
        ApplyVisual(next);

        // Only a genuine collection makes a sound. The same SyncVar also goes false when the
        // host turns the pickups mode off, which is not a player taking anything.
        if (prev && !next && everSeenAvailable && ModeOn) PlayTaken();
    }

    // Two clips that are unmistakable from each other with no visual: health rises and is bright,
    // armour is lower and lands with a thud. Generated in code because the project has no audio
    // assets — same reason HitFeedback synthesises its blips.
    void BuildAudio()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;                       // world sound: direction and distance
        audioSrc.rolloffMode = AudioRolloffMode.Linear;
        audioSrc.minDistance = 3f;
        audioSrc.maxDistance = audioRange;

        takeClip = kind == Kind.Armour
            ? MakeTone(220f, 150f, 0.26f, 9f, 0.35f)      // falling, heavy
            : MakeTone(660f, 990f, 0.16f, 14f, 0f);       // rising, bright
    }

    void PlayTaken()
    {
        if (audioSrc != null && takeClip != null) audioSrc.PlayOneShot(takeClip, volume);
    }

    // Sine sweeping hzFrom -> hzTo over `seconds`, decaying by `decay`. `buzz` adds a second
    // voice a fifth below, which is what gives the armour cue its metallic weight.
    static AudioClip MakeTone(float hzFrom, float hzTo, float seconds, float decay, float buzz)
    {
        const int rate = 44100;
        int n = Mathf.Max(1, (int)(rate * seconds));
        var data = new float[n];
        float phase = 0f, phaseLow = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float k = t / seconds;
            float hz = Mathf.Lerp(hzFrom, hzTo, k);
            phase += 2f * Mathf.PI * hz / rate;
            phaseLow += 2f * Mathf.PI * (hz * 0.67f) / rate;
            float env = Mathf.Exp(-t * decay);
            data[i] = (Mathf.Sin(phase) + Mathf.Sin(phaseLow) * buzz) * env * 0.5f;
        }
        var clip = AudioClip.Create("pickup", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // What the renderers are currently showing, so Update can cheaply reconcile against what
    // they SHOULD be showing. Starts true because the prefab's renderers start enabled.
    bool shownNow = true;

    void ApplyVisual(bool on)
    {
        shownNow = on;
        if (visuals == null) return;
        foreach (var r in visuals) if (r != null) r.enabled = on;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyVisual(available.Value && ModeOn);
    }

    // Pickups mode is decided by the host and synced through MatchManager, so a client cannot
    // disagree about whether the resource exists.
    bool ModeOn
    {
        get
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            return match != null && match.PickupsEnabled;
        }
    }

    void Update()
    {
        bool on = ModeOn;

        // Reconcile the renderers every frame instead of only on the SyncVar edge. The edge
        // alone had a late-joiner hole: OnStartClient can run before MatchManager's
        // pickupsEnabled has synced, hide the visuals, and nothing ever turns them back on —
        // `available` never changes, so OnAvailableChanged never fires, and the pickup is
        // invisible for that client for the rest of the session ("3rd player can't see stuff").
        bool shouldShow = on && available.Value;
        if (shouldShow != shownNow) ApplyVisual(shouldShow);

        // Cosmetic idle motion, run everywhere so it looks alive on every client.
        if (on && available.Value)
        {
            everSeenAvailable = true; // observed present, so a later collection is ours to hear
            transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
            // Spins on a tilted axis for armour, so the two differ in MOTION as well as size —
            // a third cue that costs nothing and works while you are sprinting past.
            transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        }

        if (!IsServerStarted) return;

        if (!on)
        {
            if (available.Value) { available.Value = false; ApplyVisual(false); }
            return;
        }

        if (!available.Value)
        {
            if (Time.time >= readyAt) available.Value = true;
            return;
        }

        TryCollect();
    }

    void TryCollect()
    {
        var hits = Physics.OverlapSphere(basePos, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            var hp = c.GetComponentInParent<PlayerHealth>();
            if (hp == null || !hp.Alive) continue;
            if (!Grant(hp)) continue; // already full for this kind — walk over it, take nothing

            available.Value = false;
            readyAt = Time.time + respawnSeconds;
            return;
        }
    }

    // Returns false when the player cannot use it, so the pickup stays up. A resource consumed
    // for no effect is a resource the map stopped offering — and the player has no way to know
    // it happened.
    bool Grant(PlayerHealth hp)
    {
        if (kind == Kind.Armour)
        {
            var armour = hp.GetComponent<PlayerArmour>();
            return armour != null && armour.Add(amount);
        }

        if (hp.Hp >= hp.MaxHp) return false;
        hp.Heal(amount);
        return true;
    }
}
