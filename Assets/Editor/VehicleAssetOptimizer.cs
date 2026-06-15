using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Tones down the imported vehicle art (pulled from random sources, some huge). Walks Assets/Art/Vehicles and:
///   • caps every texture at 512px (the bus-stop 4K maps were ~190MB alone),
///   • sets *_Normal maps to NormalMap import type,
///   • marks *_Roughness/_Metallic/_AmbientOcclusion/_Opacity as LINEAR (non-sRGB),
///   • enables crunch compression to shrink the build,
///   • caps mesh import (no blendshapes/anim, optimize, weld) on the FBX/GLB.
/// One-click, safe to re-run. (Decimation of the 103MB Rickshaw isn't possible from script — we just don't use
/// that one; the VehicleModelLibrary points at the lean Rickshaw 2 instead.)
public static class VehicleAssetOptimizer
{
    const string Root = "Assets/Art/Vehicles";
    const int MaxTexSize = 512;

    [MenuItem("Bame Plastic/Vehicles ▸ 1. Optimize Imported Assets")]
    public static void Optimize()
    {
        int tex = 0, mesh = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            if (FixTexture(AssetDatabase.GUIDToAssetPath(guid))) tex++;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            if (FixModel(AssetDatabase.GUIDToAssetPath(guid))) mesh++;

        // the BUSSID packs reference dead absolute texture paths → materials render WHITE. Use embedded
        // materials but REMAP their textures to the PNGs sitting in each pack's /textures folder by name.
        int packs = 0;
        packs += RemapPackTextures("Assets/Art/Vehicles/Traffic Pack 1/source/untitled.fbx", "Assets/Art/Vehicles/Traffic Pack 1/textures");
        packs += RemapPackTextures("Assets/Art/Vehicles/Traffic Pack 2/source/untitled.fbx", "Assets/Art/Vehicles/Traffic Pack 2/textures");

        AssetDatabase.Refresh();
        Debug.Log($"[VehicleOpt] capped {tex} textures at {MaxTexSize}px + fixed {mesh} models + remapped {packs} pack textures. " +
                  "Textures capped + pack materials re-textured. Prefabs live in Resources/Vehicles/<category>.");
    }

    static bool FixTexture(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return false;
        bool dirty = false;
        string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

        bool isNormal = lower.EndsWith("normal") || lower.EndsWith("_normal") || lower.Contains("normal");
        bool isLinearData = isNormal || lower.Contains("roughness") || lower.Contains("metallic")
                          || lower.Contains("occlusion") || lower.Contains("_ao") || lower.Contains("opacity");

        var want = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != want) { ti.textureType = want; dirty = true; }
        if (!isNormal)
        {
            bool sRGB = !isLinearData;        // colour maps sRGB; data maps linear
            if (ti.sRGBTexture != sRGB) { ti.sRGBTexture = sRGB; dirty = true; }
        }
        if (ti.maxTextureSize > MaxTexSize) { ti.maxTextureSize = MaxTexSize; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
        { ti.textureCompression = TextureImporterCompression.Compressed; dirty = true; }
        if (!ti.crunchedCompression) { ti.crunchedCompression = true; ti.compressionQuality = 50; dirty = true; }

        if (dirty) ti.SaveAndReimport();
        return dirty;
    }

    // Remap a pack FBX's embedded-material TEXTURE references to the PNGs in `texDir` (matched by base name),
    // so the BUSSID materials stop rendering white (their FBX points at dead D:\... paths). Returns # remapped.
    // The BUSSID packs reference dead absolute texture paths → white materials. Their MATERIAL names ARE the
    // texture page names (PBA / SAP / SAX / TrafficBody_01 / TrafficLight_01 / TrafficOther_01, with .00x dupes).
    // Fix: EXTRACT the materials to real .mat assets next to the FBX, then assign each material's _BaseMap to
    // the pack texture whose name matches the material's base name. Robust + bypasses the FBX remap entirely.
    static int RemapPackTextures(string fbxPath, string texDir)
    {
        var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (mi == null) return 0;

        // index the pack's textures by normalized base name (strip the ".png.006.png" doubled suffixes)
        var byName = new Dictionary<string, Texture2D>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t == null) continue;
            byName[NormalizeName(Path.GetFileName(p))] = t;
        }
        if (byName.Count == 0) return 0;

        // 1) make the FBX import materials, then EXTRACT each embedded (in-prefab) material to a real .mat asset
        //    via AssetDatabase.ExtractAsset (the supported API; the old materialLocation=External is deprecated).
        string matDir = Path.Combine(Path.GetDirectoryName(fbxPath), "materials").Replace('\\', '/');
        Directory.CreateDirectory(matDir);
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.SaveAndReimport();

        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is not Material embedded) continue;
            string dest = $"{matDir}/{SafeFile(embedded.name)}.mat";
            string err = AssetDatabase.ExtractAsset(embedded, dest);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[VehicleOpt] extract '{embedded.name}': {err}");
        }
        AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        // 2) assign each extracted material's base map by matching its NAME to a texture page
        int fixedN = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { matDir }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null) continue;
            Texture2D tex = BestMatch(NormalizeName(mat.name), byName);
            if (tex == null) continue;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);  // un-tint so the map shows
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(mat);
            fixedN++;
        }
        AssetDatabase.SaveAssets();
        return fixedN;
    }

    static string SafeFile(string n)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return string.IsNullOrEmpty(n) ? "mat" : n;
    }

    static string NormalizeName(string n)
    {
        n = n.ToLowerInvariant();
        n = n.Replace(".png", "").Replace(".jpeg", "").Replace(".jpg", "").Replace(".mat", "");
        for (int i = 0; i < 10; i++) n = n.Replace("." + i.ToString("000"), "").Replace("." + i, "");
        return n.Trim();
    }

    static Texture2D BestMatch(string key, Dictionary<string, Texture2D> byName)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (byName.TryGetValue(key, out var exact)) return exact;
        foreach (var kv in byName)
            if (kv.Key.Length > 2 && (kv.Key.Contains(key) || key.Contains(kv.Key))) return kv.Value;
        return null;
    }

    static bool FixModel(string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return false;
        bool dirty = false;
        if (mi.isReadable) { mi.isReadable = false; dirty = true; }   // runtime doesn't need CPU mesh access
        if (mi.importBlendShapes) { mi.importBlendShapes = false; dirty = true; }
        if (mi.importVisibility != false) { mi.importVisibility = false; dirty = true; }
        if (mi.importCameras) { mi.importCameras = false; dirty = true; }
        if (mi.importLights) { mi.importLights = false; dirty = true; }
        if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
        if (mi.meshCompression != ModelImporterMeshCompression.Medium) { mi.meshCompression = ModelImporterMeshCompression.Medium; dirty = true; }
        if (!mi.optimizeMeshPolygons) { mi.optimizeMeshPolygons = true; dirty = true; }
        if (!mi.optimizeMeshVertices) { mi.optimizeMeshVertices = true; dirty = true; }
        if (!mi.weldVertices) { mi.weldVertices = true; dirty = true; }
        if (dirty) mi.SaveAndReimport();
        return dirty;
    }
}
