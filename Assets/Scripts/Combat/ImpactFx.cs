using UnityEngine;

// Where a shot LANDED. The project drew the flight of a bullet (TracerRenderer) and the
// damage it did (HitFeedback), but nothing at the far end — a miss left the world completely
// unmarked, so there was no way to see how far off you were, and a hit on a body looked
// exactly like a hit on a wall until the marker flashed.
//
// Two marks, both from the Easy FPS art pack: a bullet hole on geometry, a burst of blood on
// anything that can take damage. Both are pooled and world-space, the same shape TracerRenderer
// uses, because a shotgun puts eight of these on screen in one trigger pull and the pack's own
// approach — Instantiate per hit, Destroy 0.8s later — would churn garbage at that rate.
//
// A scene singleton rather than a per-player component: marks belong to the WORLD, they outlive
// the shot, and a player who dies mid-flight should not take their bullet holes with them.
public class ImpactFx : MonoBehaviour
{
    const string HolePrefab = "EasyFPS/bulletHole";
    const string BloodPrefab = "EasyFPS/bloodEffectParticlePrefab_melee";

    [Tooltip("Concurrent bullet holes. Oldest is recycled once the pool is full, which is why " +
             "this is a look-and-feel number rather than a correctness one: too low and a " +
             "shotgun blast erases its own pattern before you can read it.")]
    public int holePool = 48;
    [Tooltip("Concurrent blood bursts. Far fewer than holes — blood is a half-second particle, " +
             "not a mark that stays on the wall.")]
    public int bloodPool = 8;
    [Tooltip("Seconds a bullet hole stays before fading out. Long enough to read your own " +
             "spread, short enough that a long match does not end up wallpapered.")]
    public float holeLife = 20f;

    static ImpactFx instance;

    GameObject[] holes;
    float[] holeHideAt;
    int nextHole;

    ParticleSystem[] bloods;
    int nextBlood;

    // Found or created on first use. Nothing in the scene has to remember to place it, which
    // matches how every other visual in this project comes into being.
    static ImpactFx Instance
    {
        get
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<ImpactFx>();
            if (instance == null) instance = new GameObject("ImpactFx").AddComponent<ImpactFx>();
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        BuildHoles();
        BuildBlood();
    }

    void BuildHoles()
    {
        var prefab = Resources.Load<GameObject>(HolePrefab);
        if (prefab == null) return;

        holes = new GameObject[holePool];
        holeHideAt = new float[holePool];
        for (int i = 0; i < holePool; i++)
        {
            var holder = new GameObject("HoleHolder");
            holder.SetActive(false);
            var go = Instantiate(prefab, holder.transform);
            // The pack's hole ships with a BoxCollider and a self-destruct script. The
            // collider is the dangerous one: a decal that sits in the world on the default
            // layer is a surface hitscan can hit, so unstripped it would let players build a
            // wall of bullet holes and shoot them instead of each other.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) DestroyImmediate(mb);
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            holes[i] = go;
            Destroy(holder);
        }
    }

    void BuildBlood()
    {
        var prefab = Resources.Load<GameObject>(BloodPrefab);
        if (prefab == null) return;

        bloods = new ParticleSystem[bloodPool];
        for (int i = 0; i < bloodPool; i++)
        {
            var holder = new GameObject("BloodHolder");
            holder.SetActive(false);
            var go = Instantiate(prefab, holder.transform);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) DestroyImmediate(mb);
            go.transform.SetParent(transform, false);
            var ps = go.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null)
            {
                var main = ps.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None; // pooled: never self-destruct
            }
            bloods[i] = ps;
            Destroy(holder);
        }
    }

    // A shot that hit the world. Offset along the normal so the decal does not z-fight the
    // surface it is stuck to.
    public static void Hole(Vector3 point, Vector3 normal) => Instance.ShowHole(point, normal);

    // A shot that hit something alive. Sprayed back along the surface normal — towards the
    // shooter, which is where the person who needs to see it is standing.
    public static void Blood(Vector3 point, Vector3 normal) => Instance.ShowBlood(point, normal);

    void ShowHole(Vector3 point, Vector3 normal)
    {
        if (holes == null || holes.Length == 0) return;
        int i = nextHole;
        nextHole = (nextHole + 1) % holes.Length;

        var go = holes[i];
        if (go == null) return;
        go.transform.SetPositionAndRotation(point + normal * 0.01f,
                                            Quaternion.LookRotation(-normal));
        go.SetActive(true);
        holeHideAt[i] = Time.time + holeLife;
    }

    void ShowBlood(Vector3 point, Vector3 normal)
    {
        if (bloods == null || bloods.Length == 0) return;
        int i = nextBlood;
        nextBlood = (nextBlood + 1) % bloods.Length;

        var ps = bloods[i];
        if (ps == null) return;
        ps.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
        ps.gameObject.SetActive(true);
        ps.Clear(true);   // a burst recycled early must not carry the last one's particles
        ps.Play(true);
    }

    void Update()
    {
        if (holes == null) return;
        for (int i = 0; i < holes.Length; i++)
            if (holes[i] != null && holes[i].activeSelf && Time.time > holeHideAt[i])
                holes[i].SetActive(false);
    }
}
