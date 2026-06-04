using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// Tools > Bame Plastic > Apply World Scale (chunks)
/// Shrinks the road chunks + chunkSize together so 1 unit = 1 m and the road is proportional to the
/// (already-correct) 12 m bus. Measured baseline: chunks render at root scale 80 with chunkSize 1600.
///
/// The bus, camera, and physics are intentionally NOT touched — the bus is already real-world scale;
/// only the chunks were oversized. Idempotent (computes targets from the fixed baseline, not the
/// current values), so it's safe to re-run, and you can retune by editing Factor.
public static class ApplyWorldScale
{
    const float Factor = 0.25f;          // matches "bus 0.2 on scale-4 chunks" with the bus kept at real 12 m
                                         // (bus 0.2:chunk 4 == bus 1:chunk 20). chunk scale 20, chunkSize 400.
    const float BaseChunkScale = 80f;    // measured current chunk root lossyScale
    const float BaseChunkSize = 1600f;   // measured current chunkSize

    [MenuItem("Tools/Bame Plastic/Apply World Scale (chunks)")]
    static void Apply()
    {
        float newScale = BaseChunkScale * Factor;   // 4
        float newSize = BaseChunkSize * Factor;      // 80

        if (!EditorUtility.DisplayDialog("Apply World Scale",
            $"Rescale ALL road chunks + chunkSize by factor {Factor}:\n" +
            $"   chunk root scale  {BaseChunkScale} -> {newScale}\n" +
            $"   chunkSize         {BaseChunkSize} -> {newSize}\n\n" +
            "The bus, camera, and physics are NOT touched.\n\nMake sure you have a Plastic checkpoint first. Continue?",
            "Apply", "Cancel")) return;

        LevelLayoutGenerator gen = Object.FindFirstObjectByType<LevelLayoutGenerator>();
        if (gen == null || gen.levelChunkData == null)
        {
            Debug.LogError("[ApplyWorldScale] No LevelLayoutGenerator with chunk data in the open scene.");
            return;
        }

        HashSet<string> donePrefabs = new HashSet<string>();
        int prefabs = 0, sos = 0;

        foreach (LevelChunkData data in gen.levelChunkData)
        {
            if (data == null) continue;
            data.chunkSize = new Vector2(newSize, newSize);
            EditorUtility.SetDirty(data);
            sos++;

            if (data.levelChunks == null) continue;
            foreach (GameObject prefab in data.levelChunks)
            {
                if (prefab == null) continue;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || donePrefabs.Contains(path)) continue;
                donePrefabs.Add(path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                root.transform.localScale = new Vector3(newScale, newScale, newScale);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                prefabs++;
            }
        }

        // Pre-placed first chunk(s) sitting in the scene: rescale their roots too (idempotent).
        int sceneChunks = 0;
        HashSet<Transform> roots = new HashSet<Transform>();
        foreach (TriggerExit te in Object.FindObjectsByType<TriggerExit>(FindObjectsSortMode.None))
            roots.Add(te.transform.root);
        foreach (Transform r in roots)
        {
            r.localScale = new Vector3(newScale, newScale, newScale);
            EditorUtility.SetDirty(r.gameObject);
            sceneChunks++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ApplyWorldScale] factor {Factor}: {prefabs} chunk prefab(s) -> scale {newScale}, " +
                  $"{sos} chunkSize -> {newSize}, {sceneChunks} scene chunk(s). Bus/camera/physics untouched.\n" +
                  "If the bus now feels too fast across the smaller chunks, lower BusController.acceleration (30 -> ~12).");
    }
}
