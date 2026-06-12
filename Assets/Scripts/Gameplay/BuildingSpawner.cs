using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// Streams a tight, GAPLESS city street-wall down both sides of the endless road. Buildings line up edge-to-edge
/// on an OFFSET SPLINE just past the footpath, each oriented to follow the road's local tangent, base on the
/// ground. Pooled; recycled behind the bus. PLAY-ONLY (no [ExecuteAlways]).
///
/// ARCHITECTURE (the rewrite that fixed all the "starts ahead / gaps / on the footpath" pain): placement marches
/// the road's OWN WORLD CENTRELINE POLYLINE (TiledRoadStreamer.GetCenterline) — it is COMPLETELY INDEPENDENT of
/// the bus's tracked road-position (_busTileF / SampleRoad), which lags ~120m at spawn and was the root cause of
/// every desync. Buildings are anchored to road geometry in world space, so they can never drift relative to the
/// road. The offset spline uses the PER-KNOT perpendicular (matching the road mesh's cross-section), so fronts
/// sit exactly off the footpath edge on curves — never on the road/footpath. Gapless spacing comes from marching
/// the offset polyline by world arc-length; a sagitta push-out keeps the rigid box faces from poking roadward on
/// convex curves. Cheap: ~one polyline build + a handful of incremental placements per frame.
[RequireComponent(typeof(TiledRoadStreamer))]
public class BuildingSpawner : MonoBehaviour
{
    [Header("Building prefabs (Bld_*; from the extractor)")]
    public List<GameObject> blockPrefabs = new List<GameObject>();

    [Header("Placement (offset-spline)")]
    [Tooltip("Gap (m) from the FOOTPATH edge (RoadZone.RoadHalf ≈17.5m) to the building fronts. Building fronts " +
             "sit on an offset spline at this lateral, off the road by construction. Small (~3) = hug the footpath.")]
    public float frontGap = 3f;
    [Tooltip("Target seam (m) between neighbours. 0 = touching; small negative = slight overlap (Akihabara-packed).")]
    [Range(-1.5f, 2f)] public float wallSeam = -0.8f;
    [Tooltip("Sink buildings this many m INTO the ground so they sit planted, not floating.")]
    public float groundSink = 0.4f;
    [Tooltip("Don't reuse the same building prefab within this many placements on a side (variety).")]
    public int noRepeatWindow = 5;
    [Tooltip("On curves, bias the pick toward SMALL-footprint buildings (0 = ignore curvature, 1 = strongly " +
             "prefer the smallest). Tight bends → small buildings → tight wall that hugs the arc.")]
    [Range(0f, 1f)] public float curveSmallBias = 0.7f;
    [Tooltip("Extra metres of OVERLAP between buildings on a full curve (closes the outer-corner gap of rigid boxes).")]
    [Range(0f, 6f)] public float curveOverlap = 3f;

    [Header("Size")]
    [Tooltip("Target building height (m) — every prefab is scaled UNIFORMLY to about this (varied). The extracted " +
             "prefabs are tiny at native scale, so this MUST be a real height (≈24-36).")]
    public float targetHeight = 28f;
    [Range(0f, 0.4f)] public float heightJitter = 0.12f;

    [Header("Stream range")]
    [Tooltip("Place buildings out to ~the smog's far distance so new ones fade in inside the smog (no clear-air pop-in).")]
    public float spawnAhead = 250f;
    [Tooltip("Recycle a building once it's this far behind the bus (m).")]
    public float cullBehind = 80f;
    [Tooltip("HARD CAP on live buildings (both sides) — a RUNAWAY backstop only. Set well ABOVE the real maximum " +
             "(a full dense wall both sides is ~60-120) but below a runaway (1000+). If you see a gap at the far " +
             "end, this is too low — raise it. Default 400 leaves huge headroom for dense packing on curves.")]
    public int maxLiveBuildings = 400;

    class Blk { public GameObject go; public int prefabIndex; public Vector3 worldPos; public int side; public float halfWidth; }
    struct Measure { public float height, width, depth, baseToPivot; }

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<Blk> _live = new List<Blk>();
    readonly Dictionary<int, Stack<GameObject>> _pool = new Dictionary<int, Stack<GameObject>>();
    readonly Dictionary<int, Measure> _measure = new Dictionary<int, Measure>();
    readonly Queue<int>[] _recent = { new Queue<int>(), new Queue<int>() };

    // reusable buffers (no per-frame GC): the road centreline + each side's offset polyline (pts/outward/cum).
    readonly List<Vector3> _center = new List<Vector3>();
    readonly List<Vector3>[] _offPts = { new List<Vector3>(), new List<Vector3>() };
    readonly List<Vector3>[] _offOut = { new List<Vector3>(), new List<Vector3>() };
    readonly List<float>[]   _offCum = { new List<float>(),   new List<float>()   };
    bool _ready;

