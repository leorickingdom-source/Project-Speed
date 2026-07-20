using UnityEngine;

// Draws shot tracers. Extracted out of WeaponController for one specific reason: that
// component is disabled on non-owners, which meant enemy shots produced no visual whatsoever —
// you could be fired on from across the map with nothing on screen to tell you.
//
// Kept ACTIVE on every player, local and remote, so a remote player's shots are drawn on their
// own object. The expiry sweep lives here too; on the old disabled component Update never ran,
// so any tracer drawn would have hung on screen forever.
public class TracerRenderer : MonoBehaviour
{
    [Tooltip("Concurrent tracers. Shotgun fires 8 pellets at once, so this needs headroom.")]
    public int poolSize = 16;
    public float width = 0.03f;

    LineRenderer[] pool;
    float[] hideAt;
    int next;

    void Awake()
    {
        pool = new LineRenderer[poolSize];
        hideAt = new float[poolSize];
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("Tracer" + i);
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.useWorldSpace = true;   // world space, so the line stays put as the shooter moves
            lr.numCapVertices = 2;
            lr.material = new Material(sh);
            lr.enabled = false;
            pool[i] = lr;
        }
    }

    public void Show(Vector3 from, Vector3 to, Color col, float seconds)
    {
        if (pool == null || pool.Length == 0) return;
        int i = next;
        next = (next + 1) % pool.Length;

        var lr = pool[i];
        lr.startColor = lr.endColor = col;
        if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", col);
        lr.material.color = col;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.enabled = true;
        hideAt[i] = Time.time + seconds;
    }

    void Update()
    {
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != null && pool[i].enabled && Time.time > hideAt[i]) pool[i].enabled = false;
    }
}
