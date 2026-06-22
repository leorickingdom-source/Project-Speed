using UnityEngine;

// Radial blast: knocks back the player (rocket jump) and damages anything with
// Health. Static so weapons, traps, or test code all produce identical blasts.
public static class Explosion
{
    public static void Detonate(Vector3 center, float radius, float force,
        float selfForce, float damage, GameObject owner)
    {
        Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            Vector3 to = c.bounds.center - center;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : Vector3.up;
            // Bias upward so blasts launch you instead of skidding along the floor.
            dir = (dir + Vector3.up * 0.5f).normalized;
            float falloff = 1f - Mathf.Clamp01(dist / radius);

            var motor = c.GetComponentInParent<PlayerMotor>();
            if (motor != null)
            {
                float f = (motor.gameObject == owner) ? selfForce : force;
                motor.AddImpulse(dir * f * (0.35f + 0.65f * falloff));
            }

            var hp = c.GetComponentInParent<Health>();
            if (hp != null) hp.Damage(damage * falloff);
        }
    }
}
