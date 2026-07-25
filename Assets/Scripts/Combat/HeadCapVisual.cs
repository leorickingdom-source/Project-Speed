using UnityEngine;

// The dark "aim here" cap drawn over the headshot band of a target. Shared by players
// (PlayerNetwork) and bots (SimpleBot) so the paint and the damage rule cannot drift apart:
// both size the cap from the same headFraction that Headshot.IsHead scores with.
//
// Bounds-based rather than assuming a capsule mesh, so it sits correctly on anything with a
// renderer — a bot that is a box tomorrow gets a correct cap with no edits here.
public static class HeadCapVisual
{
    // Parents a squashed dark sphere over the top `fraction` of `target`'s renderer bounds.
    // Visual only — the collider is destroyed, so it can never change what a shot hits.
    public static void Attach(Transform target, float fraction, Color bodyColor)
    {
        var rend = target.GetComponent<Renderer>();
        if (rend == null) return;
        Bounds b = rend.bounds; // world space

        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cap.name = "HeadCap";
        Object.Destroy(cap.GetComponent<Collider>());

        float capHeight = b.size.y * fraction;
        cap.transform.position = new Vector3(b.center.x, b.max.y - capHeight * 0.5f, b.center.z);
        // Slightly wider than the body so it reads as a layer on it, not z-fighting in it.
        cap.transform.localScale = new Vector3(b.size.x * 1.04f, capHeight, b.size.z * 1.04f);
        // Parent AFTER world placement; keeps the maths in one coordinate space.
        cap.transform.SetParent(target, true);

        // Much darker of the same hue: still reads as that target's colour at a glance, but
        // the boundary between "body" and "head" is obvious at fight distance.
        Color dark = bodyColor * 0.35f;
        dark.a = 1f;
        var m = cap.GetComponent<Renderer>().material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
        m.color = dark;
    }
}
