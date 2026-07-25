using UnityEngine;

// Cosmetic corpse: a tinted capsule that takes the dead player's place and falls over.
//
// Before this, death looked like nothing — the body froze standing upright until the respawn
// teleport, so from across the arena a kill was indistinguishable from someone standing still.
// The playtest ask was literally "let people know they died". A capsule with a Rigidbody and a
// shove is the whole message: bodies that fall are dead, bodies that stand are threats.
//
// Purely local on every client (spawned from the health SyncVar callback, which already runs
// everywhere), so it costs zero bandwidth and can never desync anything that matters.
public static class CorpseFx
{
    const float Lifetime = 4f;       // gone well before the 20s pickup cycle makes spots reused
    const float TipImpulse = 2.6f;   // enough to clearly topple, not to launch across the map
    const float SpinImpulse = 4f;

    // On Ignore Raycast so a corpse never eats a live shot (WeaponController strips layer 2
    // from its hit mask for the same reason). It still collides with world geometry, so it
    // falls onto the floor rather than through it.
    const int IgnoreRaycastLayer = 2;

    // `body` is the player's visual capsule — the corpse copies its pose, scale and colour so
    // the swap reads as the same player keeling over, not a prop appearing. `awayFrom` is the
    // killer's position when known; the corpse falls away from the shot, which is the one
    // detail that makes the physics read as caused rather than random.
    public static void Spawn(Transform body, Vector3? awayFrom)
    {
        if (body == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Corpse";
        go.layer = IgnoreRaycastLayer;
        go.transform.SetPositionAndRotation(body.position, body.rotation);
        go.transform.localScale = body.lossyScale;

        // Match the player's tint so spectators can tell WHO died at a glance.
        var src = body.GetComponent<Renderer>();
        var dst = go.GetComponent<Renderer>();
        if (src != null && dst != null)
        {
            Color c = src.material.color;
            var m = dst.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
        }

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.linearDamping = 0.4f;
        rb.angularDamping = 0.6f;

        // Tip away from the killer when we know them (the victim's own client does); everyone
        // else gets a random horizontal shove, which still reads as a fall.
        Vector3 dir;
        if (awayFrom.HasValue)
        {
            dir = body.position - awayFrom.Value;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Random.insideUnitSphere.normalized;
        }
        else
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            dir = new Vector3(r.x, 0f, r.y);
        }

        rb.AddForce(dir * TipImpulse, ForceMode.VelocityChange);
        // Torque axis perpendicular to the fall direction, so it topples in the direction of
        // the shove instead of spinning on the spot like a top.
        rb.AddTorque(Vector3.Cross(Vector3.up, dir) * SpinImpulse, ForceMode.VelocityChange);

        Object.Destroy(go, Lifetime);
    }
}
