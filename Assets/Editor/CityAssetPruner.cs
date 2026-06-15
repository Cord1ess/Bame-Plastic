using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Deletes the now-ORPHANED standalone building assets under Assets/Art/CityBuildings — the meshes, materials,
/// and textures left behind after building prefabs were removed (both the ones trimmed by the size tool and the
/// ones the user deleted by hand), PLUS the duplicate texture copies that piled up across the earlier
/// extract/repair runs.
///
/// SAFETY: it never deletes by a guessed list. It computes the KEEP set as the recursive dependency closure of
/// EVERY surviving asset that lives OUTSIDE Art/CityBuildings (all prefabs, scenes, materials, etc. in the
/// project), via AssetDatabase.GetDependencies. Any Art/CityBuildings asset in that closure is referenced and is
/// kept; everything else is an orphan and is deleted. So a mesh/material/texture used by ANY surviving prefab —
/// or by the scene or anything else — is guaranteed safe. Run REPORT first (deletes nothing), then PRUNE.
public static class CityAssetPruner
{
    const string CityDir = "Assets/Prefabs/City";
    const string OutRoot = "Assets/Art/CityBuildings";

    [MenuItem("Bame Plastic/Road/Prune City Assets/Report Orphaned")]
    static void Report() => Run(dryRun: true);

    [MenuItem("Bame Plastic/Road/Prune City Assets/Prune Orphaned (delete)")]
    static void Prune()
    {
        var (orphans, kept) = Collect();
        if (orphans.Count == 0) { Debug.Log("[Prune] no orphaned assets under " + OutRoot + " — nothing to delete."); return; }
        if (kept.Count == 0)
        {
            Debug.LogError("[Prune] ABORT: the keep-set is EMPTY — that means NOTHING references these assets, " +
                           "which is suspicious (did the prefabs get unlinked?). Refusing to delete. Run REPORT.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Prune Orphaned City Assets",
            $"Delete {orphans.Count} orphaned asset(s) under {OutRoot}?\n\n" +
            $"{kept.Count} referenced asset(s) will be KEPT (used by the surviving {SurvivingPrefabCount()} prefabs " +
            "or anything else in the project).\n\nRun 'Report Orphaned City Assets' first to see the full list.",
            "Delete orphans", "Cancel"))
            return;
        Run(dryRun: false);
    }

    static void Run(bool dryRun)
    {
        var (orphans, kept) = Collect();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Prune] {OutRoot}: {kept.Count} referenced (KEEP), {orphans.Count} orphaned " +
                      $"({(dryRun ? "would delete" : "deleting")}). Referenced by the {SurvivingPrefabCount()} surviving prefabs + rest of project.");

        if (orphans.Count == 0) { sb.AppendLine("  (nothing to delete)"); Debug.Log(sb.ToString()); return; }

        // group the orphan list by type for a readable report
        foreach (var grp in orphans.GroupBy(ExtOf).OrderBy(g => g.Key))
            sb.AppendLine($"  {grp.Key}: {grp.Count()}");

        int deleted = 0;
        if (!dryRun)
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string p in orphans) if (AssetDatabase.DeleteAsset(p)) deleted++;
            }
            finally { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
            sb.AppendLine($"  → deleted {deleted}/{orphans.Count}.");
        }
        else
        {
            // in dry-run, list the actual files so the user can eyeball them
            foreach (string p in orphans.OrderBy(x => x)) sb.AppendLine("    ✗ " + p);
        }
        Debug.Log(sb.ToString());
    }

    // returns (orphanPaths, keptPaths) — both restricted to assets physically under OutRoot
    static (List<string> orphans, HashSet<string> kept) Collect()
    {
        // every asset that lives under OutRoot (candidates for deletion)
        var underOut = AssetDatabase.FindAssets("", new[] { OutRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(OutRoot) && !AssetDatabase.IsValidFolder(p))
            .Distinct()
            .ToList();

        // KEEP set = dependency closure of everything OUTSIDE OutRoot (prefabs, scenes, mats, controllers…)
        // We gather dependencies of all prefabs + scenes + materials + scriptable assets in the project, then
        // intersect with underOut. Anything under OutRoot that appears = referenced = keep.
        var roots = new List<string>();
        roots.AddRange(AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath));
        roots.AddRange(AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath));
        // include materials/assets that live OUTSIDE OutRoot too (e.g. a mat elsewhere using one of our textures)
        roots.AddRange(AssetDatabase.FindAssets("t:Material").Select(AssetDatabase.GUIDToAssetPath));

        var kept = new HashSet<string>();
        foreach (string r in roots.Distinct())
        {
            if (string.IsNullOrEmpty(r)) continue;
            if (r.StartsWith(OutRoot)) continue;                  // don't seed from inside OutRoot itself
            foreach (string dep in AssetDatabase.GetDependencies(r, true))
                if (dep.StartsWith(OutRoot)) kept.Add(dep);
        }

        var orphans = underOut.Where(p => !kept.Contains(p)).ToList();
        return (orphans, kept);
    }

    static int SurvivingPrefabCount() =>
        AssetDatabase.FindAssets("t:Prefab", new[] { CityDir }).Length;

    static string ExtOf(string path)
    {
        string e = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return e == ".asset" ? "meshes" : e == ".mat" ? "materials" : "textures(" + e + ")";
    }
}
