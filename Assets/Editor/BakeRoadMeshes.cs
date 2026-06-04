using System.IO;
using UnityEngine;
using UnityEditor;

/// One-click bake for the procedural road pieces (TrackCurved, RevisedTrack).
///
/// WHY: ProBuilder roads rebuild their mesh at runtime (their prefab MeshFilter ships empty), which
/// costs CPU every time a chunk is created — that's the residual spawn hitch. This saves the current
/// built mesh as a real asset, points the MeshFilter/MeshCollider at it, and strips the ProBuilder
/// components so it never rebuilds again. The road looks identical but is now a cheap static mesh.
///
/// WHEN TO USE: this is a FINAL optimization — run it once the road GEOMETRY is locked. During
/// development keep using the generator's "Prewarm Per Variant" instead (that keeps full ProBuilder
/// editing).
///
/// EDITING AFTER BAKING:
///   * Textures & lane lines are MATERIAL work — always editable, baking changes nothing there.
///   * To change road GEOMETRY again: this stripped ProBuilder, so get the ProBuilder version back
///     (revert the prefab in Plastic, or keep a duplicate "_Source" prefab before baking), edit it,
///     then run this bake again. The baked mesh asset is overwritten in place (same GUID), so the
///     runtime prefab picks up the new shape automatically.
///
/// USAGE: open the road prefab in Prefab Mode (TrackCurved, and the LevelBlock that holds
/// RevisedTrack), select the road object (or the prefab root), then
/// Tools > Bame Plastic > Bake Selected Road Meshes. Save the prefab afterwards (Ctrl+S).
public static class BakeRoadMeshes
{
    const string BakeFolder = "Assets/Art/Models/BakedRoads";

    [MenuItem("Tools/Bame Plastic/Bake Selected Road Meshes")]
    static void BakeSelected()
    {
        if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Bake Road Meshes",
                "Select the road object(s) first.\n\nOpen the road prefab in Prefab Mode, then select the ProBuilder road (or the prefab root), and run this again.",
                "OK");
            return;
        }

        if (!Directory.Exists(BakeFolder)) Directory.CreateDirectory(BakeFolder);

        int baked = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (BakeMeshFilter(mf)) baked++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BakeRoadMeshes] Baked {baked} ProBuilder mesh(es) into {BakeFolder}. " +
                  "Save the open prefab (Ctrl+S) to keep it.");
    }

    static bool BakeMeshFilter(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null) return false;
        GameObject go = mf.gameObject;

        // Only touch objects that are actually ProBuilder meshes.
        Component pbMesh = FindComponentByTypeName(go, "ProBuilderMesh");
        if (pbMesh == null) return false;

        // Save (or overwrite) a permanent Mesh asset. Overwriting by a fixed name keeps the asset's
        // GUID, so anything already referencing it picks up a re-bake without re-linking.
        string path = $"{BakeFolder}/{SafeName(go.name)}_baked.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Mesh bakedMesh;
        if (existing != null)
        {
            EditorUtility.CopySerialized(mf.sharedMesh, existing); // overwrite contents, keep GUID
            bakedMesh = existing;
        }
        else
        {
            bakedMesh = Object.Instantiate(mf.sharedMesh);
            bakedMesh.name = go.name + "_baked";
            AssetDatabase.CreateAsset(bakedMesh, path);
        }

        // Point the renderer + collider at the baked asset.
        mf.sharedMesh = bakedMesh;
        MeshCollider mc = go.GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = bakedMesh;

        // Strip ProBuilder (found by type name, so this file has no compile dependency on the package)
        // so the mesh never rebuilds at runtime.
        Component poly = FindComponentByTypeName(go, "PolyShape");
        if (poly != null) Object.DestroyImmediate(poly);
        Object.DestroyImmediate(pbMesh);

        // Re-assert in case ProBuilder cleared the filter when its component was destroyed.
        mf.sharedMesh = bakedMesh;
        if (mc != null) mc.sharedMesh = bakedMesh;

        EditorUtility.SetDirty(go);
        return true;
    }

    static Component FindComponentByTypeName(GameObject go, string typeName)
    {
        foreach (Component c in go.GetComponents<Component>())
            if (c != null && c.GetType().Name == typeName) return c;
        return null;
    }

    static string SafeName(string n)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n.Replace(' ', '_');
    }
}
