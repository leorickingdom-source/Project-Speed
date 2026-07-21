using UnityEngine;

// Tells the SHOOTER that a shot landed, and tells the VICTIM that they are being shot and from
// where. Without the first, the only evidence a hit happened is a number on the victim's screen,
// which you cannot see — you are aiming blind, and no damage value can be judged because you
// cannot perceive it landing.
//
// Owner-only: remote players have this disabled by PlayerNetwork along with their camera.
//
// Sounds are generated in code rather than loaded from assets, because the project has no
// audio assets at all. A decaying sine blip is crude but it is immediate, needs no import
// pipeline, and it is the information that matters — not the fidelity.
public class HitFeedback : MonoBehaviour
{
    [Header("Marker")]
    [Tooltip("Seconds the hitmarker stays on screen.")]
    public float markerTime = 0.12f;
    public float killMarkerTime = 0.35f;
    [Tooltip("Distance in pixels from crosshair centre to each marker tick.")]
    public float markerSpread = 10f;
    public float markerLength = 6f;
    public float markerThickness = 2f;
    public Color hitColor = new Color(1f, 1f, 1f, 0.95f);
    public Color killColor = new Color(1f, 0.35f, 0.3f, 1f);

    [Header("Audio")]
    [Range(0f, 1f)] public float volume = 0.35f;
    [Tooltip("Hit blip pitch (Hz). The kill blip drops below this so the two never blur.")]
    public float hitHz = 1000f;
    public float killHz = 420f;

    [Header("Damage direction")]
    [Tooltip("Seconds an incoming-damage wedge stays up.")]
    public float damageIndicatorTime = 1.1f;
    public Color damageColor = new Color(1f, 0.25f, 0.2f, 0.9f);
    [Tooltip("Two hits landing from world positions closer together than this are treated as " +
             "the same attacker: they refresh one wedge instead of consuming two slots.")]
    public float sameAttackerRadius = 6f;

    [Header("Damage screen effect")]
    [Tooltip("Seconds the hurt flash takes to fade out.")]
    public float hurtFlashTime = 0.55f;
    public Color hurtColor = new Color(0.72f, 0.04f, 0.03f, 1f);
    [Range(0f, 1f)] public float hurtMaxAlpha = 0.62f;
    [Tooltip("Damage in one hit that produces a full-strength flash. Below the sniper body " +
             "shot (100) on purpose, so an ordinary hit still reads as a real hit.")]
    public float hurtFullDamage = 55f;
    [Tooltip("Health fraction under which the screen keeps a slow red pulse.")]
    [Range(0f, 1f)] public float lowHealthFraction = 0.3f;
    [Range(0f, 1f)] public float lowHealthAlpha = 0.4f;
    public float lowHealthPulseHz = 0.9f;

    // Wedges live at once. Six is well past how many people can plausibly be shooting you at
    // the same instant, and the merge below means one persistent attacker only ever holds one.
    const int MaxMarks = 6;

    struct DamageMark
    {
        public Vector3 from;
        public float until;
    }

    readonly DamageMark[] marks = new DamageMark[MaxMarks];

    AudioSource audioSrc;
    AudioClip hitClip, killClip;
    float markerUntil;
    bool lastWasKill;
    Texture2D pixel;
    Texture2D vignette;

    PlayerHealth health;
    Camera cam;
    float lastHp = -1f;
    float hurtStrength;   // 0..1, how hard the last flash started
    float hurtUntil;

    void Awake()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f; // 2D — this is UI feedback, not a world sound

        hitClip = MakeBlip(hitHz, 0.06f, 60f);
        killClip = MakeBlip(killHz, 0.18f, 18f);

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();

        vignette = MakeVignette(96, 0.25f, 1.7f);

