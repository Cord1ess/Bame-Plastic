using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// Streams a cramped city street-wall down both sides of the endless road (so it doesn't look like a floating
/// strip) using the individual Akihabara BUILDING prefabs (Bld_*; from the extractor). Buildings line up ONE
/// AFTER ANOTHER, edge-to-edge, along a straight back-line just past the ground shoulder (footpath → a small
/// dirt gap → buildings), each REORIENTED to face the road, base planted on the ground. Placed at road-relative
/// positions (TiledRoadStreamer.SampleRoad) so they ride the floating origin + curves; pooled; recycled behind
/// the bus. The smog hides the spawn edge.
///
/// Each building prefab is MEASURED once (bounds) to: scale it to a target height, plant its base, and advance
/// the cursor by its real WIDTH (edge-to-edge serial wall). PLAY-ONLY (no [ExecuteAlways]).
[RequireComponent(typeof(TiledRoadStreamer))]
public class BuildingSpawner : MonoBehaviour
{
    [Header("Building prefabs (Bld_*; from the extractor)")]
    public List<GameObject> blockPrefabs = new List<GameObject>();

    [Header("Placement")]
    [Tooltip("Gap (m) from the FOOTPATH edge (RoadZone.RoadHalf) to the building fronts — buildings sit ON the " +
             "ground band of the road, a little distance off the footpath. Keep < groundWidth (~22m). Should be " +
             "> roadClearance so gentle curves don't hide buildings.")]
    public float dirtGap = 5f;
    [Tooltip("How much each building OVERLAPS the previous one, as a fraction of its width. 0 = edge-to-edge, " +
             "0.35 = each sits 35% into the last (dense, zero-gap, Akihabara-packed). 0.5 = very heavy overlap.")]
    [Range(0f, 0.6f)] public float overlapFrac = 0.35f;
    [Tooltip("Sink buildings this many m INTO the ground so they sit planted, not floating on the surface.")]
    public float groundSink = 0.4f;
    [Tooltip("HARD RULE: a building is HIDDEN if its footprint comes within this many m of the footpath edge " +
             "(RoadHalf) anywhere — so buildings never sit on the road/footpath, even on sharp turns/U-turns. " +
             "Bigger = safer (more gets hidden on turns). Prefer a gap to an overlap.")]
    public float roadClearance = 2.5f;
    [Tooltip("Don't reuse the same building prefab within this many placements on a side (variety).")]
    public int noRepeatWindow = 4;
    [Tooltip("Random extra setback (m) per building, so overlapping neighbours sit at slightly different depths " +
             "— breaks the Z-FIGHTING flicker where crammed walls would be coplanar. Keep small (1-3m).")]
    public float depthJitter = 2f;

    [Header("Size")]
    [Tooltip("Target building height (m) — scaled uniformly to about this, varied. 0 = keep native scale (real district heights).")]
    public float targetHeight = 0f;
    [Range(0f, 0.4f)] public float heightJitter = 0.12f;

    [Header("Stream range")]
    [Tooltip("Place buildings out to ~the smog's far distance (smogEnd) so new ones FADE IN inside the smog " +
             "instead of popping in clear air. Keep ≈ DayNightController.smogEnd (currently 240). Capped by the " +
             "road's loaded distance (~360m).")]
    public float spawnAhead = 250f;
    [Tooltip("Recycle a building once it's this far behind the bus (m).")]
    public float cullBehind = 80f;

    class Blk
    {
        public GameObject go;
        public int prefabIndex;
        public Vector3 worldPos;  // FIXED world position, set ONCE at spawn — buildings are STATIC (no per-frame
                                  // re-placement → no jitter/ghosting). Only shifted on a floating-origin recenter.
        public int side;
    }

