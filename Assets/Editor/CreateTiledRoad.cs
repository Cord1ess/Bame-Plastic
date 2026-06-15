using UnityEngine;
using UnityEditor;

/// One-click setup for the bulletproof POOLED-TILE endless road. Drops a single GameObject with
/// RoadZone + TiledRoadStreamer + SplineStopSpawner, and a FloatingOrigin if the scene lacks one.
/// Streams fixed-length road tiles around the bus — each lofted once and pooled, so there's no
/// whole-road rebuild and effectively zero streaming lag. Press Play with the bus in the scene.
///
/// Menu: Bame Plastic ▸ Create Tiled Road (Fast)
public static class CreateTiledRoad
{
    [MenuItem("Bame Plastic/Road/1. Create Tiled Road (Fast)")]
    static void Create()
    {
        GameObject go = new GameObject("TiledRoad");
        Undo.RegisterCreatedObjectUndo(go, "Create Tiled Road");

        go.AddComponent<RoadZone>();
        var streamer = go.AddComponent<TiledRoadStreamer>();
        go.AddComponent<SplineStopSpawner>();   // Phase C: bus stops + crowds (binds to the streamer)
        go.AddComponent<FootpathPedestrians>(); // L5: pedestrians walking the footpath that join stops as fares
        go.AddComponent<TrafficSystem>();       // Phase C / L1: deterministic kinematic traffic both ways
        go.AddComponent<RivalManager>();        // L4: deploys rival buses (TrafficVehicle + RivalBrain) that camp stops
        go.AddComponent<PoliceHazard>();        // hazard: periodic police checkpoints — slow to 45km/h or be fined
        var buildings = go.AddComponent<BuildingSpawner>();   // streets: streamed Akihabara block wall both sides
        SeedBlocks(buildings, freshlyAdded: true);
        go.AddComponent<RoadBarrier>();         // invisible wall past the footpath so the bus can't drive off

        // Driver guide line ("eagle-vision" optimal path through traffic). Auto-finds bus/road/traffic.
        var guideGo = new GameObject("DriverGuide");
        guideGo.transform.SetParent(go.transform, false);
        guideGo.AddComponent<DriverGuide>();    // self-adds a LineRenderer; aims a reactive guide line at gaps

        streamer.RebuildEditorPreview();         // build tiles + seat the bus on the left lane, in the Scene view

#if UNITY_2022_2_OR_NEWER
        if (Object.FindAnyObjectByType<FloatingOrigin>() == null)
#else
        if (Object.FindObjectOfType<FloatingOrigin>() == null)
#endif
        {
            GameObject fo = new GameObject("FloatingOrigin");
            fo.AddComponent<FloatingOrigin>();
            Undo.RegisterCreatedObjectUndo(fo, "Create Floating Origin");
        }

        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();
        Debug.Log("[CreateTiledRoad] Tiled road created. Press Play with the bus in the scene — it streams " +
                  "pooled tiles forever with no rebuild lag. Set a Seed for a repeatable layout; flip " +
                  "'Use Burst' on TiledRoadStreamer once you've confirmed it looks right, for max headroom.");
    }

    // Add (or refresh) the BuildingSpawner on the EXISTING road in the open scene — so you get the building
    // wall without recreating the road (keeps your tuning). Re-seeds the block prefab list too.
    [MenuItem("Bame Plastic/Road/2. Add or Refresh Buildings On Road")]
    static void AddBuildingsToExistingRoad()
    {
        var road = Object.FindAnyObjectByType<TiledRoadStreamer>();
        if (road == null) { Debug.LogWarning("[Buildings] No TiledRoad in the scene."); return; }
        var spawner = road.GetComponent<BuildingSpawner>();
        bool fresh = spawner == null;
        if (fresh) spawner = Undo.AddComponent<BuildingSpawner>(road.gameObject);
        SeedBlocks(spawner, fresh);
        if (road.GetComponent<RoadBarrier>() == null) Undo.AddComponent<RoadBarrier>(road.gameObject);
        if (road.GetComponent<PoliceHazard>() == null) Undo.AddComponent<PoliceHazard>(road.gameObject);
        Selection.activeGameObject = road.gameObject;
    }

    // Fill the spawner's building list from the extracted Akihabara building prefabs (Assets/Prefabs/City/Bld_*).
    static void SeedBlocks(BuildingSpawner spawner, bool freshlyAdded)
    {
        // When the spawner is FIRST added, set a sane real height — the extracted prefabs are tiny at native
        // scale, so leaving targetHeight at 0 makes them specks. On a refresh we DON'T touch it (keep user tuning).
        if (freshlyAdded && spawner.targetHeight < 0.01f) spawner.targetHeight = 28f;

        const string dir = "Assets/Prefabs/City";
        if (!System.IO.Directory.Exists(dir))
        {
            Debug.LogWarning("[Buildings] No building prefabs at " + dir + " — expected the Bld_* prefabs there.");
            return;
        }
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileName(path).StartsWith("Bld_")) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) list.Add(prefab);
        }
        spawner.blockPrefabs = list;
        EditorUtility.SetDirty(spawner);
        Debug.Log($"[Buildings] BuildingSpawner seeded with {list.Count} building prefab(s).");
    }
}
