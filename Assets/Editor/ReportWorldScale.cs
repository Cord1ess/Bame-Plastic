using System.Text;
using UnityEngine;
using UnityEditor;

/// Tools > Bame Plastic > Report World Scale
/// Prints the real world-space size (renderer bounds) of the bus and each road-chunk variant, plus
/// their transform scales and the generator's chunkSize — so we can calibrate a clean 1 unit = 1 m
/// rescale instead of guessing. Read-only: it instantiates chunks to measure them, then deletes them.
public static class ReportWorldScale
{
    [MenuItem("Tools/Bame Plastic/Report World Scale")]
    static void Report()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Bame Plastic — World Scale Report ===");

        // --- Bus ---
        BusController bus = Object.FindFirstObjectByType<BusController>();
        float busLen = 0f;
        if (bus != null)
        {
            Bounds all = CombinedBounds(bus.gameObject);            // everything (incl. trails/particles)
            Bounds body = MeshBounds(bus.gameObject);               // solid body only — the real size
            busLen = Mathf.Max(body.size.x, body.size.z);
            sb.AppendLine($"BUS '{bus.name}' BODY size = {Fmt(body.size)}  longestHoriz = {busLen:F2}  (lossyScale {Fmt(bus.transform.lossyScale)})");
            sb.AppendLine($"   (all renderers incl. trails/particles = {Fmt(all.size)})");
        }
        else
        {
            sb.AppendLine("BUS: no BusController found in the open scene.");
        }

        // --- Chunks (via the generator's data) ---
        LevelLayoutGenerator gen = Object.FindFirstObjectByType<LevelLayoutGenerator>();
        if (gen != null && gen.levelChunkData != null)
        {
            foreach (var data in gen.levelChunkData)
            {
                if (data == null) continue;
                sb.AppendLine($"\nChunkData '{data.name}': chunkSize = {data.chunkSize.x} x {data.chunkSize.y}");
                if (data.levelChunks == null) continue;
                foreach (var prefab in data.levelChunks)
                {
                    if (prefab == null) continue;
                    GameObject inst = (GameObject)Object.Instantiate(prefab);
                    Bounds b = MeshBounds(inst);
                    sb.AppendLine($"   '{prefab.name}': tile size = {Fmt(b.size)}  (root lossyScale {Fmt(inst.transform.lossyScale)})");
                    int rc;
                    Bounds road = NamedMeshBounds(inst, RoadKeys, out rc);
                    if (rc > 0)
                        sb.AppendLine($"      road piece ({rc} mesh): size = {Fmt(road.size)}  -> width ~{Mathf.Min(road.size.x, road.size.z):F1}");
                    else
                        sb.AppendLine("      road piece: nothing named track/revised/road found");
                    Object.DestroyImmediate(inst);
                }
            }
        }
        else
        {
            sb.AppendLine("\nNo LevelLayoutGenerator (with chunk data) found in the open scene.");
        }

        // --- Suggested factor ---
        if (busLen > 0f)
        {
            float factor = 12f / busLen;
            sb.AppendLine($"\nFor a ~12 m bus, scale factor = 12 / {busLen:F2} = {factor:F5}");
            sb.AppendLine($"-> new chunkSize would be ~{1600f * factor:F1} (currently 1600).");
        }
        sb.AppendLine("Apply the same factor to: chunk scale, chunkSize, bus scale, camera distance/height/near-clip, and BusController speeds/forces.");

        Debug.Log(sb.ToString());
    }

    static Bounds CombinedBounds(GameObject go)
    {
        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    // Solid body only — ignores trails, particles, line/billboard renderers that balloon the bounds.
    static Bounds MeshBounds(GameObject go)
    {
        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        bool has = false;
        Bounds b = default;
        foreach (Renderer r in rends)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? b : new Bounds(go.transform.position, Vector3.zero);
    }

    static readonly string[] RoadKeys = { "track", "revised", "road" };

    // Bounds of just the road mesh(es) (objects whose name or an ancestor's name matches a keyword).
    static Bounds NamedMeshBounds(GameObject root, string[] keys, out int count)
    {
        count = 0;
        bool has = false;
        Bounds b = default;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (!NameMatches(r.transform, keys, root.transform)) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
            count++;
        }
        return has ? b : new Bounds(root.transform.position, Vector3.zero);
    }

    static bool NameMatches(Transform t, string[] keys, Transform stopAt)
    {
        Transform cur = t;
        while (cur != null)
        {
            string n = cur.name.ToLowerInvariant();
            for (int i = 0; i < keys.Length; i++) if (n.Contains(keys[i])) return true;
            if (cur == stopAt) break;
            cur = cur.parent;
        }
        return false;
    }

    static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
}
