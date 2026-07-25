using UnityEngine;

// Per-map out-of-bounds limits. One of these sits in a map scene; PlayerHealth reads it
// instead of its own serialized defaults.
//
// Necessary the moment maps stopped being the same size. PlayerHealth.killDist was tuned for
// Arena — 48, just past its walls at 45.5 — and that number lives on the PLAYER prefab, which
// is shared by every map. On a 150m arena it would kill anyone who walked more than a third
// of the way out, in open space, for no visible reason. A limit that describes the map has to
// be stored with the map.
public class MapBounds : MonoBehaviour
{
    [Tooltip("Square half-extent from the origin on X and Z. Set just past the perimeter " +
             "walls: this is the net that catches players launched OVER them, so it should " +
             "trigger soon after they clear the wall rather than after a long silent arc.")]
    public float killDistance = 48f;

    [Tooltip("Fall below this world Y and you die. Floor top is y=0 on every map so far.")]
    public float killY = -10f;

    static MapBounds instance;
    static float nextProbeAt;

    void Awake() => instance = this;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // Rate-limited lookup: a map may legitimately have none (then callers keep their own
    // defaults), and searching the scene every frame for something that is not there is the
    // expensive way to learn that. Re-probed rather than latched so a map change is picked up.
    public static MapBounds Current
    {
        get
        {
            if (instance == null && Time.unscaledTime >= nextProbeAt)
            {
                instance = FindAnyObjectByType<MapBounds>();
                nextProbeAt = Time.unscaledTime + 2f;
            }
            return instance;
        }
    }
}
