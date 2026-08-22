using UnityEngine;

// Shared headshot test. Extracted so hitscan (WeaponController) and projectiles
// (Projectile) can never drift on what counts as a head — a target that is a headshot
// for the crossbow has to be one for the bow too.
public static class Headshot
{
    // Two answers, and which one you get depends on what the shot actually hit.
    //
    // A Hitbox collider IS a body part, so the question is identity: head box or not. That is
    // the accurate path, and it is what a player with an animated rig gets.
    //
    // Everything else — bots, and players with rigHitboxes off — still gets the geometric rule:
    // the hit landed in the top `fraction` of the collider's bounds. It has to stay, because a
    // capsule and a sphere have no head to hit, and it is what the head CAP is painted from.
    //
    // The geometric rule is also why the rig version is worth having. A fraction of a capsule
    // that changes height moves when you crouch, and it never moved the way the visible body
    // did: measured against the clips, a crouching player's crown stood 14-27cm above the whole
    // 1m capsule, and a sliding one 28-62cm — not merely outside the band, outside the collider,
    // so the head could not be hit at all. A box bolted to the skull cannot disagree with it.
    public static bool IsHead(Collider target, Vector3 point, float fraction)
    {
        var hb = target.GetComponent<Hitbox>();
        if (hb != null) return hb.IsHead;
        return point.y >= target.bounds.max.y - target.bounds.size.y * fraction;
    }

    // Per-part damage scale, 1 for anything that is not part of a rig. Kept beside IsHead so
    // both questions about "what did I hit" are answered from the same place.
    public static float PartScale(Collider target)
    {
        var hb = target.GetComponent<Hitbox>();
        return hb != null ? hb.damageScale : 1f;
    }
}
