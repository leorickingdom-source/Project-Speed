using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps PlayerSpawner pointed at the CURRENT map's spawn points.
//
// Necessary because of a sharp edge in changing maps: PlayerSpawner.Spawns is an array of
// scene Transforms, and FishNet keeps NetworkManager alive across scene loads while the scene
// those Transforms belonged to is destroyed. The array is then full of dead references, and
// both the initial spawn and PlayerHealth's respawn fall back to the prefab position — which
// on any map is a single fixed point, so everyone would pile onto the same spot.
//
// Rebinding on every scene load keeps spawning correct no matter how many times the map
// changes. Matches by name prefix so a new map only has to name its objects SpawnPoint_*.
[RequireComponent(typeof(FishNet.Component.Spawning.PlayerSpawner))]
public class SpawnPointBinder : MonoBehaviour
{
    public string prefix = "SpawnPoint_";

    FishNet.Component.Spawning.PlayerSpawner spawner;

    void Awake()
    {
        spawner = GetComponent<FishNet.Component.Spawning.PlayerSpawner>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rebind();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene s, LoadSceneMode mode) => Rebind();

    void Rebind()
    {
        if (spawner == null) return;

        var found = new System.Collections.Generic.List<Transform>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.name.StartsWith(prefix)) found.Add(t);

        // Stable order, so spawn assignment is not at the mercy of scene traversal order.
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Only overwrite if this map actually has points — otherwise a scene without any
        // would silently wipe a working array.
        if (found.Count > 0) spawner.Spawns = found.ToArray();
    }
}
