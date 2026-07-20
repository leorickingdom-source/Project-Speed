using UnityEngine;

// Placeholder audio generated in code. The project ships no audio assets, and waiting for
// real ones would mean shipping a movement shooter with no footsteps — which is not a polish
// gap but an information gap: sound is how you locate someone you cannot see.
//
// Everything here is deliberately crude and cheap. Swap for real clips later; the call sites
// in PlayerAudio will not change.
public static class ProceduralAudio
{
    const int Rate = 44100;

    static AudioClip Make(string name, int samples, System.Func<float, int, float> shape)
    {
        var data = new float[samples];
        for (int i = 0; i < samples; i++) data[i] = shape(i / (float)Rate, i);
        var clip = AudioClip.Create(name, samples, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Decaying sine. Clean and tonal — reads as a UI or mechanical cue.
    public static AudioClip Tone(string name, float hz, float seconds, float decay, float gain = 0.6f)
        => Make(name, Mathf.Max(1, (int)(Rate * seconds)),
            (t, i) => Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * decay) * gain);

    // Decaying noise. Reads as impact, scuff, or blast depending on decay and how it's filtered.
    public static AudioClip Noise(string name, float seconds, float decay, float gain = 0.5f)
    {
        var rng = new System.Random(name.GetHashCode()); // deterministic per sound
        float last = 0f;
        return Make(name, Mathf.Max(1, (int)(Rate * seconds)), (t, i) =>
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            // One-pole lowpass so it reads as a body/thud rather than a hiss.
            last = Mathf.Lerp(last, white, 0.35f);
            return last * Mathf.Exp(-t * decay) * gain;
        });
    }

    // Frequency sweep — rising reads as effort or launch, falling as release.
    public static AudioClip Sweep(string name, float fromHz, float toHz, float seconds,
        float decay, float gain = 0.5f)
    {
        int n = Mathf.Max(1, (int)(Rate * seconds));
        float phase = 0f;
        return Make(name, n, (t, i) =>
        {
            float k = n <= 1 ? 0f : i / (float)(n - 1);
            float hz = Mathf.Lerp(fromHz, toHz, k);
            phase += 2f * Mathf.PI * hz / Rate;
            return Mathf.Sin(phase) * Mathf.Exp(-t * decay) * gain;
        });
    }
}
