using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// Tools > Bame Plastic > Thin Chunk Bases
/// Each chunk's base is a "GroundCube" slab that extends well below the road, making the chunks look
/// fat. This trims it from the BOTTOM: shrinks the GroundCube's height to ThinFactor of its current
/// value while keeping its TOP (the road surface) exactly where it is — so nothing the bus drives on
/// moves. Scale-independent (works whether chunks are at scale 4, 20, etc.). Run on a Plastic checkpoint.
public static class ThinChunkBases
{
    const string GroundName = "GroundCube";
    const float ThinFactor = 0.1f;   // keep 10% of the current base thickness (edit + re-run to taste)

    [MenuItem("Tools/Bame Plastic/Thin Chunk Bases")]
    static void Thin()
    {
        if (!EditorUtility.DisplayDialog("Thin Chunk Bases",
            $"Trim every chunk's '{GroundName}' to {ThinFactor:P0} of its current thickness (from the bottom; the road surface stays put).\n\nMake a Plastic checkpoint first. Continue?",
            "Thin", "Cancel")) return;

        LevelLayoutGenerator gen = Object.FindFirstObjectByType<LevelLayoutGenerator>();
        if (gen == null || gen.levelChunkData == null)
        {
            Debug.LogError("[ThinChunkBases] No LevelLayoutGenerator with chunk data in the open scene.");
            return;
        }

        HashSet<string> done = new HashSet<string>();
        int count = 0;
        foreach (LevelChunkData data in gen.levelChunkData)
        {
            if (data == null || data.levelChunks == null) continue;
            foreach (GameObject prefab in data.levelChunks)
            {
                if (prefab == null) continue;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || done.Contains(path)) continue;
                done.Add(path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                Transform gc = FindByName(root.transform, GroundName);
                Renderer r = gc != null ? gc.GetComponent<Renderer>() : null;
                if (r != null)
                {
                    float topBefore = r.bounds.center.y + r.bounds.extents.y;
                    Vector3 s = gc.localScale;
                    gc.localScale = new Vector3(s.x, s.y * ThinFactor, s.z);
                    float topAfter = r.bounds.center.y + r.bounds.extents.y;
                    gc.position += new Vector3(0f, topBefore - topAfter, 0f);   // keep the top (road surface) fixed
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                else
                {
                    Debug.LogWarning($"[ThinChunkBases] '{GroundName}' with a Renderer not found in {prefab.name} — skipped.");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ThinChunkBases] Thinned {count} chunk base(s) to {ThinFactor:P0} thickness (top kept fixed).");
    }

    static Transform FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform found = FindByName(c, name);
            if (found != null) return found;
        }
        return null;
    }
}