        health = GetComponent<PlayerHealth>();
        cam = GetComponentInChildren<Camera>();
    }

    void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
        if (vignette != null) Destroy(vignette);
    }

    // Called by WeaponController the moment a damaging hit is detected locally. Fired on the
    // shooter's own machine on purpose: waiting for the server to confirm would add a full
    // round-trip of delay to the one cue that has to feel instant.
    public void ShowHit()
    {
        markerUntil = Time.time + markerTime;
        lastWasKill = false;
        if (audioSrc != null && hitClip != null) audioSrc.PlayOneShot(hitClip, volume);
    }

    // Called from the server's confirmation (see PlayerNetwork.ConfirmKill) — the shooter
    // cannot know locally whether the target actually died, since health is server-owned.
    public void ShowKill()
    {
        markerUntil = Time.time + killMarkerTime;
        lastWasKill = true;
        if (audioSrc != null && killClip != null) audioSrc.PlayOneShot(killClip, volume);
    }

    // Decaying sine. `decay` is how fast it dies away — higher is a shorter, sharper click.
    static AudioClip MakeBlip(float hz, float seconds, float decay)
    {
        const int rate = 44100;
        int n = Mathf.Max(1, (int)(rate * seconds));
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * decay) * 0.6f;
        }
        var clip = AudioClip.Create("blip", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Radial alpha ramp, white so GUI.color can tint it to whatever the effect wants. Built in
    // code for the same reason the blips are: there is no art pipeline in this project, and a
    // 96x96 gradient stretched over the screen is indistinguishable from an authored one.
    static Texture2D MakeVignette(int size, float inner, float power)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float corner = Mathf.Sqrt(2f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny) / corner; // 0 at centre, 1 at a corner
                float a = Mathf.Pow(Mathf.Clamp01((d - inner) / Mathf.Max(0.001f, 1f - inner)), power);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // Health is polled rather than pushed. The alternative — firing the flash from the damage
    // RPC — misses every source that does not go through it: pit falls, out-of-bounds, splash,
    // bots, and the whole offline case where nothing is networked at all. A float compare per
    // frame covers all of them with one code path.
    void Update()
    {
        if (health == null) return;

        float hp = health.Hp;
        if (lastHp < 0f) { lastHp = hp; return; } // first frame: nothing to compare against

        if (hp < lastHp - 0.01f)
        {
            float taken = lastHp - hp;
            // Take the stronger of the new hit and whatever is still on screen. A second hit
            // during the fade must never make the screen LESS red than it already was.
            hurtStrength = Mathf.Max(CurrentHurt01(),
                Mathf.Clamp01(taken / Mathf.Max(1f, hurtFullDamage)));
            hurtUntil = Time.time + hurtFlashTime;
        }

        lastHp = hp;
    }

    float CurrentHurt01()
    {
        if (Time.time >= hurtUntil) return 0f;
        return hurtStrength * Mathf.Clamp01((hurtUntil - Time.time) / Mathf.Max(0.01f, hurtFlashTime));
    }

    // Called from the server's damage report. Stores where the shot came from so the wedge can
    // be drawn relative to wherever the player is looking at the time, not where they looked
    // when hit — otherwise turning would leave the indicator pointing at nothing.
    //
    // Keeps several at once. One slot meant the second attacker erased the first, so being
    // crossfired — the exact case where knowing the directions decides whether you live —
    // showed you only whichever of them happened to shoot most recently.
    public void ShowDamageFrom(Vector3 worldPos)
    {
        float now = Time.time;
        float mergeSqr = sameAttackerRadius * sameAttackerRadius;
        int oldest = 0;

        for (int i = 0; i < marks.Length; i++)
        {
            // Same attacker still on screen: refresh in place. Without this a single enemy
            // firing an automatic weapon fills every slot with copies of one direction and
            // pushes a genuine second attacker straight back out.
            if (marks[i].until > now && (marks[i].from - worldPos).sqrMagnitude <= mergeSqr)
            {
                marks[i].from = worldPos;
                marks[i].until = now + damageIndicatorTime;
                return;
            }

            // Expired slots have until < now, so this naturally prefers a free one.
            if (marks[i].until < marks[oldest].until) oldest = i;
        }

        marks[oldest].from = worldPos;
        marks[oldest].until = now + damageIndicatorTime;
    }

    // A wedge per live attacker, offset from the crosshair in their direction. Signed angle
    // against the camera's forward, so each stays correct as you turn to face them.
    void DrawDamageDirections()
    {
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) return;
        fwd.Normalize();

        float now = Time.time;
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        float radius = Mathf.Min(Screen.width, Screen.height) * 0.22f;

        for (int i = 0; i < marks.Length; i++)
        {
            if (marks[i].until <= now) continue;

            Vector3 to = marks[i].from - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) continue;

            float angle = Vector3.SignedAngle(fwd, to.normalized, Vector3.up);
            float fade = Mathf.Clamp01((marks[i].until - now) / Mathf.Max(0.01f, damageIndicatorTime));

            float rad = angle * Mathf.Deg2Rad;
            float x = cx + Mathf.Sin(rad) * radius;
            float y = cy - Mathf.Cos(rad) * radius;

            var c = damageColor;
            c.a *= fade;
            GUI.color = c;

            // Simple thick bar, rotated to sit tangential to the ring around the crosshair.
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - 26f, y - 4f, 52f, 8f), pixel);
            GUI.matrix = m;
        }

        GUI.color = Color.white;
    }

    // Red at the edges: a hit flash that fades, plus a slow pulse while critically low.
    //
    // The direction wedges say WHERE, but nothing said THAT — at a glance the HUD number is the
    // only sign you are being killed, and it sits in the bottom corner while you are looking at
    // the crosshair. Edge-weighted rather than a full-screen wash so it registers peripherally
    // without covering the thing you are aiming at.
    void DrawDamageVignette()
    {
        float a = CurrentHurt01() * hurtMaxAlpha;

        if (health != null && health.Alive && health.MaxHp > 0f)
        {
            float frac = health.Hp / health.MaxHp;
            if (frac < lowHealthFraction)
            {
                // Deepens as health falls, so "nearly dead" and "just under the line" do not
                // look the same. Pulsing, because a constant tint stops being visible.
                float depth = Mathf.InverseLerp(lowHealthFraction, 0f, frac);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * lowHealthPulseHz * 2f * Mathf.PI);
                a = Mathf.Max(a, depth * lowHealthAlpha * pulse);
            }
        }

        if (a <= 0.002f || vignette == null) return;

        var c = hurtColor;
        c.a = a;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        if (GameMenu.IsPaused || KeybindsUI.Open) return;

        DrawDamageVignette();   // behind everything else — it is atmosphere, not information
        DrawDamageDirections();

        if (Time.time > markerUntil) return;

        // Four ticks angling out from the crosshair — the classic shape, and readable against
        // any background because it is offset from centre rather than drawn over it.
        GUI.color = lastWasKill ? killColor : hitColor;
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        float s = markerSpread, L = markerLength, T = markerThickness;

        GUI.DrawTexture(new Rect(cx - s - L, cy - s - L, L, T), pixel); // top-left
        GUI.DrawTexture(new Rect(cx - s - L, cy - s - L, T, L), pixel);
        GUI.DrawTexture(new Rect(cx + s, cy - s - L, L, T), pixel);     // top-right
        GUI.DrawTexture(new Rect(cx + s + L - T, cy - s - L, T, L), pixel);
        GUI.DrawTexture(new Rect(cx - s - L, cy + s + L - T, L, T), pixel); // bottom-left
        GUI.DrawTexture(new Rect(cx - s - L, cy + s, T, L), pixel);
        GUI.DrawTexture(new Rect(cx + s, cy + s + L - T, L, T), pixel);  // bottom-right
        GUI.DrawTexture(new Rect(cx + s + L - T, cy + s, T, L), pixel);
        GUI.color = Color.white;
    }
}
