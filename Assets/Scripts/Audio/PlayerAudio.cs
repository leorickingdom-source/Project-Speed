using UnityEngine;

// All sound a player makes. Sits on every player, local and remote, and plays through a 3D
// AudioSource — so distance and direction carry, which is the entire point: you should be
// able to hear where someone is without seeing them.
//
// Jumps and landings are DERIVED from the motor rather than networked. Remote players
// already move correctly through prediction, so watching their replicated motion is enough
// to know when they landed. That means enemy movement audio costs no bandwidth at all. Only
// weapon fire needs an explicit message, because WeaponController is disabled on non-owners
// and nothing else reveals a shot.
//
// Footsteps REMOVED after playtest. In a game where everyone runs at 9+ m/s permanently,
// steps are not information — they are a metronome that never stops, and they buried the
// sounds that do carry meaning (jumps, landings, dashes, gunfire). Landings still give away
// position, and they fire exactly when something HAPPENED.
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAudio : MonoBehaviour
{
    [Header("Mix")]
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("Distance at which a sound is no longer audible. The arena is 90m across, so " +
             "60 lets you hear most of it while still fading with distance.")]
    public float maxDistance = 60f;

    PlayerMotor motor;
    AudioSource src;
    AudioClip land, jump, dashClip, grappleOn, grappleOff, grappleWarn, fireLow, fireHigh, meleeSwing;

    bool wasGrounded;
    bool wasGrappled;
    bool warnedThisHook;
    float lastDashCooldown;
    int lastAirJumps = -1;
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

        land = ProceduralAudio.Noise("land", 0.16f, 26f, 0.6f);
        jump = ProceduralAudio.Sweep("jump", 220f, 420f, 0.10f, 26f, 0.35f);
        dashClip = ProceduralAudio.Sweep("dash", 700f, 180f, 0.20f, 14f, 0.45f);
        grappleOn = ProceduralAudio.Tone("grapOn", 900f, 0.09f, 40f, 0.4f);
        grappleOff = ProceduralAudio.Tone("grapOff", 380f, 0.10f, 34f, 0.3f);
        // Higher and shorter than either: a tick you notice mid-swing without mistaking it
        // for the hook having already let go.
        grappleWarn = ProceduralAudio.Tone("grapWarn", 1250f, 0.06f, 55f, 0.25f);
        fireLow = ProceduralAudio.Noise("fireLow", 0.13f, 40f, 0.55f);
        fireHigh = ProceduralAudio.Noise("fireHigh", 0.06f, 70f, 0.4f);
        // A downward whoosh, unlike every gunshot in the game — melee is an instant kill, so
        // hearing one behind you has to be instantly distinguishable from being shot at.
        meleeSwing = ProceduralAudio.Sweep("melee", 520f, 130f, 0.16f, 18f, 0.5f);

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
    public void PlayMelee() => Play(meleeSwing, Random.Range(0.95f, 1.05f), 1.1f);

    public void PlayFire(int weaponIndex)
    {
        switch (weaponIndex)
        {
            // Melee rides the fire message rather than owning a second RPC that says the
            // same thing — see WeaponController.MeleeAudioIndex.
            case WeaponController.MeleeAudioIndex: PlayMelee(); break;
            case 2: Play(fireLow, 0.55f, 1.1f); break;  // Sniper — deep, carries
            case 4: Play(fireLow, 0.8f, 1.0f); break;   // Shotgun — full-bodied
            case 3: Play(fireHigh, 1.25f, 0.55f); break;// SMG — thin and fast
            case 1: Play(fireHigh, 1.0f, 0.7f); break;  // Rifle
            // Revolver — the loudest thing in the game per shot, and deliberately deep. Six
            // rounds landing a 3-shot kill has to SOUND like it hurts, or the weapon's whole
            // premise ("every trigger pull matters") is contradicted by a polite click.
            default: Play(fireLow, 0.7f, 1.15f); break;
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
        }
        else if (!g && wasGrounded)
        {
            Play(jump, Random.Range(0.95f, 1.05f), 0.7f);
        }
        wasGrounded = g;

        // Dash, inferred from the cooldown jumping back up to full. Derived rather than pushed
        // from PlayerMotor so the deterministic sim keeps no audio dependency — and it covers
        // remote players for free, since their cooldown replicates with the rest of the state.
        // Louder than any footstep (playtest: bursts under-read) — a dash is a statement.
        float cd = motor.DashCooldownLeft;
        if (cd > lastDashCooldown + 0.01f) Play(dashClip, Random.Range(0.95f, 1.05f), 1.15f);
        lastDashCooldown = cd;

        // Air jump, same derived pattern (AirJumpsLeft replicates with the motor state). The
        // ground-leave edge above never fires for it — you are already airborne — so without
        // this the double jump was the one movement verb with NO sound at all. Higher pitch
        // than a ground jump: same family, clearly the special one.
        int aj = motor.AirJumpsLeft;
        if (lastAirJumps >= 0 && aj < lastAirJumps) Play(jump, 1.35f, 1.0f);
        lastAirJumps = aj;

        // Grapple attach / release, read off the hook's own state so it covers remote players.
        if (grapple != null)
        {
            bool a = grapple.Attached;
            if (a && !wasGrappled) { Play(grappleOn, 1f, 0.8f); warnedThisHook = false; }
            else if (!a && wasGrappled) Play(grappleOff, 1f, 0.6f);
            wasGrappled = a;

            // Fires once per hook, on the way down through the threshold. A repeating beep
            // would be noise in a 2.5 second window; one tick is a deadline.
            if (a && !warnedThisHook && grapple.TimeLeft01 <= 0.3f)
            {
                warnedThisHook = true;
                Play(grappleWarn, 1f, 0.7f);
            }
        }
    }
}
