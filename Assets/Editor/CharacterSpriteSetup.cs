using UnityEditor;
using UnityEngine;

/// Prepares all character art in Resources/Characters for the billboard pipeline: imports every PNG as a
/// readable Sprite with a BOTTOM-CENTRE pivot (so a character's feet sit on the ground), point filtering
/// (crisp pixel art), full alpha. The CharacterSheetN pages stay as single full-rect sprites (960×960) and are
/// SLICED into the 4×2 = 8 character cells AT RUNTIME by CharacterSprites (reading the texture), so no manual
/// sprite-editor slicing is needed. Run once after adding/replacing character art. Re-runnable.
public static class CharacterSpriteSetup
{
    const string Root = "Assets/Resources/Characters";

    [MenuItem("Bame Plastic/Characters ▸ Setup Character Sprites")]
    public static void Setup()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            if (Fix(AssetDatabase.GUIDToAssetPath(guid))) n++;
        AssetDatabase.Refresh();
        Debug.Log($"[CharacterSetup] configured {n} character sprites in {Root}. Sheets are sliced at runtime " +
                  "(4×2 = 8 chars/page; cell N across pages 1-9 = character N's walk cycle).");
    }

    static bool Fix(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return false;
        bool dirty = false;

        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (!ti.isReadable) { ti.isReadable = true; dirty = true; }          // runtime slicing reads pixels
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }   // crisp pixels
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
        if (ti.spritePixelsPerUnit != 100f) { ti.spritePixelsPerUnit = 100f; dirty = true; }

        // BOTTOM-CENTRE pivot so feet plant on the ground (matches the old procedural sprite pivot 0.5,0).
        // Sheets are full-rect (sliced in code) so their pivot doesn't matter; the individual frames need it.
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        if (s.spriteAlignment != (int)SpriteAlignment.BottomCenter)
        {
            s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            ti.SetTextureSettings(s);
            dirty = true;
        }

        // keep the source resolution (these are small) but cap + compress lightly for the web build
        if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }

        if (dirty) ti.SaveAndReimport();
        return dirty;
    }
}
