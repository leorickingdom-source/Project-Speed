using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Generates Assets/Scenes/Vault.unity — the first fully ENCLOSED map: floor, four walls and a
// CEILING. Exists because every shipped map is open sky, which quietly disables half the kit:
// rockets have no tight rooms where splash matters, and the grapple has no ceiling to hook.
//
// Built by SURGERY ON STACKS rather than from an empty scene, deliberately. A map scene is
// mostly things that are not geometry — MatchManager, GameMenu, KillFeed, MenuCamera, post
// volume, six spawn points, five Flashpoint spawns, objective spawns, six pads, four pickups,
// three bots — every one carrying serialized fields somebody already tuned. Recreating those
// by AddComponent means re-guessing every field; opening Stacks and saving-as keeps them all,
// and this script then deletes only the geometry and repositions the functional objects into
// the new space. Re-runnable: it rebuilds Vault.unity from current Stacks every time.
//
//   Unity.exe -quit -batchmode -nographics -projectPath <project>
//             -executeMethod VaultMapGenerator.Generate -logFile vault.log
public static class VaultMapGenerator
{
    const string SourceScene = "Assets/Scenes/Stacks.unity";
    const string OutputScene = "Assets/Scenes/Vault.unity";

    // Interior half-extent on X/Z. Walls sit just outside; the ceiling stays well inside the
    // rope's 55m reach from anywhere on the floor.
    //
    // 30 -> 40 and 16 -> 22 after playtest: "feels too cramped, keep hitting platforms". At 60
    // square a player crossed the entire map in 2.5s at swing speed, and a 16m lid put every
    // rope arc into the ceiling. The interior thinned out in the same pass -- floor area is up
    // 78% while the deck SHRANK -- so this is mostly open air rather than more building. Free
    // air above the deck goes from 10m to 14m, which is what an arc actually needs.
    const float Half = 40f;
    const float CeilingY = 22f;

    // Deck top surface. 6 -> 8: enough headroom underneath to run and swing through the low
    // room rather than duck across it, which was most of "keep hitting platforms".
    const float DeckY = 8f;

    // Every Stacks object that is MAP GEOMETRY, by name. Everything not listed survives.
    static readonly string[] StacksGeometry =
    {
        "Floor", "Wall_N", "Wall_S", "Wall_E", "Wall_W",
        "Top_SW", "Top_NE", "Mid_NE", "Mid_NW", "Mid_SE", "Mid_SW",
        "Ledge_N", "Ledge_S", "Ledge_E",
        "Cover_A", "Cover_B", "Cover_C", "Cover_D", "Cover_E", "Cover_F",
        "Beam_SW_Z", "Beam_SW_X", "Beam_NE_Z", "Beam_NE_X",
        "Pillar",
    };

