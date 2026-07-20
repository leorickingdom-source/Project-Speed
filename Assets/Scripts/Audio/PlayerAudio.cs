using UnityEngine;

// All sound a player makes. Sits on every player, local and remote, and plays through a 3D
// AudioSource — so distance and direction carry, which is the entire point: you should be
// able to hear where someone is without seeing them.
//
// Footsteps, jumps and landings are DERIVED from the motor rather than networked. Remote
// players already move correctly through prediction, so watching their replicated motion is
// enough to know when they stepped or landed. That means enemy movement audio costs no
// bandwidth at all. Only weapon fire needs an explicit message, because WeaponController is
// disabled on non-owners and nothing else reveals a shot.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAudio : MonoBehaviour
{
    [Header("Mix")]
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("Distance at which a sound is no longer audible. The arena is 90m across, so " +
             "60 lets you hear most of it while still fading with distance.")]
    public float maxDistance = 60f;

    [Header("Footsteps")]
    [Tooltip("Metres travelled between steps. Cadence follows speed automatically because " +
             "it is distance-based, not time-based.")]
    public float stepDistance = 2.6f;
    [Tooltip("Below this speed you are considered to be sneaking and make no step sound.")]
    public float minStepSpeed = 2f;

    PlayerMotor motor;
    AudioSource src;
    AudioClip step, land, jump, dashClip, grappleOn, grappleOff, fireLow, fireHigh;

    float stepAccum;
    bool wasGrounded;
    bool wasGrappled;
    float lastDashCooldown;
    GrappleHook grapple;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        grapple = GetComponent<GrappleHook>();

        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;                 // 3D — direction and distance are the payload
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 3f;
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0f;                 // off: fast movement would warp pitch constantly

        step = ProceduralAudio.Noise("step", 0.07f, 55f, 0.35f);
        land = ProceduralAudio.Noise("land", 0.16f, 26f, 0.6f);
        jump = ProceduralAudio.Sweep("jump", 220f, 420f, 0.10f, 26f, 0.35f);
        dashClip = ProceduralAudio.Sweep("dash", 700f, 180f, 0.20f, 14f, 0.45f);
        grappleOn = ProceduralAudio.Tone("grapOn", 900f, 0.09f, 40f, 0.4f);
        grappleOff = ProceduralAudio.Tone("grapOff", 380f, 0.10f, 34f, 0.3f);
        fireLow = ProceduralAudio.Noise("fireLow", 0.13f, 40f, 0.55f);
        fireHigh = ProceduralAudio.Noise("fireHigh", 0.06f, 70f, 0.4f);

        wasGrounded = motor.grounded;
    }

    void Play(AudioClip c, float pitch = 1f, float gain = 1f)
    {
        if (c == null || src == null) return;
        src.pitch = pitch;
        src.PlayOneShot(c, volume * gain);
    }

    // Called by WeaponController on the owner, and by PlayerNetwork on everyone else when the
    // fire message arrives. Heavier weapons get a lower pitch so shots are identifiable by ear.
    public void PlayFire(int weaponIndex)
    {
        switch (weaponIndex)
        {
            case 2: Play(fireLow, 0.55f, 1.1f); break;  // Sniper — deep, carries
            case 4: Play(fireLow, 0.8f, 1.0f); break;   // Shotgun — full-bodied
            case 3: Play(fireHigh, 1.25f, 0.55f); break;// SMG — thin and fast
            case 1: Play(fireHigh, 1.0f, 0.7f); break;  // Rifle
            default: Play(fireHigh, 0.9f, 0.8f); break; // Pistol
        }
    }


    void Update()
    {
        if (motor == null) return;

        // Landing: detect the air->ground edge. Loud in proportion to impact so a big fall
        // reads differently from stepping off a crate.
        bool g = motor.grounded;
        if (g && !wasGrounded)
        {
            float impact = Mathf.InverseLerp(2f, 18f, Mathf.Abs(motor.velocity.y));
            Play(land, Random.Range(0.92f, 1.08f), 0.5f + impact * 0.8f);
            stepAccum = 0f;
        }
        else if (!g && wasGrounded)
        {
            Play(jump, Random.Range(0.95f, 1.05f), 0.7f);
        }
        wasGrounded = g;

        // Footsteps by DISTANCE travelled, so cadence speeds up naturally with movement and
        // stays silent when you are barely moving.
        if (g && motor.Speed > minStepSpeed && !motor.sliding)
        {
            stepAccum += motor.Speed * Time.deltaTime;
            if (stepAccum >= stepDistance)
            {
                stepAccum -= stepDistance;
                Play(step, Random.Range(0.9f, 1.15f), 0.9f);
            }
        }
        else if (!g)
        {
            stepAccum = 0f;
        }

        // Dash, inferred from the cooldown jumping back up to full. Derived rather than pushed
        // from PlayerMotor so the deterministic sim keeps no audio dependency — and it covers
        // remote players for free, since their cooldown replicates with the rest of the state.
        float cd = motor.DashCooldownLeft;
        if (cd > lastDashCooldown + 0.01f) Play(dashClip, Random.Range(0.95f, 1.05f), 0.9f);
        lastDashCooldown = cd;

        // Grapple attach / release, read off the hook's own state so it covers remote players.
        if (grapple != null)
        {
            bool a = grapple.Attached;
            if (a && !wasGrappled) Play(grappleOn, 1f, 0.8f);
            else if (!a && wasGrappled) Play(grappleOff, 1f, 0.6f);
            wasGrappled = a;
        }
    }
}
