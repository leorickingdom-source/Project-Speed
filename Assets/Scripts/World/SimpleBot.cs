using UnityEngine;

// Dumb ground chaser: walks toward the player on flat ground. Shootable via its
// Health (respawns). Placeholder enemy for the movement playground — no nav mesh,
// no collision avoidance yet.
[RequireComponent(typeof(Health))]
public class SimpleBot : MonoBehaviour
{
    public float moveSpeed = 4.5f;
    public float stopDistance = 2f;
    public float turnSpeed = 8f;

    Transform target;

    void Start()
    {
        var pm = FindAnyObjectByType<PlayerMotor>();
        if (pm != null) target = pm.transform;
    }

    void Update()
    {
        if (target == null) return;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist > stopDistance && dist > 0.01f)
        {
            Vector3 dir = to / dist;
            transform.position += dir * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }
    }
}
