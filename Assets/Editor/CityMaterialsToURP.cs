using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// Converts the Akihabara city pack's materials from the BUILT-IN render pipeline to URP/Lit so they don't
/// render pink in this URP project. Handles two cases:
///   1. Standard/Legacy materials → Unity's built-in→URP upgrader path (URP/Lit, copies _MainTex/_Color).
///   2. The pack's CUSTOM built-in shaders (StandardDoubleClip, su_Double_Clip, su_VertexCol_1UV_Single) →
///      remapped to URP/Lit by hand, preserving the base map + flagging the transparent/clip ones as
///      alpha-clipped double-sided (Akihabara has lots of cut-out signage/decals).
///
/// Scoped to the pack folder only. Idempotent — safe to re-run. Run via the menu AFTER extracting blocks.
public static class CityMaterialsToURP
{
    const string PackDir = "Assets/ZRNAssets";
    static readonly string[] CustomBuiltinShaders = { "StandardDoubleClip", "su_Double_Clip", "su_VertexCol_1UV_Single" };

    [MenuItem("Bame Plastic/Buildings/2. Convert City Materials → URP")]
    static void Convert()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) { Debug.LogError("[CityURP] URP/Lit shader not found — is URP installed?"); return; }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { PackDir });
        int converted = 0, clipped = 0, skipped = 0;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            string sh = mat.shader.name;

            // already URP? leave it.
            if (sh.StartsWith("Universal Render Pipeline/")) { skipped++; continue; }

            bool isBuiltinStandard = sh == "Standard" || sh == "Standard (Specular setup)" || sh.StartsWith("Legacy Shaders/");
            bool isCustomBuiltin = System.Array.IndexOf(CustomBuiltinShaders, sh) >= 0;
            if (!isBuiltinStandard && !isCustomBuiltin) { skipped++; continue; }

            // capture the source look BEFORE swapping the shader (property IDs differ per shader)
            Texture baseTex = GetTex(mat, "_MainTex") ?? GetTex(mat, "_BaseMap");
            Color baseCol = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            // transparency hint: name has TransP / the shader clips, OR the source had a cutout/fade mode
            bool wantsClip = path.Contains("TransP") || mat.name.Contains("TransP") ||
                             sh == "StandardDoubleClip" || sh == "su_Double_Clip";

            mat.shader = urpLit;
            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", baseCol);

            if (wantsClip)
            {
                // alpha-clipped cut-out signage/fences. Use alpha-to-coverage for clean (non-shimmering) edges,
                // a lower cutoff so thin features aren't eaten, and ZWrite on so it sorts solidly (no ghosting).
                // BACK-FACE CULL (not double-sided): double-sided clip is the main cause of the see-through
                // "ghosting" on these — back faces fighting front. If a sign needs both sides it's rare here.
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.35f);
                mat.SetFloat("_AlphaToMask", 1f);
                mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
                mat.SetFloat("_ZWrite", 1f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                clipped++;
            }
            else
            {
                mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            }
            // city buildings are matte; kill the default URP shine so they don't look plastic
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", 1f);   // metallic workflow
            EditorUtility.SetDirty(mat);
            converted++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[CityURP] converted {converted} material(s) to URP/Lit ({clipped} alpha-clipped), skipped {skipped}. " +
                  "If any building still looks off, it's likely a transparency/decal material to hand-tune.");
    }

    static Texture GetTex(Material m, string prop)
    {
        return m.HasProperty(prop) ? m.GetTexture(prop) : null;
    }
}
