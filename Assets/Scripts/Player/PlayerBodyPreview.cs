using UnityEngine;

// Dev tool. A row of humanoids, each frozen in one motion state and animating, so the gait
// can be judged from OUTSIDE without standing up a second client.
//
// It exists because PlayerBody is invisible to the person best placed to evaluate it: the
// body renders on remote players only, so the one view you cannot get while testing alone is
// the one you actually need. This puts every state side by side instead.
//
// Drop it on an empty GameObject and press Play. It fakes the motor rather than simulating
// one — a disabled PlayerMotor is just a bag of public fields, and PlayerBody reads nothing
// else, so a state that would take a grapple swing to reach in game is one line here.
public class PlayerBodyPreview : MonoBehaviour
{
    [Tooltip("Metres between dummies.")]
    public float spacing = 2.2f;
    [Tooltip("Rotate the row to face the camera as it orbits. Off = fixed forward, which is " +
             "what you want when comparing silhouettes rather than watching one.")]
    public bool faceCamera;
    [Tooltip("Draw the state name over each dummy.")]
    public bool labels = true;

    // Everything PlayerBody reads, per dummy. Speed comes from `velocity`, so a state is just
    // a velocity, a stance height and whether the feet are down.
    struct State
    {
        public string name;
        public Vector3 velocity;
        public bool grounded;
        public float height;
        public bool sliding;
    }

    static readonly State[] States =
    {
        new State { name = "idle",        velocity = Vector3.zero,               grounded = true,  height = 2f },
        new State { name = "walk 4",      velocity = new Vector3(0, 0, 4f),      grounded = true,  height = 2f },
        new State { name = "run 11",      velocity = new Vector3(0, 0, 11f),     grounded = true,  height = 2f },
        new State { name = "sprint 28",   velocity = new Vector3(0, 0, 28f),     grounded = true,  height = 2f },
        new State { name = "backpedal",   velocity = new Vector3(0, 0, -10f),    grounded = true,  height = 2f },
        new State { name = "20 m/s",      velocity = new Vector3(0, 0, 20f),     grounded = true,  height = 2f },
        new State { name = "strafe",      velocity = new Vector3(12f, 0, 0),     grounded = true,  height = 2f },
        new State { name = "airborne",    velocity = new Vector3(0, 9f, 14f),    grounded = false, height = 2f },
        new State { name = "half crouch", velocity = new Vector3(0, 0, 8f),      grounded = true,  height = 1.5f },
        new State { name = "crouch walk", velocity = new Vector3(0, 0, 4f),      grounded = true,  height = 1f },
        new State { name = "slide",       velocity = new Vector3(0, 0, 14f),     grounded = true,  height = 1f, sliding = true },
    };

    PlayerMotor[] motors;
    Transform[] roots;

    void Start()
    {
        motors = new PlayerMotor[States.Length];
        roots = new Transform[States.Length];
        float x = -(States.Length - 1) * spacing * 0.5f;

        for (int i = 0; i < States.Length; i++)
        {
            var go = new GameObject("Preview_" + States[i].name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x + i * spacing, 0f, 0f);
            roots[i] = go.transform;

            // Disabled immediately: Awake still runs and captures what it needs, but Update
            // never does, so the fields set below are never overwritten by a simulation that
            // has no collider, no input and nowhere to move.
            var motor = go.AddComponent<PlayerMotor>();
            motor.enabled = false;
            motors[i] = motor;

            // Colour-cycled through the real palette, so this doubles as a check that player
            // identity still reads at a glance across the whole set.
            PlayerBody.Attach(go.transform, PlayerColors.For(i), true, hitboxes: true);
        }
    }

    void Update()
    {
        if (motors == null) return;
        for (int i = 0; i < motors.Length; i++)
        {
            if (motors[i] == null) continue;
            // Re-applied every frame. PlayerMotor is disabled so nothing else writes these,
            // but re-applying costs nothing and means a domain reload or an accidental
            // re-enable cannot quietly strand a dummy in the wrong state.
            motors[i].velocity = States[i].velocity;
            motors[i].grounded = States[i].grounded;
            motors[i].height = States[i].height;
            motors[i].sliding = States[i].sliding;

            if (faceCamera && Camera.main != null)
            {
                Vector3 to = Camera.main.transform.position - roots[i].position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) roots[i].rotation = Quaternion.LookRotation(-to);
            }
        }
    }

    void OnGUI()
    {
        if (!labels || roots == null || Camera.main == null) return;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            Vector3 p = Camera.main.WorldToScreenPoint(roots[i].position + Vector3.up * 2.15f);
            if (p.z <= 0f) continue;
            GUI.Label(new Rect(p.x - 50f, Screen.height - p.y - 10f, 100f, 20f), States[i].name);
        }
    }
}
