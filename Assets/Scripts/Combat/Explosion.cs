using UnityEngine;

// Radial blast: knocks back the player (rocket jump) and damages anything with
// Health. Static so weapons, traps, or test code all produce identical blasts.
//
// A blast only reaches targets it can SEE (Quake's G_RadiusDamage/CanDamage): world
// geometry between the blast and a target shields it, so ducking behind a pillar is a
// real defensive play instead of a decoration. Self-damage is scaled down (Quake halves
// it) so rocket-jumping is a repeatable mobility tool, not a one-shot you pay 70% of
// your health for.
public static class Explosion
{
    // Probe points as a fraction of the target's bounds, tried until one has line of
    // sight. Q3 probes the target's corners for the same reason: a target only half
    // behind cover should still take the hit, so cover degrades instead of flipping
    // a coin at the edge.
    static readonly Vector3[] Probes =
    {
        new Vector3( 0.00f,  0.00f,  0.00f), // centre — the common case, checked first
        new Vector3( 0.00f,  0.45f,  0.00f),
        new Vector3( 0.00f, -0.45f,  0.00f),
        new Vector3( 0.35f,  0.00f,  0.35f),
        new Vector3(-0.35f,  0.00f, -0.35f),
    };

    /// <param name="blockMask">What counts as cover. Pass the weapon's hit mask so
    /// players don't shield each other — only the map blocks a blast.</param>
    /// <param name="selfDamageScale">Fraction of the blast the owner takes (Quake: 0.5).</param>
    /// <param name="damageScale">Outgoing multiplier (Momentum passive). Applied to OTHERS
    /// only — scaling your own rocket-jump blast with speed would punish the exact
    /// behaviour the passive exists to reward.</param>
    public static void Detonate(Vector3 center, float radius, float force,
        float selfForce, float damage, GameObject owner,
        LayerMask blockMask, float selfDamageScale = 0.5f, float damageScale = 1f)
    {
        // Show the sphere that is about to be judged — the visual IS the hitbox, so what
        // players learn from watching it is true.
        BlastFx.Spawn(center, radius);

        Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (!HasLineOfSight(center, c, blockMask)) continue; // behind cover — no damage, no shove

            Vector3 to = c.bounds.center - center;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : Vector3.up;
            // Bias upward so blasts launch you instead of skidding along the floor.
            dir = (dir + Vector3.up * 0.5f).normalized;
            float falloff = 1f - Mathf.Clamp01(dist / radius);
            bool self = owner != null && c.transform.IsChildOf(owner.transform);

            var motor = c.GetComponentInParent<PlayerMotor>();
            if (motor != null)
                motor.AddImpulse(dir * (self ? selfForce : force) * (0.35f + 0.65f * falloff));

            var hp = c.GetComponentInParent<IDamageable>();
            if (hp != null)
            {
                // Name the shooter before the damage lands, so a lethal blast is a credited
                // kill in the feed rather than "X died". Not for self-hits: your own rocket
                // killing you is a suicide, and crediting it would pay for the mistake.
                // Only sticks where the write sticks — on the authority — same as the damage.
                if (!self && owner != null)
                {
                    var victimHealth = hp as PlayerHealth;
                    var ownerNob = owner.GetComponent<FishNet.Object.NetworkObject>();
                    if (victimHealth != null && ownerNob != null)
                        victimHealth.RecordServerAttacker(ownerNob, KillKind.Normal);
                }
                // Kinetic Plating cuts what your OWN blast does to you and leaves the shove
                // alone (the impulse above never sees this), so the rocket stays exactly as
                // good at moving you and gets cheaper to move with. That split is the whole
                // pick: in a game with no regen, the launcher's mobility was rationed by
                // attrition rather than by skill.
                float selfScale = self ? selfDamageScale * SelfDamagePassiveScale(owner) : damageScale;
                hp.Damage(damage * falloff * selfScale);
            }
        }
    }

    // How much of your own blast Kinetic Plating lets through. Looked up per blast rather than
    // cached anywhere: explosions are rare, this is a static helper with no instance to hang a
    // cache on, and a stale value here would silently mis-price every rocket jump of a life.
    public const float KineticPlatingScale = 0.5f;

    static float SelfDamagePassiveScale(GameObject owner)
    {
        if (owner == null) return 1f;
        var passives = owner.GetComponent<PassiveLoadout>();
        return passives != null && passives.Has(PassiveType.KineticPlating)
            ? KineticPlatingScale : 1f;
    }

    // Exposed if ANY probe point traces clear. Note the blast centre must sit slightly
    // off whatever surface it detonated against (see Rocket.Boom) or every trace would
    // start inside that surface and report the whole world as covered.
    static bool HasLineOfSight(Vector3 center, Collider target, LayerMask blockMask)
    {
        Bounds b = target.bounds;
        foreach (var probe in Probes)
        {
            Vector3 point = b.center + Vector3.Scale(probe, b.size);
            Vector3 to = point - center;
            float dist = to.magnitude;

            if (dist < 0.2f) return true; // point blank — nothing could fit in between
            if (!Physics.Raycast(center, to / dist, out RaycastHit hit, dist,
                    blockMask, QueryTriggerInteraction.Ignore))
                return true;              // clear line
            if (hit.collider == target || hit.collider.transform.IsChildOf(target.transform.root))
                return true;              // we hit the target itself, not cover
        }
        return false;
    }
}
