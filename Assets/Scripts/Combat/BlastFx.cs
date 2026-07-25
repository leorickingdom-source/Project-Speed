using UnityEngine;

// Visualises an explosion's ACTUAL damage sphere: a translucent shell that snaps to the
// blast radius and fades. Before this, splash was invisible — players judged "was I in
// range?" from whether their health moved, which is unlearnable. Showing the true radius
// turns near-misses into information: you saw the edge, you know how far to stand next time.
//
// Purely local. Every machine that runs a Detonate (real or visual rocket) draws its own —
// no networking, nothing to desync.
public class BlastFx : MonoBehaviour
{
    const float Life = 0.35f;       // long enough to read the edge, short enough to not smoke up the fight
    const float StartAlpha = 0.35f;

    Material mat;
    Color tint;
    float bornAt;
    float radius;

    public static void Spawn(Vector3 center, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BlastFx";
        Object.Destroy(go.GetComponent<Collider>()); // visual only — must never block a shot
        go.transform.position = center;

        var fx = go.AddComponent<BlastFx>();
        fx.radius = radius;

        // Sprites/Default: unlit AND respects vertex/material alpha, which URP Unlit does not
        // do without surface-type surgery. The same trade TracerRenderer already accepts.
        var rend = go.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) rend.material = new Material(sh);
        fx.mat = rend.material;
        fx.tint = new Color(1f, 0.55f, 0.2f, StartAlpha);
    }

    void Start() => bornAt = Time.time;

    void Update()
    {
        float t = (Time.time - bornAt) / Life;
        if (t >= 1f) { Destroy(gameObject); return; }

        // Pops to ~70% instantly then eases to the full radius — reads as a blast, not a
        // balloon. Scale is DIAMETER, hence radius * 2.
        float r = radius * Mathf.Lerp(0.7f, 1f, 1f - Mathf.Pow(1f - t, 3f));
        transform.localScale = Vector3.one * (r * 2f);

        if (mat != null)
        {
            Color c = tint;
            c.a = StartAlpha * (1f - t);
            mat.color = c;
        }
    }
}
