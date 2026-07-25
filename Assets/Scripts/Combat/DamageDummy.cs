using System.Collections.Generic;
using UnityEngine;

// Training dummy ("punching bag"): takes damage from any weapon/projectile via
// IDamageable, never dies, flashes on hit, and pops rising damage numbers plus a
// running total so you can read each weapon's output.
public class DamageDummy : MonoBehaviour, IDamageable
{
    [Header("Look")]
    public Color idleColor = new Color(0.55f, 0.55f, 0.62f);
    public Color hitFlash = new Color(1f, 0.3f, 0.2f);

    [Header("Numbers")]
    public float numberLifetime = 1.1f;
    public float riseSpeed = 1.6f;
    public Color numberColor = new Color(1f, 0.92f, 0.35f);
    [Tooltip("Clear the running total after this many seconds without a hit.")]
    public float resetAfter = 2f;

    public float Total { get; private set; }
    public float Last { get; private set; }

    struct Pop { public float amount; public float time; public Vector3 origin; }
    readonly List<Pop> pops = new();
    Renderer rend;
    MaterialPropertyBlock mpb;
    float lastHitTime = -99f;
    GUIStyle style;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        EnsureBlock();

        // Same dark head cap players and bots wear, so the dummy TEACHES the head zone —
        // it is the one target you can study at leisure, which makes it the worst place
        // for the band to be invisible. 0.28 matches WeaponController's headFraction.
        HeadCapVisual.Attach(transform, 0.28f, idleColor);
    }

    // A MaterialPropertyBlock is a plain C# object, so Unity does not serialize it across a
    // DOMAIN RELOAD — which is what a script recompile during play mode causes. The Renderer
    // reference survives (it is a UnityEngine.Object) but this does not, so a null check on
    // `rend` alone is not enough: after a hot recompile the guard passes and the block is null.
    void EnsureBlock()
    {
        if (rend != null && mpb == null) mpb = new MaterialPropertyBlock();
    }

    public void Damage(float amount)
    {
        if (amount <= 0f) return;
        Last = amount;
        Total += amount;
        lastHitTime = Time.time;
        pops.Add(new Pop { amount = amount, time = Time.time, origin = transform.position + Vector3.up * 1.3f });
    }

    void Update()
    {
        // Flash red on hit, ease back to idle.
        EnsureBlock();
        if (rend != null && mpb != null)
        {
            float t = Mathf.Clamp01((Time.time - lastHitTime) / 0.12f);
            mpb.SetColor("_BaseColor", Color.Lerp(hitFlash, idleColor, t));
            rend.SetPropertyBlock(mpb);
        }
        // Drop expired numbers.
        for (int i = pops.Count - 1; i >= 0; i--)
            if (Time.time - pops[i].time > numberLifetime) pops.RemoveAt(i);

        // Clear the running total after a lull, so each burst reads fresh.
        if (Total > 0f && Time.time - lastHitTime > resetAfter) { Total = 0f; Last = 0f; }
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        // Rising, fading damage numbers.
        foreach (var p in pops)
        {
            float e = Time.time - p.time;
            Vector3 sp = cam.WorldToScreenPoint(p.origin + Vector3.up * (e * riseSpeed));
            if (sp.z <= 0f) continue;
            Color c = numberColor; c.a = 1f - Mathf.Clamp01(e / numberLifetime);
            style.normal.textColor = c;
            GUI.Label(new Rect(sp.x - 40f, Screen.height - sp.y - 12f, 80f, 26f), p.amount.ToString("0"), style);
        }

        // Running total above the bag.
        Vector3 basePos = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
        if (basePos.z > 0f)
        {
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(basePos.x - 90f, Screen.height - basePos.y - 12f, 180f, 26f), $"total {Total:0}", style);
        }
    }
}
