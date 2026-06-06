using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// Lofts a 4-lane road mesh (footpaths + lanes + median, with curbs) along a Unity SplineContainer,
/// using the widths from a RoadZone. This is the reusable core — the endless generator will feed it
/// procedurally-built splines later. For now: draw a SplineContainer, add this + a RoadZone, assign
/// materials, and hit Rebuild to see the road.
///
/// Cross-section (local right axis), centred on the median; curbs raise the footpaths + median.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SplineRoadLoft : MonoBehaviour
{
    public SplineContainer spline;
    public RoadZone zone;
    [Tooltip("Loft resolution: a cross-section ring roughly every N metres.")]
    public float metersPerSample = 2f;
    public float curbHeight = 0.15f;
    public Material roadMaterial;
    public Material footpathMaterial;

    void Reset() { spline = GetComponent<SplineContainer>(); zone = GetComponent<RoadZone>(); }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (spline == null) spline = GetComponent<SplineContainer>();
        if (zone == null) zone = GetComponent<RoadZone>();
        if (spline == null) { Debug.LogWarning("[SplineRoadLoft] No SplineContainer."); return; }

        float mh = zone != null ? zone.MedianHalf : 0.75f;
        float dh = zone != null ? zone.DriveHalf : 7.25f;
        float rh = zone != null ? zone.RoadHalf : 9.75f;
        float h = curbHeight;

        // Cross-section points (rightOffset, upOffset), left → right. Curbs raise footpaths + median.
        Vector2[] prof =
        {
            new Vector2(-rh, h),   // 0 left footpath outer
            new Vector2(-dh, h),   // 1 left footpath inner (curb top)
            new Vector2(-dh, 0f),  // 2 curb face down to road
            new Vector2(-mh, 0f),  // 3 left lanes
            new Vector2(-mh, h),   // 4 median left up
            new Vector2( mh, h),   // 5 median top
            new Vector2( mh, 0f),  // 6 median right down
            new Vector2( dh, 0f),  // 7 right lanes
            new Vector2( dh, h),   // 8 right curb top
            new Vector2( rh, h),   // 9 right footpath outer
        };
        // submesh per segment j (prof[j]→prof[j+1]): 0 = road (drivable), 1 = footpath/median/curb
        int[] seg = { 1, 1, 0, 1, 1, 1, 0, 1, 1 };
        int P = prof.Length;

        float length = spline.CalculateLength();
        if (length < 0.01f) { Debug.LogWarning("[SplineRoadLoft] Spline has no length."); return; }
        int rings = Mathf.Max(2, Mathf.RoundToInt(length / Mathf.Max(0.25f, metersPerSample)) + 1);

        Vector3[] verts = new Vector3[rings * P];
        Vector2[] uvs = new Vector2[rings * P];

        for (int i = 0; i < rings; i++)
        {
            float t = i / (float)(rings - 1);
            spline.Evaluate(t, out float3 fp, out float3 ft, out float3 fu);
            Vector3 pos = transform.InverseTransformPoint((Vector3)fp);
            Vector3 fwd = transform.InverseTransformDirection(((Vector3)ft));
            Vector3 up = transform.InverseTransformDirection(((Vector3)fu));
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; else fwd.Normalize();
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up; else up.Normalize();
            Vector3 right = Vector3.Cross(up, fwd).normalized;

            for (int j = 0; j < P; j++)
            {
                verts[i * P + j] = pos + right * prof[j].x + up * prof[j].y;
                uvs[i * P + j] = new Vector2(j / (float)(P - 1), t * length * 0.1f);
            }
        }

        List<int> road = new List<int>();
        List<int> path = new List<int>();
        for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < P - 1; j++)
            {
                int a = i * P + j, b = i * P + j + 1, c = (i + 1) * P + j + 1, d = (i + 1) * P + j;
                List<int> tl = seg[j] == 0 ? road : path;
                tl.Add(a); tl.Add(c); tl.Add(b);
                tl.Add(a); tl.Add(d); tl.Add(c);
            }

        Mesh mesh = new Mesh { name = "SplineRoad" };
        mesh.indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(road, 0);
        mesh.SetTriangles(path, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = GetComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { roadMaterial, footpathMaterial };

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = null; mc.sharedMesh = mesh;

        Debug.Log($"[SplineRoadLoft] Built {rings} rings over {length:0.0}m. Width ≈ {rh * 2f:0.0}m.");
    }
}
