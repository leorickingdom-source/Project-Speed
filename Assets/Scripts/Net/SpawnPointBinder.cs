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
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Rebind();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode mode) => Rebind();

    // Also on UNLOAD. A map change loads the new scene before dropping the old one, so for a
    // moment both are in memory and both answer to SpawnPoint_* — rebinding only on load can
    // leave the spawner holding the outgoing map's points. Rebinding again once they are gone
    // guarantees the array ends up describing the map that is actually there.
    void OnSceneUnloaded(Scene s) => Rebind();

    void Rebind()
    {
        if (spawner == null) return;

        // Points in the ACTIVE scene only, when it has any. During a map change the outgoing
        // scene is still loaded and its SpawnPoint_1..6 are indistinguishable by name from the
        // incoming map's — an unfiltered scan mixes the two, and a player handed one of the
        // stale entries spawns at the old map's coordinates inside the new one, which is to
        // say outside it. Falling back to an unfiltered scan keeps single-scene setups (the
        // editor's SampleScene, a dedicated server mid-load) working exactly as before.
        Scene active = SceneManager.GetActiveScene();
        var found = new System.Collections.Generic.List<Transform>();
        var anyScene = new System.Collections.Generic.List<Transform>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || !t.name.StartsWith(prefix)) continue;
            anyScene.Add(t);
            if (t.gameObject.scene == active) found.Add(t);
        }
        if (found.Count == 0) found = anyScene;

        // Stable order, so spawn assignment is not at the mercy of scene traversal order.
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Only overwrite if this map actually has points — otherwise a scene without any
        // would silently wipe a working array.
        if (found.Count > 0) spawner.Spawns = found.ToArray();
    }
}
