using UnityEngine;

/// The ZONING / DATA layer for a road chunk — the single source of truth for what's drivable, what's
/// the divider, and where the footpaths are. Geometry + textures are built separately to match this
/// (select the chunk to see the gizmo blueprint). Gameplay queries this, never guesses:
///   • bus stops + crowds + stalls spawn on the FOOTPATHS
///   • traffic / rivals drive in the LANES (oncoming vs your direction)
///   • "is the bus on the road?" / police / AI use IsDrivable + lane centres
///
/// Road runs along the chunk's local +Z; the cross-section is laid out along local X, centred on the
/// median at X = 0. Bangladesh is LEFT-hand traffic, so your forward lanes are on the -X (left) side.
public class RoadZone : MonoBehaviour
{
    [Header("Cross-section (metres; road runs along local +Z)")]
    [Tooltip("Chunk length along Z — set to your actual chunk length.")]
    public float roadLength = 40f;
    public int lanesPerDirection = 2;
    public float laneWidth = 3.25f;
    [Tooltip("Central divider width (non-drivable).")]
    public float medianWidth = 1.5f;
    [Tooltip("Footpath / sidewalk width each side (pedestrians, stalls, stops).")]
    public float footpathWidth = 2.5f;
    [Tooltip("Left-hand traffic (Bangladesh): your forward lanes are on the LEFT (-X). Untick for right-hand.")]
    public bool leftHandTraffic = true;

    public float MedianHalf => medianWidth * 0.5f;
    public float DriveHalf => MedianHalf + lanesPerDirection * laneWidth;   // outer edge of the lanes
    public float RoadHalf => DriveHalf + footpathWidth;                     // outer edge incl. footpath
    public float TotalWidth => RoadHalf * 2f;

    /// Local X of a lane centre. forward = your direction; laneIndex 0 = innermost (beside the median).
    public float LaneCenterX(int laneIndex, bool forward)
    {
        float sideSign = (forward == leftHandTraffic) ? -1f : 1f;   // forward+LHT → left (-X)
        return sideSign * (MedianHalf + (Mathf.Clamp(laneIndex, 0, lanesPerDirection - 1) + 0.5f) * laneWidth);
    }

    /// World position at a lane centre, z01 in [0,1] along the chunk.
    public Vector3 LaneCenterWorld(int laneIndex, bool forward, float z01)
    {
        float z = Mathf.Lerp(-roadLength * 0.5f, roadLength * 0.5f, Mathf.Clamp01(z01));
        return transform.TransformPoint(new Vector3(LaneCenterX(laneIndex, forward), 0f, z));
    }

    /// Is a local-X position on a drivable lane (not the median, not the footpath)?
    public bool IsDrivable(float localX)
    {
        float ax = Mathf.Abs(localX);
        return ax >= MedianHalf && ax <= DriveHalf;
    }

    public bool IsDrivableWorld(Vector3 worldPos) => IsDrivable(transform.InverseTransformPoint(worldPos).x);

    /// A random local point on a footpath (leftSide = the -X side). For spawning stops / crowd / stalls.
    public Vector3 RandomFootpathLocal(bool leftSide)
    {
        float sign = leftSide ? -1f : 1f;
        float x = sign * Random.Range(DriveHalf + 0.2f, RoadHalf - 0.2f);
        float z = Random.Range(-roadLength * 0.5f, roadLength * 0.5f);
        return new Vector3(x, 0f, z);
    }

    public Vector3 RandomFootpathWorld(bool leftSide) => transform.TransformPoint(RandomFootpathLocal(leftSide));

    // ---- Gizmo blueprint: select the chunk to see lanes (grey), median (yellow), footpaths (green),
    // and lane centre lines (white dashed). Build + texture your geometry to match these. ----
    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        float hl = roadLength * 0.5f;

        // Footpaths (green)
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.35f);
        DrawBand(-RoadHalf, -DriveHalf, hl);
        DrawBand(DriveHalf, RoadHalf, hl);

        // Lanes (grey) — both sides
        Gizmos.color = new Color(0.55f, 0.55f, 0.6f, 0.35f);
        DrawBand(-DriveHalf, -MedianHalf, hl);
        DrawBand(MedianHalf, DriveHalf, hl);

        // Median / divider (yellow)
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
        DrawBand(-MedianHalf, MedianHalf, hl);

        // Lane centre lines (white)
        Gizmos.color = Color.white;
        for (int i = 0; i < lanesPerDirection; i++)
        {
            float xF = LaneCenterX(i, true);
            float xO = LaneCenterX(i, false);
            Gizmos.DrawLine(new Vector3(xF, 0.02f, -hl), new Vector3(xF, 0.02f, hl));
            Gizmos.DrawLine(new Vector3(xO, 0.02f, -hl), new Vector3(xO, 0.02f, hl));
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    void DrawBand(float x0, float x1, float halfLen)
    {
        float cx = (x0 + x1) * 0.5f;
        float w = Mathf.Abs(x1 - x0);
        Gizmos.DrawCube(new Vector3(cx, 0f, 0f), new Vector3(w, 0.04f, halfLen * 2f));
    }
}
