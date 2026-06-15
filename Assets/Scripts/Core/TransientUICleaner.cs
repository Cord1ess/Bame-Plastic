using UnityEngine;

/// SELF-HEALING guard against UI/preview junk getting embedded in a scene. Anything the menu/atmosphere
/// systems create at runtime (the menu canvas, crew, fog ring, a spawned EventSystem) should NEVER be saved
/// into the scene — but a past [ExecuteAlways] bug baked some in, and that kind of mistake is easy to repeat.
/// This sweeps those orphans away on every scene load (play AND, via the editor companion, edit mode), so a
/// stray object can't persist or accumulate. You never have to delete Library/ScriptAssemblies to fix it.
///
/// It removes objects by KNOWN RUNTIME NAMES (the names our spawners use). Real, hand-placed scene objects
/// are never touched.
public static class TransientUICleaner
{
    // names of objects that are ALWAYS runtime-spawned and must never be SAVED into a scene. (Live preview
    // copies carry HideFlags.DontSave and are left alone — we only strip ones that got truly serialized.)
    static readonly string[] OrphanNames =
    {
        "MenuCanvas", "MenuUI", "SpeedoCanvas", "HUDCanvas", "GuideStrip", "FogRing (runtime)",
        // these are runtime-spawned under the road/managers; if SERIALIZED (no DontSave) they're baked junk.
        // The DontSave-skip above protects the LIVE edit-preview copies, so only baked ones are stripped.
        "Tiles", "Traffic", "Rivals", "Pedestrians", "CityBlocks", "RoadBarrier", "PoliceHazards",
    };
    static readonly string[] OrphanPrefixes = { "MenuCrew_" };

    /// Sweep orphaned UI/preview objects that got SERIALIZED into the active scene (edit mode only — at
    /// runtime the spawners manage their own objects). Only removes saved objects matching the runtime names;
    /// live DontSave preview objects and real scene objects are never touched.
    public static int Sweep()
    {
        if (Application.isPlaying) return 0;
        int removed = 0;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
        {
            if (go == null || go.transform.parent != null) continue;       // roots only
            if ((go.hideFlags & HideFlags.DontSave) != 0) continue;        // a live preview object — leave it
            if (!MatchesName(go.name)) continue;                           // not one of ours
            Object.DestroyImmediate(go);                                   // it was baked into the scene → strip it
            removed++;
        }
        return removed;
    }

    static bool MatchesName(string n)
    {
        for (int i = 0; i < OrphanNames.Length; i++) if (n == OrphanNames[i]) return true;
        for (int i = 0; i < OrphanPrefixes.Length; i++) if (n.StartsWith(OrphanPrefixes[i])) return true;
        return false;
    }
}