    void Awake() { _road = GetComponent<TiledRoadStreamer>(); _zone = GetComponent<RoadZone>(); }

    void Start()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("CityBlocks"); go.transform.SetParent(transform, false); _parent = go.transform;
        for (int i = 0; i < blockPrefabs.Count; i++) if (blockPrefabs[i] != null) _measure[i] = MeasurePrefab(blockPrefabs[i]);
        _ready = _measure.Count > 0;
        if (!_ready) Debug.LogWarning("[BuildingSpawner] no prefabs assigned — run 'Add or Refresh Buildings On Road'.");
    }

    void Update()
    {
        if (!_ready || _zone == null) return;
        BusController bus = BusController.Instance;
        if (bus == null) return;

        Vector3 busPos = bus.transform.position; busPos.y = 0f;
        Vector3 busFwd = bus.transform.forward; busFwd.y = 0f;
        busFwd = busFwd.sqrMagnitude > 1e-5f ? busFwd.normalized : Vector3.forward;

        // CULL: buildings behind the bus by cullBehind (world distance — no road sampling). Independent of placement.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Vector3 d = _live[i].worldPos - busPos; d.y = 0f;
            if (Vector3.Dot(d, busFwd) < -cullBehind) Recycle(i);
        }

        // ALWAYS run the fill. (A "skip when the frontier is past spawnAhead" gate self-starved: _frontierRel only
        // updates INSIDE FillSide, so once it passed spawnAhead the gate skipped FillSide forever → it never came
        // back down as the bus drove on → empty road. FillSide is cheap and its own arc>=arcLimit is the real,
        // self-correcting stop, so just always call it.)
        _road.GetCenterline(_center);
        if (_center.Count < 2) return;

        for (int sideIdx = 0; sideIdx < 2; sideIdx++)
            FillSide(sideIdx, sideIdx == 0 ? -1 : 1, busPos, busFwd);
    }

    void FillSide(int sideIdx, int s, Vector3 busPos, Vector3 busFwd)
    {
        BuildOffset(sideIdx, s, _zone.RoadHalf + frontGap);
        var cum = _offCum[sideIdx];
        int n = _offPts[sideIdx].Count;
        if (n < 2) return;
        float totalLen = cum[n - 1];

        // The bus's arc on this side's offset spline. The fill window is [busArc - cullBehind, busArc + spawnAhead].
        float busArc = ProjectArc(sideIdx, busPos, 0f, totalLen);
        float arcLimit = Mathf.Min(totalLen, busArc + spawnAhead);

        // FRONTIER = where to place the next building, in arc. Don't track it as stored state (arc origin shifts as
        // tiles recycle → drift). Instead DERIVE it fresh each frame from the actual placed buildings: the frontier
        // is just past the furthest-ahead live building on this side. No stored arc, no drift, no self-starve.
        float arc = FrontierArcFromLiveBuildings(sideIdx, s, busArc, totalLen);
        int guard = 0;
        // guard high enough to fill a whole dense side in one frame (the arc-limit/cap are the real stops); only a
        // backstop against a non-advancing loop. The `arc >= arcLimit` check is what normally ends the fill.
        while (guard++ < 300 && _live.Count < maxLiveBuildings)
        {
            if (arc >= arcLimit) break;                // reached the spawn-ahead arc (or the loaded road end) → wait

            float curv = CurvatureAt(sideIdx, arc, 8f);
            int idx = PickBestFit(sideIdx, curv);
            if (idx < 0) break;

            var m = _measure[idx];
            float scale = (targetHeight > 0.01f && m.height > 0.01f)
                ? (targetHeight * (1f + Random.Range(-heightJitter, heightJitter))) / m.height : 1f;
            float w = m.width * scale, depth = m.depth * scale;

            float midArc = arc + w * 0.5f;
            if (midArc + w * 0.5f > totalLen) break;   // road doesn't extend far enough ahead yet → wait (gapless)

            SampleAt(sideIdx, midArc, out Vector3 frontC, out Vector3 tan, out Vector3 outw);
            float along = Vector3.Dot(frontC - busPos, busFwd);

            if (along >= -cullBehind && along <= spawnAhead)   // only spawn within the live world window
            {
                // SAGITTA push-out: the building front face is a straight chord; on convex curves its ends cut
                // INSIDE the offset arc (toward the road). Push the whole building outward by that deficit so the
                // box never pokes onto the footpath — guaranteed off-road on any curve.
                SampleAt(sideIdx, arc, out Vector3 e0, out _, out _);
                SampleAt(sideIdx, arc + w, out Vector3 e1, out _, out _);
                Vector3 faceE0 = frontC - tan * (w * 0.5f), faceE1 = frontC + tan * (w * 0.5f);
                float push = Mathf.Max(0f, Mathf.Max(Vector3.Dot(e0 - faceE0, outw), Vector3.Dot(e1 - faceE1, outw)));
                Vector3 frontCentre = frontC + outw * push;

                Vector3 centre = frontCentre + outw * (depth * 0.5f);
                Vector3 worldPos = centre + Vector3.up * (-m.baseToPivot * scale - groundSink);
                Quaternion rot = Quaternion.LookRotation(-outw, Vector3.up);   // front faces the road; follows the arc

                GameObject go = Take(idx);
                if (go == null) break;
                go.transform.SetParent(_parent, true);
                go.transform.localScale = Vector3.one * scale;
                go.transform.SetPositionAndRotation(worldPos, rot);
                go.SetActive(true);
                _live.Add(new Blk { go = go, prefabIndex = idx, side = s, worldPos = worldPos, halfWidth = w * 0.5f });
            }

            // advance the cursor (overlap a touch on curves so rigid boxes still touch at their outer corners).
            // CLAMP to ≥1m so the cursor ALWAYS progresses — a tiny/zero advance would loop in place (the runaway).
            float adv = Mathf.Max(1f, w + wallSeam - curv * curveOverlap);
            arc += adv;
        }
    }

    // Where the next building goes (arc), DERIVED FRESH from the live buildings — no stored state to drift. It's
    // the arc just past the furthest-ahead live building on this side (within the live window), or the seed point
    // (cullBehind behind the bus) if this side has none. Robust to arc-origin shifts and can never self-starve.
    float FrontierArcFromLiveBuildings(int sideIdx, int s, float busArc, float totalLen)
    {
        float seed = Mathf.Clamp(busArc - cullBehind, 0f, totalLen);
        float frontier = seed;
        bool any = false;
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i].side != s) continue;
            // project this building's CENTRE onto the offset spline (windowed → no U-turn far-leg), then its FAR
            // edge = centre arc + half-width. The furthest far edge is where the next building's near edge starts.
            float a = ProjectArc(sideIdx, _live[i].worldPos, busArc - cullBehind - 30f, busArc + spawnAhead + 60f)
                    + _live[i].halfWidth;
            if (a > frontier) { frontier = a; any = true; }
        }
        // next building's near edge sits at the furthest far edge, plus the seam (negative = slight overlap).
        return any ? frontier + wallSeam : seed;
    }

    // build side `s`'s offset polyline (world): each centre knot pushed out by its PER-KNOT perpendicular (matches
    // the road mesh cross-section → off the footpath on curves), plus the outward dir + cumulative arc per knot.
    void BuildOffset(int sideIdx, int s, float lateral)
    {
        var pts = _offPts[sideIdx]; var outs = _offOut[sideIdx]; var cum = _offCum[sideIdx];
        pts.Clear(); outs.Clear(); cum.Clear();
        int n = _center.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 tan = (i == 0) ? _center[1] - _center[0]
                        : (i == n - 1) ? _center[n - 1] - _center[n - 2]
                        : _center[i + 1] - _center[i - 1];
            tan.y = 0f; tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector3.forward;
            Vector3 outward = Vector3.Cross(Vector3.up, tan) * s;     // away from the road on side s
            Vector3 off = _center[i] + outward * lateral;
            pts.Add(off); outs.Add(outward);
            cum.Add(i == 0 ? 0f : cum[i - 1] + Vector3.Distance(pts[i - 1], off));
        }
    }

    void SampleAt(int sideIdx, float arc, out Vector3 pos, out Vector3 tan, out Vector3 outward)
    {
        var pts = _offPts[sideIdx]; var outs = _offOut[sideIdx]; var cum = _offCum[sideIdx];
        int n = pts.Count;
        if (n == 0) { pos = default; tan = Vector3.forward; outward = Vector3.right; return; }
        if (n == 1) { pos = pts[0]; tan = Vector3.forward; outward = outs[0]; return; }
        arc = Mathf.Clamp(arc, 0f, cum[n - 1]);
        int i = 0; while (i < n - 2 && cum[i + 1] < arc) i++;
        float segLen = cum[i + 1] - cum[i];
        float t = segLen > 1e-5f ? (arc - cum[i]) / segLen : 0f;
        pos = Vector3.Lerp(pts[i], pts[i + 1], t);
        tan = pts[i + 1] - pts[i]; tan.y = 0f; tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector3.forward;
        outward = Vector3.Lerp(outs[i], outs[i + 1], t); outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : outs[i];
    }

    // nearest arc to world point `w`, considering only segments whose arc falls in [minArc, maxArc] (so a
    // U-turn's far leg — same world spot, very different arc — can't steal the projection).
    float ProjectArc(int sideIdx, Vector3 w, float minArc, float maxArc)
    {
        var pts = _offPts[sideIdx]; var cum = _offCum[sideIdx]; int n = pts.Count;
        if (n < 2) return 0f;
        w.y = 0f;
        float best = float.MaxValue, bestArc = Mathf.Clamp(minArc, 0f, cum[n - 1]);
        for (int i = 0; i < n - 1; i++)
        {
            if (cum[i + 1] < minArc || cum[i] > maxArc) continue;     // outside the window
            Vector3 a = pts[i]; a.y = 0f; Vector3 b = pts[i + 1]; b.y = 0f;
            Vector3 ab = b - a; float ab2 = ab.sqrMagnitude;
            float t = ab2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(w - a, ab) / ab2) : 0f;
            Vector3 proj = a + ab * t; float d = (w - proj).sqrMagnitude;
            if (d < best) { best = d; bestArc = Mathf.Lerp(cum[i], cum[i + 1], t); }
        }
        return bestArc;
    }

    float CurvatureAt(int sideIdx, float arc, float span)
    {
        SampleAt(sideIdx, arc - span, out _, out Vector3 tA, out _);
        SampleAt(sideIdx, arc + span, out _, out Vector3 tB, out _);
        return Mathf.Clamp01(Vector3.Angle(tA, tB) / 18f);
    }

    int PickBestFit(int sideIdx, float curvature)
    {
        var recent = _recent[sideIdx];
        int best = -1; float bestScore = float.NegativeInfinity; int any = -1;
        float idealWidth = targetHeight > 0.01f ? targetHeight * 0.55f : 6f;
        foreach (var kv in _measure)
        {
            int idx = kv.Key; any = idx;
            if (recent.Contains(idx)) continue;
            float width = PrefabWidth(idx), depth = PrefabDepth(idx);
            float score = -Mathf.Abs(width - idealWidth) * 0.4f;
            score -= curvature * curveSmallBias * (width * depth) * 0.05f;
            score += Random.Range(-1.5f, 1.5f);
            if (score > bestScore) { bestScore = score; best = idx; }
        }
        if (best < 0) best = any;
        if (best >= 0) { recent.Enqueue(best); while (recent.Count > Mathf.Max(0, noRepeatWindow)) recent.Dequeue(); }
        return best;
    }

    float PrefabDepth(int idx) { var m = _measure[idx]; float sc = (targetHeight > 0.01f && m.height > 0.01f) ? targetHeight / m.height : 1f; return m.depth * sc; }
    float PrefabWidth(int idx) { var m = _measure[idx]; float sc = (targetHeight > 0.01f && m.height > 0.01f) ? targetHeight / m.height : 1f; return m.width * sc; }

    Measure MeasurePrefab(GameObject prefab)
    {
        var rends = prefab.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Measure { height = 1, width = 1, depth = 1, baseToPivot = 0 };
        Bounds bb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
        float sc = Mathf.Abs(prefab.transform.localScale.y); if (sc < 0.0001f) sc = 1f;
        float h = Mathf.Max(0.01f, bb.size.y / sc);
        float w = Mathf.Max(0.01f, Mathf.Max(bb.size.x, bb.size.z) / sc);
        float d = Mathf.Max(0.01f, Mathf.Min(bb.size.x, bb.size.z) / sc);
        float baseToPivot = (bb.min.y / sc) - (prefab.transform.position.y / sc);
        return new Measure { height = h, width = w, depth = d, baseToPivot = baseToPivot };
    }

    GameObject Take(int idx)
    {
        if (_pool.TryGetValue(idx, out var st) && st.Count > 0) return st.Pop();
        var prefab = blockPrefabs[idx];
        if (prefab == null) return null;
        var go = Instantiate(prefab); go.name = prefab.name; return go;
    }
    void Recycle(int index)
    {
        Blk b = _live[index];
        b.go.SetActive(false);
        b.go.transform.SetParent(_parent, false);
        if (!_pool.TryGetValue(b.prefabIndex, out var st)) { st = new Stack<GameObject>(); _pool[b.prefabIndex] = st; }
        st.Push(b.go);
        _live.RemoveAt(index);
    }

    // floating-origin recenter: building TRANSFORMS move with the road root automatically (CityBlocks is its
    // child, which FloatingOrigin's root-pass shifts). Only the CACHED world data (cull positions + frontiers)
    // must shift so the cull/march stay correct.
    public void OnOriginShifted(Vector3 delta)
    {
        // only the cached cull positions need shifting; the frontier is DERIVED from these each frame, not stored.
        for (int i = 0; i < _live.Count; i++) _live[i].worldPos += delta;
    }
}
