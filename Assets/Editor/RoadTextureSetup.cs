using System.IO;
using UnityEditor;
using UnityEngine;

/// One-click setup for the road surface texture. We use ONLY Road012A (clean asphalt — no painted lane lines,
/// which clashed with the game's own procedural lane markings and tiled badly). Fixes the import settings
/// (normal → NormalMap, roughness → linear), builds a single URP/Lit material at Assets/Art/Materials/RoadBlend.mat
/// scaled UP so the grain reads as asphalt (not stretched lines), and assigns it to every TiledRoadStreamer's
/// `roadMaterial`. Re-run any time after changing the texture. (Safe to re-run.)
public static class RoadTextureSetup
{
    const string Set = "Assets/Art/Road Texture/Road012A_1K-PNG/Road012A_1K-PNG";
    const string MatPath = "Assets/Art/Materials/RoadBlend.mat";   // kept the same path so existing refs hold

    // along-road texture repeat: the road UVs are (across, along-in-metres). ~1 repeat / 6m along reads as
    // real asphalt; across the ~16m road we want a few repeats so it isn't blown up into one giant splotch.
    static readonly Vector2 Tiling = new Vector2(200.0f, 16.0f);   // (across, along) — 100x denser grain

    [MenuItem("Bame Plastic/Road ▸ Setup Road Texture")]
    public static void Setup()
    {
        string color = Set + "_Color.png";
        string norm  = Set + "_NormalGL.png";
        string rough = Set + "_Roughness.png";
        string ao    = Set + "_AmbientOcclusion.png";

        FixColor(color);
        FixLinear(norm, isNormal: true);
        FixLinear(rough, isNormal: false);
        if (File.Exists(ao)) FixLinear(ao, isNormal: false);

        var texColor = AssetDatabase.LoadAssetAtPath<Texture2D>(color);
        var texNorm  = AssetDatabase.LoadAssetAtPath<Texture2D>(norm);
        var texAo    = AssetDatabase.LoadAssetAtPath<Texture2D>(ao);
        if (texColor == null) { Debug.LogError($"[RoadSetup] missing albedo: {color}"); return; }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { Debug.LogError("[RoadSetup] URP/Lit shader not found."); return; }

        Directory.CreateDirectory("Assets/Art/Materials");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, MatPath); }
        else mat.shader = lit;

        mat.SetTexture("_BaseMap", texColor);
        mat.SetTextureScale("_BaseMap", Tiling);
        if (texNorm != null)
        {
            mat.SetTexture("_BumpMap", texNorm);
            mat.SetTextureScale("_BumpMap", Tiling);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 0.8f);
        }
        if (texAo != null)
        {
            mat.SetTexture("_OcclusionMap", texAo);
            mat.SetTextureScale("_OcclusionMap", Tiling);
            mat.EnableKeyword("_OCCLUSIONMAP");
            mat.SetFloat("_OcclusionStrength", 1f);
        }
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);

        int assigned = 0;
        foreach (var streamer in Object.FindObjectsByType<TiledRoadStreamer>(FindObjectsInactive.Include))
        {
            streamer.roadMaterial = mat;
            EditorUtility.SetDirty(streamer);
            assigned++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RoadSetup] Road012A asphalt material built ({MatPath}); assigned to {assigned} TiledRoadStreamer(s). " +
                  "Press Play. Adjust the tiling on the material if the grain looks too big/small.");
    }

    static void FixColor(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool dirty = false;
        if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; dirty = true; }
        if (!ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) ti.SaveAndReimport();
    }

    static void FixLinear(string path, bool isNormal)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool dirty = false;
        var want = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != want) { ti.textureType = want; dirty = true; }
        if (!isNormal && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }   // roughness/AO are linear data
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) ti.SaveAndReimport();
    }
}
