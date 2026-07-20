using UnityEngine;

// Tells the SHOOTER that a shot landed. Without this the only evidence a hit happened is a
// number on the victim's screen, which you cannot see — you are aiming blind, and no damage
// value can be judged because you cannot perceive it landing.
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

    AudioSource audioSrc;
    AudioClip hitClip, killClip;
    float markerUntil;
    bool lastWasKill;
    Texture2D pixel;

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

    void OnGUI()
    {
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
