using UnityEditor;
using UnityEngine;

/// Sets sane import settings on the game audio in Resources/Sounds: long LOOPS (engine/ambient/radio) →
/// Streaming (don't hold the decompressed PCM in RAM); short one-shots → Decompress On Load (instant, no hitch).
/// Force-to-mono for the spatial/positional ones. Run once after adding clips. Safe to re-run.
public static class SoundImportSetup
{
    const string Dir = "Assets/Resources/Sounds";

    // long looping streams (big clips, played continuously) — stream from disk. The HORN is NOT streamed (it
    // must start the instant you press, with no streaming latency).
    static readonly string[] Streamed = { "engine_drive", "engine_idle", "ambient_city", "bus_radio" };

    [MenuItem("Bame Plastic/Audio ▸ Setup Sound Import Settings")]
    public static void Setup()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (ai == null) continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool streamed = System.Array.Exists(Streamed, s => s == name);

            var s = ai.defaultSampleSettings;
            s.loadType = streamed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = true;        // mono → half the data, and spatial sources need mono anyway
            ai.loadInBackground = streamed;
            ai.SaveAndReimport();
            n++;
        }
        Debug.Log($"[SoundImport] configured {n} clips in {Dir}.");
    }
}