    [MenuItem("Build/Generate Vault Map")]
    public static void Generate()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene, OutputScene);
            scene = SceneManager.GetActiveScene(); // handle follows the save-as

            var roots = scene.GetRootGameObjects().ToDictionary(g => g.name, g => g);
            foreach (string name in StacksGeometry)
                if (roots.TryGetValue(name, out var go)) { Object.DestroyImmediate(go); roots.Remove(name); }

            BuildShell();
            BuildInterior();
            PlaceFunctional(roots);
            Lighting(roots);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings();

            Debug.Log($"[Vault] generated {OutputScene}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Vault] FAILED: {e}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }

    // ---- geometry ------------------------------------------------------------------------

    static GameObject Block(string name, Vector3 center, Vector3 scale, float yawDeg = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = center;
        go.transform.localScale = scale;
        if (yawDeg != 0f) go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        go.isStatic = true;
        return go;
    }

    static void BuildShell()
    {
        float t = 1f;                       // wall thickness
        float w = Half * 2f + t * 2f;       // outer span, corners covered
        Block("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(w, 1f, w));
        Block("Ceiling", new Vector3(0f, CeilingY + 0.5f, 0f), new Vector3(w, 1f, w));
        Block("Wall_N", new Vector3(0f, CeilingY * 0.5f, Half + t * 0.5f), new Vector3(w, CeilingY, t));
        Block("Wall_S", new Vector3(0f, CeilingY * 0.5f, -Half - t * 0.5f), new Vector3(w, CeilingY, t));
        Block("Wall_E", new Vector3(Half + t * 0.5f, CeilingY * 0.5f, 0f), new Vector3(t, CeilingY, w));
        Block("Wall_W", new Vector3(-Half - t * 0.5f, CeilingY * 0.5f, 0f), new Vector3(t, CeilingY, w));
    }

    static void BuildInterior()
    {
        // Central mezzanine deck on four pillars, with a low room beneath. The deck is the mid
        // fight; the room under it is the short-range pocket the shotgun and knife want.
        // 24 wide -> 20: at 24 it spanned 40% of the map at head height, and there was no way
        // to cross the middle without meeting it.
        Block("Deck", new Vector3(0f, DeckY - 0.25f, 0f), new Vector3(20f, 0.5f, 20f));
        Block("DeckPillar_A", new Vector3(8f, (DeckY - 0.5f) * 0.5f, 8f), new Vector3(1.2f, DeckY - 0.5f, 1.2f));
        Block("DeckPillar_B", new Vector3(-8f, (DeckY - 0.5f) * 0.5f, 8f), new Vector3(1.2f, DeckY - 0.5f, 1.2f));
        Block("DeckPillar_C", new Vector3(8f, (DeckY - 0.5f) * 0.5f, -8f), new Vector3(1.2f, DeckY - 0.5f, 1.2f));
        Block("DeckPillar_D", new Vector3(-8f, (DeckY - 0.5f) * 0.5f, -8f), new Vector3(1.2f, DeckY - 0.5f, 1.2f));

        // Two ramps onto the deck, opposite corners, so it is walkable without a pad. 25
        // degrees -- under the 55 slope limit, slideable both ways.
        var rampN = Block("Ramp_N", new Vector3(-10f, DeckY * 0.5f, 19f), new Vector3(6f, 0.5f, 19f));
        rampN.transform.rotation = Quaternion.Euler(-25f, 0f, 0f);
        var rampS = Block("Ramp_S", new Vector3(10f, DeckY * 0.5f, -19f), new Vector3(6f, 0.5f, 19f));
        rampS.transform.rotation = Quaternion.Euler(25f, 0f, 0f);

        // Roofed corridors along the E and W walls: an inner wall plus a roof over the lane
        // between it and the perimeter. The enclosed lanes are what this map exists for --
        // rocket splash with walls on three sides. Lane widened 8m -> 10m and the roof raised
        // to 7, so it reads as a room you fight in rather than a pipe you get stuck in.
        Block("CorridorWall_E", new Vector3(30f, 3.5f, 0f), new Vector3(1f, 7f, 40f));
        Block("CorridorRoof_E", new Vector3(35.25f, 7.25f, 0f), new Vector3(10.5f, 0.5f, 40f));
        Block("CorridorWall_W", new Vector3(-30f, 3.5f, 0f), new Vector3(1f, 7f, 40f));
        Block("CorridorRoof_W", new Vector3(-35.25f, 7.25f, 0f), new Vector3(10.5f, 0.5f, 40f));

        // Balconies along the N and S walls, pad- or rope-reached. High ground with 11m of
        // ceiling above it -- hookable from the balcony, and exposed to it.
        Block("Balcony_N", new Vector3(0f, 10.75f, 37f), new Vector3(60f, 0.5f, 6f));
        Block("Balcony_S", new Vector3(0f, 10.75f, -37f), new Vector3(60f, 0.5f, 6f));

        // Floor cover, yawed so no two sightlines feel identical. Four, not six, and out in the
        // corners: the pair that used to sit at z=+-20 stood between the ramps and the deck,
        // which is exactly where you are moving fastest.
        Block("Cover_A", new Vector3(24f, 1f, 24f), new Vector3(3f, 2f, 1.5f), 30f);
        Block("Cover_B", new Vector3(-24f, 1f, 24f), new Vector3(3f, 2f, 1.5f), -20f);
        Block("Cover_C", new Vector3(24f, 1f, -24f), new Vector3(3f, 2f, 1.5f), -45f);
        Block("Cover_D", new Vector3(-24f, 1f, -24f), new Vector3(3f, 2f, 1.5f), 15f);
    }

    // ---- functional objects kept from Stacks ---------------------------------------------

    static void Move(Dictionary<string, GameObject> roots, string name, Vector3 pos,
        float yawDeg = 0f, bool faceCenter = false)
    {
        if (!roots.TryGetValue(name, out var go))
        {
            Debug.LogWarning($"[Vault] missing '{name}' in source scene — skipped");
            return;
        }
        go.transform.position = pos;
        if (faceCenter)
        {
            Vector3 flat = new Vector3(-pos.x, 0f, -pos.z);
            if (flat.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(flat.normalized);
        }
        else go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    static void Pad(Dictionary<string, GameObject> roots, string name, Vector3 pos,
        float yawDeg, float up, float forward)
    {
        if (!roots.TryGetValue(name, out var go))
        {
            Debug.LogWarning($"[Vault] missing pad '{name}' — skipped");
            return;
        }
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        var pad = go.GetComponent<JumpPad>();
        if (pad != null) { pad.upForce = up; pad.forwardForce = forward; }
    }

    static void PlaceFunctional(Dictionary<string, GameObject> roots)
    {
        // Perimeter spawns at the floor, facing the centre, clear of the corridor mouths.
        Move(roots, "SpawnPoint_1", new Vector3(24f, 1f, 0f), faceCenter: true);
        Move(roots, "SpawnPoint_2", new Vector3(-24f, 1f, 0f), faceCenter: true);
        Move(roots, "SpawnPoint_3", new Vector3(0f, 1f, 33f), faceCenter: true);
        Move(roots, "SpawnPoint_4", new Vector3(0f, 1f, -33f), faceCenter: true);
        Move(roots, "SpawnPoint_5", new Vector3(26f, 1f, 26f), faceCenter: true);
        Move(roots, "SpawnPoint_6", new Vector3(-26f, 1f, -26f), faceCenter: true);

        // Pad launches, SOLVED rather than estimated -- every one of these was flown as a
        // ballistic arc against the real colliders and checked for a landing that is
        // descending, on the named surface, on a face with an upward normal.
        //
        // The estimate they replace was apex = up^2/2g, which answers "does it go high
        // enough" and not "does it get there". Every pad cleared its target's HEIGHT and
        // then hit the UNDERSIDE on the way up, because each sat too close to the platform
        // it served: the balcony pads clouted Balcony_N at y=10.5, the deck pads sailed past
        // the deck entirely and landed back on a ramp, and the corridor pads met the roof
        // slab from below. A pad that hits a ceiling is worse than no pad -- it takes your
        // momentum and gives back a stall.
        //
        // Each entry lands with at least 1.4m of platform margin, so a player who brings
        // their own speed still arrives on the surface rather than its lip.
        Pad(roots, "Pad_Mid", new Vector3(16f, 0f, -16f), -45f, 25f, 16f);   // deck centre
        Pad(roots, "Pad_Mid2", new Vector3(-16f, 0f, 16f), 135f, 25f, 16f);  // deck centre
        Pad(roots, "Pad_NE", new Vector3(24f, 0f, 28f), 0f, 25f, 7f);        // N balcony, 2.0m in
        Pad(roots, "Pad_NW", new Vector3(-24f, 0f, -28f), 180f, 25f, 7f);    // S balcony, 2.0m in
        Pad(roots, "Pad_MidNE", new Vector3(34f, 0f, 24f), 180f, 22f, 8f);   // E corridor roof
        Pad(roots, "Pad_MidSW", new Vector3(-34f, 0f, -24f), 0f, 22f, 8f);   // W corridor roof

        // Pickups: two deep in the corridors (earned by entering the knife-fight lane), two on
        // the balconies (earned by height).
        Move(roots, "Pickup_GndE", new Vector3(35f, 1f, 0f));
        Move(roots, "Pickup_GndW", new Vector3(-35f, 1f, 0f));
        Move(roots, "Pickup_TopNE", new Vector3(12f, 12f, 37f));
        Move(roots, "Pickup_TopSW", new Vector3(-12f, 12f, -37f));

        // Objectives. Rocket on the deck centre -- the contested high middle; flag under it in
        // the low room; oddball beside the rocket; Flashpoint ring across all levels.
        Move(roots, "RocketSpawn", new Vector3(0f, DeckY + 1f, 0f));
        Move(roots, "FlagSpawn", new Vector3(0f, 1f, 0f));
        Move(roots, "OddballSpawn", new Vector3(4f, DeckY + 1f, 0f));
        Move(roots, "FlashSpawn_1", new Vector3(33f, 1f, 33f));
        Move(roots, "FlashSpawn_2", new Vector3(-33f, 1f, 33f));
        Move(roots, "FlashSpawn_3", new Vector3(33f, 1f, -33f));
        Move(roots, "FlashSpawn_4", new Vector3(-33f, 1f, -33f));
        Move(roots, "FlashSpawn_5", new Vector3(0f, 12f, 37f));

        Move(roots, "Bot1", new Vector3(24f, 1f, -8f));
        Move(roots, "Bot2", new Vector3(-24f, 1f, 8f));
        Move(roots, "Bot3", new Vector3(6f, DeckY + 1f, 6f));

        // Connect-screen backdrop: high corner, looking across the deck.
        if (roots.TryGetValue("MenuCamera", out var cam))
        {
            cam.transform.position = new Vector3(34f, 17f, -34f);
            cam.transform.rotation = Quaternion.LookRotation(
                (new Vector3(0f, 6f, 0f) - cam.transform.position).normalized);
        }

        if (roots.TryGetValue("MapBounds", out var mb))
        {
            var bounds = mb.GetComponent<MapBounds>();
            if (bounds != null) { bounds.killDistance = 46f; bounds.killY = -10f; }
        }
    }

    // A directional light cannot reach an interior its own ceiling shadows, so shadows go off
    // (greybox pragmatism — light passes through) and flat ambient carries the rest.
    static void Lighting(Dictionary<string, GameObject> roots)
    {
        if (roots.TryGetValue("Directional Light", out var lightGo))
        {
            var l = lightGo.GetComponent<Light>();
            if (l != null) { l.shadows = LightShadows.None; l.intensity = 0.85f; }
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
    }

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == OutputScene)) return;
        scenes.Add(new EditorBuildSettingsScene(OutputScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
