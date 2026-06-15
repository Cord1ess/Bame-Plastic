using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Fixes the bus-stop shelter: its FBX (BusStop_L) references dead texture paths → renders UNTEXTURED. This
/// extracts the FBX materials to real .mat assets and assigns each one's maps (BaseColor/Normal) from the
/// bus-stop /textures folder by NAME (Ads/Roof/Seats/Things/Walls), then rebuilds the Resources prefab
/// (Vehicles/Props/busstop) base-seated. Rotation + scale are applied at PLACEMENT in SplineStopSpawner.
/// Run once: Bame Plastic ▸ Characters/Vehicles ▸ Fix Bus Stop.
public static class BusStopSetup
{
    const string Fbx = "Assets/Art/Vehicles/bus-stop/source/BusStop_L.fbx";
    const string TexDir = "Assets/Art/Vehicles/bus-stop/textures";
    const string OutPrefab = "Assets/Resources/Vehicles/Props/busstop.prefab";

    [MenuItem("Bame Plastic/Vehicles ▸ Fix Bus Stop (texture + prefab)")]
    public static void Fix()
    {
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if (mi == null) { Debug.LogError("[BusStop] FBX not found: " + Fbx); return; }

        // index textures by normalized base name
        var byName = new Dictionary<string, Texture2D>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t != null) byName[Norm(Path.GetFileName(p))] = t;
        }

        // 1) import materials in-prefab, then EXTRACT each to a real .mat we can edit
        string matDir = "Assets/Art/Vehicles/bus-stop/source/materials";
        Directory.CreateDirectory(matDir);
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.SaveAndReimport();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(Fbx))
        {
            if (obj is not Material m) continue;
            string dest = $"{matDir}/{Safe(m.name)}.mat";
            AssetDatabase.ExtractAsset(m, dest);
        }
        AssetDatabase.WriteImportSettingsIfDirty(Fbx);
        AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        // 2) assign each extracted material's maps by matching its NAME to a texture part
        int fixedN = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { matDir }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null) continue;
            string key = Norm(mat.name);
            var basecol = Best(key + "basecolor", byName) ?? Best(key, byName);
            var normal  = Best(key + "normal", byName);
            if (basecol != null) { if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", basecol); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", basecol); }
            if (normal != null && mat.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (basecol != null) fixedN++;
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();

        // 3) rebuild the prefab from the now-textured FBX, base-seated
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (src == null) { Debug.LogError("[BusStop] can't load FBX as GameObject"); return; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        var root = new GameObject("busstop");
        try
        {
            inst.transform.SetParent(root.transform, false);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            if (TryBounds(root, out Bounds b))
                inst.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);   // base-seat, centre X/Z
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            Directory.CreateDirectory(Path.GetDirectoryName(OutPrefab));
            PrefabUtility.SaveAsPrefabAsset(root, OutPrefab);
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(inst); }

        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log($"[BusStop] textured {fixedN} materials + rebuilt {OutPrefab}. Rotation/scale come from SplineStopSpawner.");
    }

    static string Norm(string n)
    {
        n = n.ToLowerInvariant().Replace(".png", "").Replace(".jpg", "").Replace(".jpeg", "").Replace(".mat", "");
        for (int i = 0; i < 10; i++) n = n.Replace("." + i.ToString("000"), "").Replace("." + i, "");
        return n.Replace(" ", "").Trim();
    }
    static string Safe(string n) { foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return string.IsNullOrEmpty(n) ? "mat" : n; }
    static Texture2D Best(string key, Dictionary<string, Texture2D> map)
    {
        if (map.TryGetValue(key, out var e)) return e;
        foreach (var kv in map) if (kv.Key.Length > 2 && kv.Key.Contains(key)) return kv.Value;
        return null;
    }
    static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default; bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        return has;
    }
}
