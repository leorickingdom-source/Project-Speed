using UnityEngine;

// Shared headshot test. Extracted so hitscan (WeaponController) and projectiles
// (Projectile) can never drift on what counts as a head — a target that is a headshot
// for the crossbow has to be one for the bow too.
public static class Headshot
{
    // True when the hit lands in the top `fraction` of the target's collider bounds.
    public static bool IsHead(Collider target, Vector3 point, float fraction) =>
        point.y >= target.bounds.max.y - target.bounds.size.y * fraction;
}