    struct Measure { public float height, width, depth, baseToPivot; }

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<Blk> _live = new List<Blk>();
    readonly Dictionary<int, Stack<GameObject>> _pool = new Dictionary<int, Stack<GameObject>>();
    readonly Dictionary<int, Measure> _measure = new Dictionary<int, Measure>();
    readonly Queue<int>[] _recent = { new Queue<int>(), new Queue<int>() };   // [sideIdx] last-used prefab indices
    readonly float[] _nextMetres = { -9999f, -9999f };   // PER-SIDE cursor (front edge of next building) [sideIdx]
    bool _ready;

    void Awake() { _road = GetComponent<TiledRoadStreamer>(); _zone = GetComponent<RoadZone>(); }

    void Start()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("CityBlocks"); go.transform.SetParent(transform, false); _parent = go.transform;
        for (int i = 0; i < blockPrefabs.Count; i++) if (blockPrefabs[i] != null) _measure[i] = MeasurePrefab(blockPrefabs[i]);
        _ready = _measure.Count > 0;
        if (!_ready) Debug.LogWarning("[BuildingSpawner] no block prefabs assigned — run 'Extract Akihabara Blocks' + 'Add or Refresh Buildings On Road'.");
    }

    void Update()
    {
        if (!_ready || _zone == null) return;
        BusController bus = BusController.Instance;
        if (bus == null) return;
        float busSpeed = bus.SpeedMps;
        float dt = Time.deltaTime;
        float ahead = Mathf.Min(spawnAhead, _road.MetresAhead - 5f);

        // Buildings are STATIC in the world — placed once, never moved per frame (that constant re-placement was
        // the jitter/ghosting). The BUS drives past them. Cull by real world distance behind the bus.
        Vector3 busPos = bus.transform.position;
        Vector3 busFwd = bus.transform.forward; busFwd.y = 0f;
        if (busFwd.sqrMagnitude > 1e-5f) busFwd.Normalize(); else busFwd = Vector3.forward;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Blk b = _live[i];
            Vector3 d = b.worldPos - busPos; d.y = 0f;
            float along = Vector3.Dot(d, busFwd);          // + ahead of bus, - behind
            if (along < -cullBehind) { Recycle(i); continue; }
        }

        // SERIAL fill — each side has its own cursor (bus-relative metres), placing the next building ahead.
        // The cursor decays as the bus drives; placement samples the road ONCE to fix the building's world pos.
        for (int sideIdx = 0; sideIdx < 2; sideIdx++)
        {
            int s = sideIdx == 0 ? -1 : 1;
            _nextMetres[sideIdx] -= busSpeed * dt;
            // FIRST RUN: start BEHIND the bus so the area beside + behind it is filled at spawn (not just ahead).
            if (_nextMetres[sideIdx] < -9000f) _nextMetres[sideIdx] = -cullBehind + 2f;
            int guard = 0;
            while (_nextMetres[sideIdx] < ahead && guard++ < 80) if (!PlaceOne(s, sideIdx)) break;
        }
    }

    // place one building on side `s`, advancing that side's cursor. Returns false if it couldn't (stalls the loop).
    bool PlaceOne(int s, int sideIdx)
    {
        // how much depth does this spot allow before a building would hit the road? (small on sharp turns) →
        // pick a building that FITS, so we place a small one instead of hiding a big one (fewer gaps on turns).
        float maxDepth = SafeDepthAt(_nextMetres[sideIdx] + 2f, s);
        int idx = PickPrefab(sideIdx, maxDepth);
        if (idx < 0) return false;
        var m = _measure[idx];
        float scale = (targetHeight > 0.01f && m.height > 0.01f)
            ? (targetHeight * (1f + Random.Range(-heightJitter, heightJitter))) / m.height
            : 1f;
        float worldWidth = m.width * scale;
        float worldDepth = m.depth * scale;

        // figure out WHERE this building goes (sample the road ONCE — buildings are static afterwards).
        // front a little past the footpath (RoadHalf + dirtGap), extending out; small setback jitter
        // de-coplanarises overlapping neighbours (kills Z-fighting flicker).
        float metres = _nextMetres[sideIdx] + worldWidth * 0.5f;
        float frontLateral = _zone.RoadHalf + dirtGap + Random.Range(0f, depthJitter);
        float centreLateral = frontLateral + worldDepth * 0.5f;
        float advance = worldWidth * Mathf.Max(0.15f, 1f - overlapFrac);

        // When a spot can't take THIS building, nudge the cursor only a SMALL step (not a full building width) and
        // retry — otherwise every skip leaves a building-sized hole. This closes the gaps.
        float skipStep = 3f;

        if (!_road.SampleRoad(metres, s * centreLateral, out Vector3 pos, out Vector3 fwd, out Vector3 right))
        { _nextMetres[sideIdx] += skipStep; return true; }   // off the live road here — step a little, keep walking

        // HARD RULE — never on the road. If the footprint would touch the road corridor (e.g. inside a sharp
        // turn), DON'T place it (prefer a gap). Checked ONCE here. Step a little and retry (maybe a smaller
        // building fits a hair further along) instead of skipping a whole building's width.
        if (FootprintHitsRoad(metres, pos, fwd, right, s, worldWidth * 0.5f, worldDepth * 0.5f))
        { _nextMetres[sideIdx] += skipStep; return true; }

        GameObject go = Take(idx);
        if (go == null) return false;
        Vector3 worldPos = pos + Vector3.up * (-m.baseToPivot * scale - groundSink);   // plant base, sink a bit
        Vector3 inward = -(right * s); inward.y = 0f;                                   // toward the road = the front
        Quaternion rot = inward.sqrMagnitude > 1e-5f ? Quaternion.LookRotation(inward.normalized, Vector3.up) : Quaternion.identity;

        go.transform.SetParent(_parent, true);
        go.transform.localScale = Vector3.one * scale;
        go.transform.SetPositionAndRotation(worldPos, rot);     // set ONCE — never touched again until recycled
        go.SetActive(true);
        _live.Add(new Blk { go = go, prefabIndex = idx, side = s, worldPos = worldPos });

        _nextMetres[sideIdx] += advance;
        return true;
    }

    // does the building's footprint (centred at world `pos`, at road-distance `metres`, depth half-D toward the
    // road) come within roadClearance of the road corridor anywhere along its length? Scans real centreline
    // points using the EXACT placement metres (not a fragile world projection). One-shot.
    bool FootprintHitsRoad(float metres, Vector3 pos, Vector3 fwd, Vector3 right, int side, float halfW, float halfD)
    {
        float minClear = _zone.RoadHalf + roadClearance;
        Vector3 along = fwd; Vector3 outAxis = right * side;
        float scan = halfW + 6f;
        int steps = Mathf.Clamp(Mathf.CeilToInt(scan / 3f), 2, 8);
        for (int k = -steps; k <= steps; k++)
        {
            float dm = (scan * k) / steps;
            if (!_road.SampleRoad(metres + dm, 0f, out Vector3 c, out _, out _)) continue;
            Vector3 toC = c - pos; toC.y = 0f;
            float wOff = Mathf.Clamp(Vector3.Dot(toC, along), -halfW, halfW);
            Vector3 innerEdge = pos + along * wOff + outAxis * (-halfD);
            Vector3 dd = innerEdge - c; dd.y = 0f;
            if (dd.magnitude < minClear) return true;
        }
        return false;
    }

    int PickPrefab(int sideIdx, float maxDepth)
    {
        // candidates = not recently used AND whose depth (×min scale) fits the available space at this spot.
        var recent = _recent[sideIdx];
        _tmp.Clear();
        foreach (var kv in _measure)
        {
            if (recent.Contains(kv.Key)) continue;
            if (PrefabDepth(kv.Key) > maxDepth) continue;     // too deep for this (possibly curved) spot
            _tmp.Add(kv.Key);
        }
        // relax progressively if nothing fits: ignore the recent window, then ignore the depth limit (so we
        // always place SOMETHING — the road guard is the final safety net).
        if (_tmp.Count == 0) foreach (var kv in _measure) if (PrefabDepth(kv.Key) <= maxDepth) _tmp.Add(kv.Key);
        if (_tmp.Count == 0)
        {
            // nothing fits the space → take the SHALLOWEST building (best chance of fitting a tight turn)
            int best = -1; float bestD = float.MaxValue;
            foreach (var kv in _measure) { float d = PrefabDepth(kv.Key); if (d < bestD) { bestD = d; best = kv.Key; } }
            return best;
        }
        int idx = _tmp[Random.Range(0, _tmp.Count)];
        recent.Enqueue(idx);
        while (recent.Count > Mathf.Max(0, noRepeatWindow)) recent.Dequeue();
        return idx;
    }
    readonly List<int> _tmp = new List<int>();

    // a prefab's depth at the smallest scale it'd be placed at (so the fit test is conservative)
    float PrefabDepth(int idx)
    {
        var m = _measure[idx];
        float minScale = (targetHeight > 0.01f && m.height > 0.01f) ? (targetHeight * (1f - heightJitter)) / m.height : 1f;
        return m.depth * minScale;
    }

    // estimate how deep (lateral) a building can extend here before it would hit the road. On a straight road
    // this is the whole ground band; on a curve the road bulges in and shrinks it. We scan centreline points
    // around `metres` and find the closest approach of the road to this side's back-line direction.
    float SafeDepthAt(float metres, int side)
    {
        if (!_road.SampleRoad(metres, side * (_zone.RoadHalf + dirtGap), out Vector3 front, out _, out Vector3 right))
            return 0f;
        Vector3 outAxis = right * side;                       // from road outward
        float budget = _zone.groundWidth;                    // best case: the whole ground band
        for (int k = -4; k <= 4; k++)
        {
            float dm = k * 4f;
            if (!_road.SampleRoad(metres + dm, 0f, out Vector3 c, out _, out _)) continue;
            // how far along the OUTWARD axis is the front from the centreline at this sample? the building can
            // extend until its back would be (RoadHalf+clearance) from the NEAREST road point. Approximate by
            // the outward distance from this centreline point to the front, minus the required clearance.
            Vector3 d = front - c; d.y = 0f;
            float outward = Vector3.Dot(d, outAxis);          // ~ dirtGap + RoadHalf on straight; less if road bulges in
            float allow = outward - roadClearance;            // depth the building may use from its front outward
            budget = Mathf.Min(budget, allow);
        }
        return Mathf.Clamp(budget, 0f, _zone.groundWidth);
    }


    Measure MeasurePrefab(GameObject prefab)
    {
        var rends = prefab.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Measure { height = 1, width = 1, depth = 1, baseToPivot = 0 };
        Bounds bb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
        float sc = Mathf.Abs(prefab.transform.localScale.y); if (sc < 0.0001f) sc = 1f;
        float h = Mathf.Max(0.01f, bb.size.y / sc);
        // the extractor baked long side → X, depth → Z. So width(along road) = X, depth(across) = Z. Use
        // max/min as a safety net in case a prefab wasn't re-baked.
        float w = Mathf.Max(0.01f, Mathf.Max(bb.size.x, bb.size.z) / sc);   // along-road frontage (long)
        float d = Mathf.Max(0.01f, Mathf.Min(bb.size.x, bb.size.z) / sc);   // across-road depth (short)
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

    // floating-origin recenter: the building TRANSFORMS already move with the road (their parent "CityBlocks" is
    // a child of the road root, which FloatingOrigin shifts). We must NOT shift the transform again (that would
    // double-shift) — only update the CACHED world position so the cull-distance check stays correct.
    public void OnOriginShifted(Vector3 delta)
    {
        for (int i = 0; i < _live.Count; i++) _live[i].worldPos += delta;
    }
}
