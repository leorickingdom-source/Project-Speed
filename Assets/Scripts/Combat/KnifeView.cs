using UnityEngine;

// First-person knife viewmodel: a blade in the corner of your view that swings when you
// attack. The project renders no weapon models at all, which was survivable while every
// weapon was a hitscan gun — the tracer told you what happened. A melee has no tracer and
// kills in one hit, so without something on screen the player has no way to know they are
// holding it, and dying to one looks like dying to nothing.
//
// Built from primitives at runtime, like every other visual in this project (CorpseFx,
// BlastFx, the head caps) — there is no art pipeline here, and a lit box angled correctly
// reads as "blade" at viewmodel scale.
//
// Owner-only by construction: it parents to the aim camera, which PlayerNetwork disables on
// remote players. Bystanders see the swing through the world-space slash instead.
public class KnifeView : MonoBehaviour
{
    // Resting pose, in camera space: low and right, angled across the view like a held blade.
    static readonly Vector3 RestPos = new Vector3(0.34f, -0.28f, 0.55f);
    static readonly Vector3 RestRot = new Vector3(15f, -18f, 28f);

    // Swing end pose. The arc is a sweep across and down — the direction the slash graphic
    // is drawn in, so the two read as the same motion.
    static readonly Vector3 SwingPos = new Vector3(-0.12f, -0.10f, 0.62f);
    static readonly Vector3 SwingRot = new Vector3(-25f, 32f, -55f);

    const float SwingOut = 0.09f;   // fast strike
    const float SwingBack = 0.22f;  // slower recovery, so the swing has a direction in time

    // Which viewmodel is in hand. The ball is not a weapon you choose — it is handed to you
    // by picking up the objective — so it needs its own shape rather than a re-skinned knife:
    // "am I holding the ball" has to be answerable without reading the ammo line.
    public enum Mode { None, Knife, Ball }

    Transform model;      // knife
    Transform ballModel;  // oddball
    Mode mode = Mode.None;
    float swingStartedAt = -99f;

    public void Build(Transform cam)
    {
        if (model != null || cam == null) return;

        var root = new GameObject("KnifeView");
        root.transform.SetParent(cam, false);
        model = root.transform;

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Blade";
        Destroy(blade.GetComponent<Collider>());   // a viewmodel must never be hit by anything
        blade.transform.SetParent(model, false);
        blade.transform.localScale = new Vector3(0.025f, 0.012f, 0.24f);
        blade.transform.localPosition = new Vector3(0f, 0f, 0.13f);
        Tint(blade, new Color(0.82f, 0.87f, 0.95f), 1.4f); // pale steel, faintly lit

        var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        grip.name = "Grip";
        Destroy(grip.GetComponent<Collider>());
        grip.transform.SetParent(model, false);
        grip.transform.localScale = new Vector3(0.03f, 0.03f, 0.1f);
        grip.transform.localPosition = new Vector3(0f, 0f, -0.03f);
        Tint(grip, new Color(0.18f, 0.18f, 0.2f), 0f);     // dark handle, so the blade reads

        // The ball, built alongside the blade and toggled with it. Held lower and centred —
        // a two-handed lump, not something you point.
        var ballRoot = new GameObject("BallView");
        ballRoot.transform.SetParent(cam, false);
        ballModel = ballRoot.transform;

        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Orb";
        Destroy(orb.GetComponent<Collider>());
        orb.transform.SetParent(ballModel, false);
        orb.transform.localScale = Vector3.one * 0.3f;
        Tint(orb, new Color(0.55f, 0.2f, 0.9f), 2f);   // the same violet as the world ball

        ApplyPose(0f);
        root.SetActive(false);
        ballRoot.SetActive(false);
    }

    static void Tint(GameObject go, Color c, float emission)
    {
        var m = go.GetComponent<Renderer>().material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
    }

    public void SetVisible(bool on) => SetMode(on ? Mode.Knife : Mode.None);

    public void SetMode(Mode m)
    {
        mode = m;
        if (model != null) model.gameObject.SetActive(m == Mode.Knife);
        if (ballModel != null) ballModel.gameObject.SetActive(m == Mode.Ball);
    }

    public void Swing() => swingStartedAt = Time.time;

    // LateUpdate so the camera has finished moving for the frame — a viewmodel driven in
    // Update lags the view by a frame and visibly swims when you flick.
    void LateUpdate()
    {
        if (mode == Mode.None) return;
        Transform held = mode == Mode.Ball ? ballModel : model;
        if (held == null || !held.gameObject.activeSelf) return;

        float t = Time.time - swingStartedAt;
        float k;
        if (t < 0f || t > SwingOut + SwingBack) k = 0f;
        else if (t < SwingOut) k = t / SwingOut;                              // strike out
        else k = 1f - (t - SwingOut) / SwingBack;                             // ease back

        ApplyPose(k * k * (3f - 2f * k)); // smoothstep, so neither end snaps
    }

    // Rest/swing poses. The ball sits lower and more central than the blade, and swings on a
    // shallower arc — it is heaved rather than slashed.
    void ApplyPose(float k)
    {
        if (model != null)
        {
            model.localPosition = Vector3.Lerp(RestPos, SwingPos, k);
            model.localRotation = Quaternion.Slerp(Quaternion.Euler(RestRot), Quaternion.Euler(SwingRot), k);
        }
        if (ballModel != null)
        {
            ballModel.localPosition = Vector3.Lerp(
                new Vector3(0.18f, -0.34f, 0.55f), new Vector3(-0.05f, -0.16f, 0.62f), k);
            ballModel.localRotation = Quaternion.Slerp(
                Quaternion.identity, Quaternion.Euler(0f, 40f, -30f), k);
        }
    }
}
