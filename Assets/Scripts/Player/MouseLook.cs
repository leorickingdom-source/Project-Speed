using UnityEngine;

// Yaw on the player body, pitch on the child camera. Auto-wires references.
public class MouseLook : MonoBehaviour
{
    public InputReader input;
    public Transform body;   // yaw (player root)
    public Transform cam;    // pitch (child camera)

    [Tooltip("Degrees turned per mouse pixel.")]
    public float sensitivity = 0.08f;
    public float minPitch = -89f;
    public float maxPitch = 89f;

    float pitch;
    WeaponController weapon;

    // Exposed so InputReader can bake facing into InputCmd — the sim reads the command, not
    // the transform, so the server can reproduce a client's movement and aim. Body only yaws,
    // so eulerAngles.y is the exact horizontal facing.
    public float Yaw => body != null ? body.eulerAngles.y : 0f;
    public float Pitch => pitch;

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (body == null) body = transform;
        if (cam == null)
        {
            var c = GetComponentInChildren<Camera>();
            if (c != null) cam = c.transform;
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    bool releasedForMenu;

    void Update()
    {
        // While the connect screen is up the mouse belongs to the panel. Start() locks the
        // cursor on spawn, which on the editor's scene-placed test player meant the menu was
        // being clicked blind.
        if (ConnectUI.MenuOpen)
        {
            if (!releasedForMenu)
            {
                releasedForMenu = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }
        if (releasedForMenu)
        {
            releasedForMenu = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (input == null || Time.timeScale == 0f) return; // frozen while paused
        // Scoped aim is scaled by the magnification, so a given mouse movement covers the
        // same distance on screen either way. Unscaled, zooming in would multiply your hand
        // tremor by the same factor as the target.
        float scale = weapon != null ? weapon.ScopeSensitivityScale : 1f;
        Vector2 d = input.LookDelta * sensitivity * scale;
        if (body != null) body.Rotate(0f, d.x, 0f, Space.World);
        if (cam != null)
        {
            pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
