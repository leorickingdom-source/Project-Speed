using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Golden-input replay tests for the movement sim — the tripwire under everything creative.
//
// The motor's whole design is that Step(cmd, dt) is deterministic with no hidden inputs;
// that is also what makes it TESTABLE without mocking anything: build a floor, feed scripted
// commands, assert on the state that comes out. These pin the feel that took weeks of
// playtests to tune — ground cap, stop, jump height, air speed preservation, rope behaviour,
// bit-identical replay — so a map, art or content change that quietly breaks movement fails
// a test instead of a playtest.
//
// PlayMode, not EditMode: Awake must run (it resolves the collider, masks and the grapple),
// and the sim raycasts against live scene colliders. Lives in Game.Tests, which references
// Game.Runtime — the asmdef the game scripts moved into for exactly this reason: a test
// assembly cannot reference the predefined Assembly-CSharp, so a sim with no assembly of its
// own is a sim nothing can test.
public class MotorReplayTests
{
    const float Dt = 0.02f; // the fixed tick the game actually runs at

    GameObject floor;
    GameObject player;
    PlayerMotor motor;
    GrappleHook hook;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "TestFloor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(400f, 1f, 400f);

        player = new GameObject("TestPlayer");
        player.transform.position = new Vector3(0f, 0.05f, 0f);
        // OFF the Default layer, and before any component: Awake strips the player's own
        // layer from groundMask so casts never hit the capsule — on Default that would strip
        // the test floor too and the motor would fall through the world.
        player.layer = 8;
        // Grapple first so PlayerMotor.Awake finds it. LineRenderer arrives via RequireComponent.
        hook = player.AddComponent<GrappleHook>();
        motor = player.AddComponent<PlayerMotor>();
        motor.ExternallyDriven = true; // tests drive Step; FixedUpdate must not double-tick

        Physics.SyncTransforms();
        yield return null; // one frame so play-mode lifecycle settles
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Object.Destroy(player);
        Object.Destroy(floor);
        yield return null;
    }

    static InputCmd Cmd(float x = 0f, float y = 0f, bool jump = false, bool grapple = false,
        float yaw = 0f)
        => new InputCmd { move = new Vector2(x, y), jumpHeld = jump, grapple = grapple, yaw = yaw };

    void Run(int ticks, InputCmd cmd)
    {
        for (int i = 0; i < ticks; i++) motor.Step(cmd, Dt);
    }

    // Holding W reaches the ground cap and no more; releasing everything actually stops —
    // the "slidy" complaint, pinned. stopSpeed guarantees the tail dies instead of decaying
    // forever.
    [UnityTest]
    public IEnumerator GroundRun_HitsCap_ThenStops()
    {
        Run(150, Cmd(y: 1f));                       // 3s of W
        Assert.That(motor.Speed, Is.EqualTo(motor.groundSpeed).Within(0.4f),
            "running speed should sit at the ground cap");

        Run(100, Cmd());                            // 2s hands off
        Assert.That(motor.Speed, Is.LessThan(0.1f), "friction should bring a runner to a stop");
        Assert.IsTrue(motor.grounded);
        yield return null;
    }

    // Jump apex matches v^2/2g. This is the number the whole gravity retune promised to
    // preserve — if someone touches jumpForce or gravity alone, this fails.
    [UnityTest]
    public IEnumerator Jump_ApexMatchesFormula()
    {
        Run(10, Cmd());                             // settle onto the floor
        motor.Step(Cmd(jump: true), Dt);

        float apex = 0f;
        for (int i = 0; i < 80; i++)
        {
            motor.Step(Cmd(), Dt);
            apex = Mathf.Max(apex, player.transform.position.y);
        }

        float expected = motor.jumpForce * motor.jumpForce / (2f * motor.gravity);
        Assert.That(apex, Is.EqualTo(expected).Within(0.12f),
            $"apex should be ~v^2/2g = {expected:0.00}");
        yield return null;
    }

    // Air is frictionless: speed carried into the air is exactly kept when no input pushes.
    // This is the bunnyhop contract — ground friction may not leak into airtime.
    [UnityTest]
    public IEnumerator Air_CarriesSpeedUntouched()
    {
        Run(10, Cmd());
        motor.PadBoost(12f, new Vector3(15f, 0f, 0f));
        motor.Step(Cmd(), Dt);
        float launched = motor.Speed;

        Run(30, Cmd());                             // 0.6s of empty air
        Assert.IsFalse(motor.grounded, "should still be airborne");
        Assert.That(motor.Speed, Is.EqualTo(launched).Within(0.01f),
            "horizontal speed must not decay in the air");
        yield return null;
    }

    // The rope is inextensible: from attach onward, distance to the anchor may never exceed
    // the rope, and the reel must visibly shorten it. This is the constraint the spring
    // rewrite exists for.
    [UnityTest]
    public IEnumerator Rope_NeverExtends_AndReelsIn()
    {
        Run(10, Cmd());
        Vector3 anchor = player.transform.position + new Vector3(0f, 14f, 14f);
        float rope = (anchor - (player.transform.position + Vector3.up)).magnitude;
        hook.SetNetState(true, anchor, held: true, timeLeft: 5f, ropeLength: rope, cooldownLeft: 0f);

        float slack = 0.2f;                         // position-constraint tolerance per tick
        for (int i = 0; i < 100; i++)
        {
            motor.Step(Cmd(grapple: true), Dt);
            if (!hook.Attached) break;              // arrive/auto-release ends the test early
            float dist = (anchor - (player.transform.position + Vector3.up)).magnitude;
            Assert.That(dist, Is.LessThanOrEqualTo(rope + slack),
                $"tick {i}: rope stretched — dist {dist:0.00} vs length {rope:0.00}");
            rope = Mathf.Max(0f, Mathf.Min(rope, dist)); // it may only ever shrink
        }

        Assert.That((anchor - (player.transform.position + Vector3.up)).magnitude,
            Is.LessThan(rope + 0.01f).And.LessThan(28f), "reel should have pulled closer");
        yield return null;
    }

    // Bit-identical replay: the property prediction and reconciliation stand on. Same state,
    // same commands, same dt — the same trajectory, to float precision, including a jump, a
    // yaw sweep and a rope. If this breaks, someone fed the sim a hidden input (Time.time,
    // transform reads, randomness) and netplay will mispredict forever.
    [UnityTest]
    public IEnumerator Replay_IsBitIdentical()
    {
        Run(10, Cmd());
        MotorState start = motor.GetState();

        Vector3[] first = RunScript();
        motor.SetState(start);
        Vector3[] second = RunScript();

        for (int i = 0; i < first.Length; i++)
            Assert.That((first[i] - second[i]).magnitude, Is.LessThan(1e-5f),
                $"tick {i}: replay diverged by {(first[i] - second[i]).magnitude}");
        yield return null;
    }

    Vector3[] RunScript()
    {
        var positions = new Vector3[200];
        for (int i = 0; i < positions.Length; i++)
        {
            // A deterministic mixed script: run, veer, hop twice, tap the rope mid-flight.
            var cmd = Cmd(
                x: i > 120 ? 1f : 0f,
                y: 1f,
                jump: (i % 60) < 2,
                grapple: i is > 80 and < 110,
                yaw: i * 0.5f);
            motor.Step(cmd, Dt);
            positions[i] = player.transform.position;
        }
        return positions;
    }
}
