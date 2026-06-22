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

    void Awake()
    {
        if (input == null) input = GetComponent<InputReader>();
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

    void Update()
    {
        if (input == null) return;
        Vector2 d = input.LookDelta * sensitivity;
        if (body != null) body.Rotate(0f, d.x, 0f, Space.World);
        if (cam != null)
        {
            pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
