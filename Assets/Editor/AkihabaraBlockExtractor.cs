using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// One-shot editor tool: pull the INDIVIDUAL buildings out of the Akihabara FBX and save each as its own
/// prefab, so the streaming spawner can line them up into a tight cramped streetfront along the road. The FBX
/// groups buildings into 9 district BLOCKS (Block_A..I), and each block child named `005339_08932_-…` is one
/// building. We flatten across all blocks, filter out tiny/road/ground bits by size, and bake each building
/// with a CLEAN pivot: centred in X/Z, base at Y=0, axis-aligned (its original district rotation is dropped —
/// the spawner re-orients it to face the road).
public static class AkihabaraBlockExtractor
{
    const string FbxPath = "Assets/ZRNAssets/005339_08932_25_14/Models/PQ_Remake_AKIHABARA.fbx";
    const string OutDir = "Assets/Prefabs/City";
    static readonly string[] BlockNames = { "Block_A","Block_B","Block_C","Block_D","Block_E","Block_F","Block_G","Block_H","Block_I" };

    // size gates (metres, in WORLD units after the FBX's 0.1 scale) — generous: keep anything building-ish,
    // only drop obvious tiny props and the giant merged ground/road slab. Over-including is fine.
    // A mesh is kept as a building if it's reasonably TALL, even on a small footprint (Akihabara has many
    // narrow towers). Tiny-AND-short meshes (props/signs/AC units/road bits) are dropped.
    const float MinFootprint = 1.5f;    // normal min footprint…
    const float SkinnyFootprint = 0.6f; // …but allow down to this IF the thing is tall (TallEnough)
    const float TallEnough = 4f;        // height that qualifies a narrow mesh as a real building
    const float MaxFootprint = 120f;    // only exclude the huge ground/road slab
    const float MinHeight = 2f;         // must be at least a shack tall

    [MenuItem("Bame Plastic/Buildings/1. Extract Akihabara Buildings → Prefabs")]
    static void Extract()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError("[Extractor] FBX not found at " + FbxPath); return; }

        // clean the output folder first (so re-runs don't pile up)
        if (Directory.Exists(OutDir)) AssetDatabase.DeleteAsset(OutDir);
        Directory.CreateDirectory(OutDir);
        AssetDatabase.Refresh();

        var src = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        if (src == null) { Debug.LogError("[Extractor] could not instantiate FBX."); return; }

        int saved = 0, tooSmall = 0, tooBig = 0, noRend = 0;
        // NO dedup — extract EVERY building object from EVERY block (a city reuses geometry; we want them all).
        try
        {
            foreach (string bn in BlockNames)
            {
                Transform block = FindDeep(src.transform, bn);
                if (block == null) continue;
                string tag = bn.Replace("Block_", "");          // A..I
                int idx = 0;
                for (int c = 0; c < block.childCount; c++)
                {
                    Transform bld = block.GetChild(c);
                    var rends = bld.GetComponentsInChildren<MeshRenderer>(true);
                    if (rends.Length == 0) { noRend++; continue; }

                    Bounds wb = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
                    float fp = Mathf.Min(wb.size.x, wb.size.z);
                    float fpMax = Mathf.Max(wb.size.x, wb.size.z);
                    // keep if: tall enough AND (normal footprint, OR narrow-but-tall like an Akihabara tower)
                    bool footprintOk = fp >= MinFootprint || (fp >= SkinnyFootprint && wb.size.y >= TallEnough);
                    if (wb.size.y < MinHeight || !footprintOk) { tooSmall++; continue; }
                    if (fpMax > MaxFootprint) { tooBig++; continue; }

                    if (BakeBuildingPrefab(bld, wb, $"{tag}_{idx:000}")) { saved++; idx++; }
                }
            }
        }
        finally { Object.DestroyImmediate(src); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Extractor] saved {saved} building prefab(s) to {OutDir}  |  skipped {tooSmall} small props/" +
                  $"signs/debris, {tooBig} oversized-slab, {noRend} no-mesh. NEXT: '2. Convert City Materials → " +
                  "URP', then '3. Add or Refresh Buildings On Road'.");
    }

    // clone the building, ROTATE it so its LONG footprint edge is axis-aligned to X (so the spawner can always
    // put that long side along the road), re-centre X/Z + drop base to Y=0, save as prefab.
    static bool BakeBuildingPrefab(Transform bld, Bounds worldBounds, string suffix)
    {
        var clone = Object.Instantiate(bld.gameObject);
        clone.name = "Bld_" + suffix;
        var root = new GameObject(clone.name);
        clone.transform.SetParent(root.transform, true);

        // FIND THE BEST YAW: the Akihabara meshes carry baked/diagonal orientation, so axis-aligned bounds lie.
        // Rotate the ROOT through 0..90° and pick the yaw that makes the footprint as WIDE-AND-SHALLOW as
        // possible (max X extent / min Z extent) — i.e. the long wall runs along X. Then that yaw is baked in.
        float bestYaw = 0f, bestScore = float.NegativeInfinity;
        for (float yaw = 0f; yaw < 90f; yaw += 5f)
        {
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Bounds bb = WorldBounds(clone);
            float score = bb.size.x - bb.size.z;          // want long X, short Z
            if (score > bestScore) { bestScore = score; bestYaw = yaw; }
        }
        root.transform.rotation = Quaternion.Euler(0f, bestYaw, 0f);

        // now re-centre X/Z and plant the base at Y=0 (in the chosen orientation)
        Bounds lb = WorldBounds(clone);
        Vector3 shift = new Vector3(-lb.center.x, -lb.min.y, -lb.center.z);
        clone.transform.position += shift;
        // bake the yaw into the geometry by zeroing the root rotation AFTER it's been applied to the child:
        // re-parent keeping world transform, then reset root.
        clone.transform.SetParent(null, true);
        root.transform.rotation = Quaternion.identity;
        clone.transform.SetParent(root.transform, true);

        string path = $"{OutDir}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return true;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<MeshRenderer>(true);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) { var r = FindDeep(root.GetChild(i), name); if (r != null) return r; }
        return null;
    }
}
