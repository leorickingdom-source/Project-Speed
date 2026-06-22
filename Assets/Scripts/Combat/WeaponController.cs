using UnityEngine;
using UnityEngine.InputSystem;

// Two hitscan weapons, both click-to-fire (semi-auto with a cycle cadence):
//   Key 1 = Lever-Action Shotgun (pellet spread, slow cycle)
//   Key 2 = Railgun (single pinpoint beam)
// Tracers/beam are drawn from a small pooled set of LineRenderers (no per-shot alloc).
public class WeaponController : MonoBehaviour
{
    public InputReader input;
    public Transform aim;

    [Header("Masks")]
    public LayerMask hitMask = ~0;       // player layer removed at runtime

    [Header("Shotgun (lever-action)")]
    public float shotgunCycle = 0.8f;    // seconds between shots (lever cadence)
    public int pellets = 9;
    public float spreadDegrees = 6f;
    public float pelletDamage = 12f;
    public float shotgunRange = 60f;
    public Color shotgunColor = new Color(1f, 0.85f, 0.4f);

    [Header("Railgun")]
    public float railCycle = 1.1f;
    public float railDamage = 100f;
    public float railRange = 300f;
    public Color railColor = new Color(1f, 0.3f, 0.9f);

    [Header("Tracers")]
    public float tracerTime = 0.04f;

    public int Current { get; private set; } // 0 = shotgun, 1 = rail
    float nextFire;

    LineRenderer[] pool;
    float[] poolHide;
    int poolNext;

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
        if (aim == null) { var c = GetComponentInChildren<Camera>(); if (c) aim = c.transform; }
        hitMask &= ~(1 << gameObject.layer);
        BuildPool(Mathf.Max(pellets + 2, 12));
    }

    void BuildPool(int n)
    {
        pool = new LineRenderer[n];
        poolHide = new float[n];
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Tracer" + i);
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.03f;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.material = new Material(sh);
            lr.enabled = false;
            pool[i] = lr;
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        var m = Mouse.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) Current = 0;
            if (kb.digit2Key.wasPressedThisFrame) Current = 1;
        }

        bool firePressed = m != null && m.leftButton.wasPressedThisFrame;
        if (firePressed && Time.time >= nextFire)
        {
            if (Current == 0) { FireShotgun(); nextFire = Time.time + shotgunCycle; }
            else { FireRail(); nextFire = Time.time + railCycle; }
        }

        if (pool != null)
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].enabled && Time.time > poolHide[i]) pool[i].enabled = false;
    }

    public void FireShotgun()
    {
        if (aim == null) return;
        Vector3 origin = aim.position;
        for (int i = 0; i < pellets; i++)
        {
            Vector2 off = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            Vector3 dir = (aim.forward + aim.right * off.x + aim.up * off.y).normalized;
            Vector3 end = origin + dir * shotgunRange;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, shotgunRange, hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var hp = hit.collider.GetComponentInParent<Health>();
                if (hp != null) hp.Damage(pelletDamage);
            }
            Tracer(origin - aim.up * 0.15f, end, shotgunColor);
        }
    }

    public void FireRail()
    {
        if (aim == null) return;
        Vector3 origin = aim.position;
        Vector3 end = origin + aim.forward * railRange;
        if (Physics.Raycast(origin, aim.forward, out RaycastHit hit, railRange, hitMask,
                QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var hp = hit.collider.GetComponentInParent<Health>();
            if (hp != null) hp.Damage(railDamage);
        }
        Tracer(origin - aim.up * 0.2f, end, railColor);
    }

    void Tracer(Vector3 a, Vector3 b, Color col)
    {
        if (pool == null) return;
        int idx = poolNext;
        poolNext = (poolNext + 1) % pool.Length;
        var lr = pool[idx];
        lr.startColor = lr.endColor = col;
        if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", col);
        lr.material.color = col;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.enabled = true;
        poolHide[idx] = Time.time + tracerTime;
    }
}
