using UnityEngine;
using System.Collections;

// Simple destructible target that respawns so you can keep practicing.
public class Health : MonoBehaviour
{
    public float maxHp = 100f;
    public float respawnDelay = 3f;

    float hp;
    Renderer rend;
    Collider col;

    void Awake()
    {
        hp = maxHp;
        rend = GetComponent<Renderer>();
        col = GetComponent<Collider>();
    }

    public void Damage(float amount)
    {
        if (hp <= 0f) return;
        hp -= amount;
        if (hp <= 0f) StartCoroutine(DownThenRespawn());
    }

    IEnumerator DownThenRespawn()
    {
        if (rend) rend.enabled = false;
        if (col) col.enabled = false;
        yield return new WaitForSeconds(respawnDelay);
        hp = maxHp;
        if (rend) rend.enabled = true;
        if (col) col.enabled = true;
    }
}
