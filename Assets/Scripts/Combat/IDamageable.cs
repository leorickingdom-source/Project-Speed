// Anything that can take damage — targets, bots, the player. Weapons and explosions
// resolve it via GetComponentInParent<IDamageable>() so one call hits everything, and
// networking gets a single contract to authorize server-side.
public interface IDamageable
{
    void Damage(float amount);
}
