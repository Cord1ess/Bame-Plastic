using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Random = UnityEngine.Random;

/// The bulletproof endless road: a pool of fixed-length RoadTiles streamed around the bus. Each tile is
/// lofted ONCE (Burst job, zero-GC, off-thread collider bake) from a stable run of centreline samples and
/// never rebuilt while live — so steady-state streaming costs at most ONE tile's work per recycle, not a
/// whole-road rebuild. Adjacent tiles share their boundary samples exactly, so joins are watertight.
///
/// Replaces EndlessRoadGenerator + SplineRoadLoft for runtime. Keeps the same procedural walk (variety),
/// left-lane spawn, OnForwardAdvanced / OnOriginShifted hooks, and uses RoadZone for the cross-section.
[ExecuteAlways]
[RequireComponent(typeof(RoadZone))]
public class TiledRoadStreamer : MonoBehaviour
{
    [Header("Who to follow")]
    public Transform target;

    [Header("Editor preview")]
    [Tooltip("Build the first tiles in the Scene view before Play, so the road isn't empty in the editor.")]
    public bool editorPreview = true;

    [Header("Tiles")]
    [Tooltip("Length of one tile (metres). Bigger = fewer joins, smoother long curves.")]
    public float tileLength = 60f;
    [Tooltip("Cross-section rings per tile — mesh density along the road. More = smoother curves within a tile.")]
    [Min(2)] public int ringsPerTile = 12;
    [Tooltip("Tiles kept AHEAD of the bus.")]
    [Min(2)] public int tilesAhead = 6;
    [Tooltip("Tiles kept BEHIND the bus before recycling.")]
    [Min(1)] public int tilesBehind = 3;

    [Header("Variety (random each run)")]
    [Tooltip("Max GENTLE turn in degrees per 30m of road. Higher = curvier baseline.")]
    public float maxTurnRate = 14f;
    [Range(0f, 1f)] public float behaviourChangeChance = 0.4f;
    [Range(0f, 1f)] public float straightChance = 0.45f;
    public float wobble = 3f;
    [Tooltip("0 = a stable auto-seed is generated & saved on this object (random-looking but identical in " +
             "editor preview and Play, so the pre-placed bus matches the runtime road). Set a value to pin it.")]
    public int seed = 0;
    [SerializeField, HideInInspector] int _autoSeed;   // persisted so editor preview == runtime build

    [Header("Major turns / U-turns")]
    [Tooltip("Chance (per tile) to START a hard sustained turn — a real corner or U-turn, not a gentle curve.")]
    [Range(0f, 1f)] public float sharpTurnChance = 0.12f;
    [Tooltip("Hard-turn rate in degrees per 30m while a sharp turn is active. Higher = tighter radius — but " +
             "too tight and a U-turn overlaps itself (radius must exceed the road half-width). 35 is a safe corner.")]
    public float sharpTurnRate = 35f;
    [Tooltip("Total degrees a sharp turn sweeps before ending. 90 = corner, 180 = U-turn (randomised up to this).")]
    public float sharpTurnMaxSweep = 170f;
    [Tooltip("Don't start another sharp turn until at least this much road (m) has passed since the last.")]
    public float sharpTurnCooldown = 220f;

    [Header("Materials (auto double-sided URP if empty)")]
    public Material roadMaterial, footpathMaterial, groundMaterial, markingMaterial;
    public float curbHeight = 0.15f;
    [Tooltip("Height of the central median above the road. 0 = flush with the road (just a painted divider).")]
    public float medianHeight = 0f;
    public float roadThickness = 0.5f;
    public float markingWidth = 0.15f, dashLength = 3f, gapLength = 4f;

    [Header("Build budget")]
    [Tooltip("Max tiles to build per frame. 1 is smoothest; a couple lets it catch up after a hitch or at " +
             "high speed without leaving a gap. Tiles are cheap (collider bakes off-thread), so 2-3 is fine.")]
    [Min(1)] public int maxTilesPerFrame = 3;
    [Tooltip("Use the Burst loft job (zero-GC, fastest). Off = proven managed loft. Try managed first; " +
             "flip on once you've confirmed the road looks right, for max headroom.")]
    public bool useBurst = false;

    public System.Action<Vector3> OnForwardAdvanced;
    public System.Action<Vector3> OnOriginShiftedEvent;
    public RoadZone Zone => _zone;

    RoadZone _zone;
    readonly List<TileSpan> _live = new List<TileSpan>();     // ordered front-to-back along the road
    readonly Stack<RoadTile> _pool = new Stack<RoadTile>();
    Transform _tilesParent;

    // procedural walk for the LEADING centreline, advanced one tile at a time
    struct Walk { public Vector3 head; public float yaw, turnRate; }
    Walk _lead;
    Random.State _rng;
    bool _busPlaced;

    // The bus's CHAIN position, tracked incrementally. We never re-derive it by global nearest-distance —
    // on a U-turn the road doubles back near itself, and a global search would snap onto the parallel old
    // leg (the "parallel road far away" bug). Instead we nudge this index one step at a time toward
    // whichever ADJACENT tile is closer, and shift it when tiles are added at the front.
    int _busTileIndex;
    bool _busIndexValid;

    // sharp-turn state: when active, the walk holds a hard turn rate until it has swept `_sharpTarget`
    // degrees (a corner / U-turn), then returns to gentle curving. `_sinceSharp` enforces the cooldown.
    bool _sharpActive;
    int _sharpSign;
    float _sharpSwept, _sharpTarget;
    float _sinceSharp = 9999f;

    // continuity carry-over: the previous tile's LAST sample (world) + its forward, so the next tile's
    // first ring is identical to the previous tile's last ring → frames match → the seam is invisible.
    Vector3 _carryPt;
    Vector3 _carryFwd = Vector3.forward;
    bool _haveCarry;

    // Shared cross-section (constant), kept as MANAGED arrays — NOT persistent NativeArrays. Persistent
    // native allocations leak across editor domain reloads (the leak detector flags them every recompile);
    // managed arrays are GC-owned and can't leak. We wrap them in Allocator.Temp NativeArrays per tile
    // build (auto-freed each frame). Rebuilt only when RoadZone widths change.
    float2[] _profile;
    int[] _seg;
    float[] _cum;
    RoadProfileMeta _meta;
    bool _profileReady;
    float _profGroundHalf;

    class TileSpan
    {
        public RoadTile tile;
        public Vector3 startPt, endPt;   // world centreline endpoints (for recycle distance + joins)
        public Vector3[] pts;            // world CENTRELINE ring points (the ACTUAL curved path the mesh traces).
                                         // CenterlineAt interpolates along THESE — endpoint-only lerp chorded
                                         // across curves (the "centreline goes straight on a corner" bug).
    }

    void Awake()
    {
        _zone = GetComponent<RoadZone>();
        if (target == null && BusController.Instance != null) target = BusController.Instance.transform;
    }

    void Start()
    {
        if (!Application.isPlaying) return;

        // Runtime-created meshes don't survive the edit→play domain reload, so we DO rebuild the road at
        // runtime — but deterministically (fixed seed + fixed origin), so it reproduces the EXACT road the
        // editor preview showed. The bus, already seated on that road in edit mode, therefore lands on the
        // identical geometry. We still re-assert the spawn pose once (cheap, no async dependency) to be
        // pixel-perfect, which is reliable here because the road is already built this frame.
        BuildInitial();
        TryPlaceBus();
    }

    void OnEnable()
    {
        if (!Application.isPlaying && editorPreview) RebuildEditorPreview();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && !Application.isPlaying && editorPreview) RebuildEditorPreview();
        };
    }
#endif

    // One-shot preview build for the Scene view (not playing). Lays the initial tiles so you can see the
    // road before pressing Play, AND drops the bus onto the road so there's nothing to place at runtime.
    [ContextMenu("Rebuild Preview + Seat Bus")]
    public void RebuildEditorPreview()
    {
        if (this == null) return;
        BuildInitial();
        PlaceBusInEditor();
    }

    // EDIT MODE: snap the BusPlayer onto the left lane at the middle of the road, so before Play the bus is
    // already correctly seated — no runtime teleport, no async-bake / init-order race (the source of the
    // flaky "spawn in the sky"). At runtime we keep this exact road (no rebuild), so the position holds.
    public void PlaceBusInEditor()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        ResolveTarget();
        if (target == null) return;
        if (!TryGetSpawnPose(out Vector3 pos, out Quaternion rot)) return;

        // The bus root pivot is at ground contact, so just sit the root on the lane surface.
        target.SetPositionAndRotation(pos, rot);
        UnityEditor.EditorUtility.SetDirty(target);
#endif
    }

    void Update()
    {
        if (!Application.isPlaying) return;        // no streaming/spawn in edit mode
        Stream();
    }

    void ResolveTarget()
    {
        if (target != null) return;
        var bc = BusController.Instance;
        if (bc == null) bc = FindAnyObjectByType<BusController>();
        if (bc != null) target = bc.transform;
    }

    void EnsureParent()
    {
        if (_tilesParent != null) return;
        // Reuse an existing "Tiles" container if one survived a recompile / edit-mode rebuild, and reclaim
        // its tiles into the pool so previews don't spawn duplicates.
        Transform existing = transform.Find("Tiles");
        if (existing != null)
        {
            _tilesParent = existing;
            foreach (var rt in existing.GetComponentsInChildren<RoadTile>(true))
            {
                rt.Release();
                if (!_pool.Contains(rt)) _pool.Push(rt);
            }
            return;
        }
        var go = new GameObject("Tiles");
        go.transform.SetParent(transform, false);
        // never serialize generated tiles into the scene (edit-mode preview was baking 150+ RoadTiles into
        // the .unity file, bloating it). They're regenerated every load.
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        _tilesParent = go.transform;
    }

    void EnsureMaterials()
    {
        if (roadMaterial == null) roadMaterial = MakeUrp("RoadAsphalt", new Color(0.28f, 0.28f, 0.3f));
        if (footpathMaterial == null) footpathMaterial = MakeUrp("Footpath", new Color(0.62f, 0.6f, 0.56f));
        if (groundMaterial == null) groundMaterial = MakeUrp("Ground", new Color(0.42f, 0.4f, 0.34f));
        if (markingMaterial == null) markingMaterial = MakeUrp("LaneMarking", new Color(0.92f, 0.92f, 0.85f));
    }

    void BuildProfile()
    {
        float mh = _zone.MedianHalf, dh = _zone.DriveHalf, rh = _zone.RoadHalf, gh = _zone.GroundHalf, h = curbHeight;
        float mhgt = medianHeight;   // median top height (0 = flush with the road)
        var prof = new float2[]
        {
            new float2(-gh, h), new float2(-rh, h), new float2(-dh, h), new float2(-dh, 0f),
            new float2(-mh, 0f), new float2(-mh, mhgt), new float2(mh, mhgt), new float2(mh, 0f),
            new float2(dh, 0f), new float2(dh, h), new float2(rh, h), new float2(gh, h),
        };
        int[] seg = { 2, 1, 1, 0, 1, 1, 1, 0, 1, 1, 2 };
        int P = prof.Length;
        float[] cum = new float[P];
        for (int j = 1; j < P; j++) cum[j] = cum[j - 1] + math.distance(prof[j], prof[j - 1]);

        _profile = prof;          // managed arrays — no native allocation to leak
        _seg = seg;
        _cum = cum;
        _profGroundHalf = gh;
        _meta = new RoadProfileMeta
        {
            profileCount = P, leftEdgeIndex = 0, rightEdgeIndex = P - 1,
            laneHalf = dh, medianHalf = mh, laneWidth = _zone.laneWidth, lanesPerDir = _zone.lanesPerDirection,
            markWidth = markingWidth, markLift = 0.02f, dashLength = dashLength, gapLength = gapLength,
        };
        _profileReady = true;
    }

    // A stable non-zero seed, generated once and persisted in _autoSeed. Uses the global RNG (varies per
    // session when first created in the editor), then stays fixed so preview == runtime.
    int MakeAutoSeed()
    {
        int s = Random.Range(1, int.MaxValue);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);     // persist the chosen seed with the scene/prefab
#endif
        return s == 0 ? 1 : s;
    }

    public void BuildInitial()
    {
        _zone = GetComponent<RoadZone>();
        EnsureParent(); EnsureMaterials(); BuildProfile();

        // clear any existing
        for (int i = _live.Count - 1; i >= 0; i--) ReleaseSpan(i);

        // Deterministic seed so the editor preview and the runtime build produce the IDENTICAL road (the
        // pre-placed bus must match what gets built at runtime). seed>0 pins it; seed==0 uses a stable
        // auto-seed saved on this object (generated once, random-looking but persistent).
        int effective = seed != 0 ? seed : (_autoSeed != 0 ? _autoSeed : (_autoSeed = MakeAutoSeed()));
        // In PLAY mode, the session (menu/lobby) seed wins so all clients build the SAME world (the MP
        // promise) and solo uses its fresh seed. Edit-mode preview keeps the persisted seed so the
        // pre-placed bus still matches.
        if (Application.isPlaying && SessionContext.Instance != null && SessionContext.Instance.Seed != 0)
            effective = SessionContext.Instance.Seed;
        Random.InitState(effective);
        _rng = Random.state;
        _haveCarry = false;                       // fresh build: no boundary carry-over yet
        _sharpActive = false; _sharpSwept = 0f; _sinceSharp = 9999f;
        _busIndexValid = false; _busTileIndex = 0; _busTileF = 0f; _busTileFInit = false; _pendingIndexShift = 0;   // re-seed the bus chain tracker on a fresh build

        // Build the centreline at WORLD Y=0 (ground plane), independent of this object's transform Y, so the
        // road never sits high (and the bus never spawns in the sky because the road itself was elevated).
        int behind = (tilesAhead + tilesBehind) / 2;
        Vector3 origin = transform.position; origin.y = 0f;
        _lead = new Walk { head = origin - Vector3.forward * tileLength * behind, yaw = 0f, turnRate = 0f };

        int n = tilesAhead + tilesBehind;
        for (int i = 0; i < n; i++) AppendTileAhead();
    }

    void Stream()
    {
        if (target == null) { ResolveTarget(); if (target == null) return; }
        if (!_busPlaced) TryPlaceBus();
        if (!_profileReady || _live.Count == 0) return;

        TrackBusIndex();                                  // incremental — immune to U-turn parallel legs

        // EXTEND: keep `tilesAhead` tiles in front of the bus (lower indices). Appending at index 0 pushes
        // the bus index up by 1, so we track that as we go.
        int built = 0;
        int budget = Mathf.Max(1, maxTilesPerFrame);
        while (_busTileIndex < tilesAhead && built < budget)
        {
            AppendTileAhead();                           // inserts at index 0
            _busTileIndex++;                             // everything shifted up one
            _pendingIndexShift++;                        // so _busTileF compensates next TrackBusIndex
            built++;
            OnForwardAdvanced?.Invoke(_live[0].endPt);
        }

        // RECYCLE the back of the chain — but ONLY tiles that are BOTH far in chain-order AND physically
        // far from the bus. The physical guard is the safety net: even if the tracked index briefly drifts,
        // we can never drop a tile near the bus and strand it (that was the chain-split / parallel-road bug).
        float keepRadius = tileLength * (tilesBehind + 1f);
        while (_live.Count - 1 - _busTileIndex > tilesBehind)
        {
            int backIdx = _live.Count - 1;
            Vector3 mid = (_live[backIdx].startPt + _live[backIdx].endPt) * 0.5f;
            if ((mid - target.position).sqrMagnitude < keepRadius * keepRadius) break;   // too close — keep it
            ReleaseSpan(backIdx);
        }
    }

    // Track the bus's chain index by a BOUNDED-WINDOW nearest search around the last index. A single-step
    // hill-climb stalls in local minima on curves (a tile 2 ahead can be closer than the adjacent one →
    // the index freezes → recycle drops the wrong tiles → the chain splits into a parallel fragment).
    // A window of ±searchRadius can't get stuck, yet (being bounded) can't jump to a U-turn's far leg.
    [Tooltip("How many tiles either side of the last bus position to search. Must exceed the most tiles the " +
             "bus can cross in one frame; large enough to ride curves, small enough to ignore U-turn legs.")]
    public int trackSearchRadius = 4;

    void TrackBusIndex()
    {
        if (_live.Count == 0) { _busTileIndex = 0; return; }
        if (!_busIndexValid) { _busTileIndex = GlobalNearestIndex(); _busIndexValid = true; }
        _busTileIndex = Mathf.Clamp(_busTileIndex, 0, _live.Count - 1);

        Vector3 b = target.position;
        int lo = Mathf.Max(0, _busTileIndex - trackSearchRadius);
        int hi = Mathf.Min(_live.Count - 1, _busTileIndex + trackSearchRadius);
        float best = float.MaxValue; int bestIdx = _busTileIndex;
        for (int i = lo; i <= hi; i++)
        {
            float d = SqrToTile(i, b);
            if (d < best) { best = d; bestIdx = i; }
        }
        _busTileIndex = bestIdx;

        // CONTINUOUS bus position along the chain (_busTileF). Projecting the bus onto its nearest span
        // raw each frame is NOISY near tile boundaries (the nearest-span pick flickers → traffic ghosts and
        // snaps). So we SMOOTH it: compute the raw projection, then ease _busTileF toward it. The bus's true
        // road progress is continuous (physics-driven), so the filter tracks it tightly with no lag while
        // rejecting the per-frame measurement jitter.
        TileSpan s = _live[_busTileIndex];
        Vector3 a = s.endPt, c = s.startPt;           // a=front edge (t=0), c=rear edge (t=1)
        Vector3 seg = c - a;
        float segLen2 = seg.sqrMagnitude;
        float t = segLen2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(b - a, seg) / segLen2) : 0f;
        float raw = _busTileIndex + t;

        float prevTileF = _busTileF;
        if (!_busTileFInit) { _busTileF = raw; _busTileFInit = true; _busArcAdvance = 0f; }
        else
        {
            // append-compensation: when front tiles were added this frame, _busTileIndex (and raw) shifted
            // up by _pendingIndexShift; apply that to _busTileF first so it stays on the same world spot,
            // then smooth only the genuine motion.
            _busTileF += _pendingIndexShift;
            _busTileF = Mathf.Lerp(_busTileF, raw, 1f - Mathf.Exp(-12f * Time.deltaTime));  // framerate-independent ease
            // Signed change in the bus's continuous tileF this frame, MINUS the append shift (not real motion),
            // expressed in metres. tileF DECREASES as the bus moves forward (front = lower index), so this is
            // NEGATIVE when moving forward. Road-relative content adds THIS to its "metres ahead of bus" cursor
            // (cursor += delta → decreases) instead of using busSpeed*dt — on curves world-speed ≠ centreline arc,
            // which slid the cursor out of sync with placed buildings → the curve gaps.
            _busArcAdvance = ((_busTileF - prevTileF) - _pendingIndexShift) * tileLength;
            _pendingIndexShift = 0;
        }

        // Bus lateral offset on the road (metres right of centre) — so traffic can treat the bus as an
        // obstacle in the correct lane. Project the bus onto the road frame at its position.
        if (SampleRoad(0f, 0f, out Vector3 centre, out Vector3 _, out Vector3 r))
            _busLateral = Vector3.Dot(b - centre, r);
    }
    float _busTileF;
    bool _busTileFInit;
    int _pendingIndexShift;
    float _busLateral;
    float _busArcAdvance;
    /// Signed CENTRELINE arc (metres) the bus moved this frame (negative = forward, since front = lower tileF).
    /// Road-relative streamed content should decay its bus-relative "metres ahead" cursor by ADDING this, not by
    /// busSpeed*dt — on curves the bus's world speed ≠ its centreline-arc rate, and the mismatch slides the cursor.
    public float BusArcAdvance => _busArcAdvance;
    /// The bus's lateral offset on the road (m right of centre). Traffic senses the bus as an obstacle here.
    public float BusLateral => _busLateral;

    /// The drivable lateral band (m, signed offsets from centre) for the forward (bus) or oncoming side.
    /// forward+LHT → the -X lanes. Used by DriverGuide to keep the guide line on our drivable lanes.
    public void SampleBand(bool forward, out float min, out float max)
    {
        float mh = _zone != null ? _zone.MedianHalf : 0.75f;
        float dh = _zone != null ? _zone.DriveHalf : 7.25f;
        bool lht = _zone == null || _zone.leftHandTraffic;
        float sideSign = (forward == lht) ? -1f : 1f;     // forward+LHT → -X side
        float a = sideSign * mh, b = sideSign * dh;
        min = Mathf.Min(a, b); max = Mathf.Max(a, b);
    }

    float SqrToTile(int i, Vector3 b)
    {
        Vector3 mid = (_live[i].startPt + _live[i].endPt) * 0.5f;
        return (mid - b).sqrMagnitude;
    }

    // Only used to (re)seed the tracker — e.g. right after spawn, before any U-turn exists.
    int GlobalNearestIndex()
    {
        Vector3 b = target.position;
        float best = float.MaxValue; int bestIdx = 0;
        for (int i = 0; i < _live.Count; i++)
        {
            float d = SqrToTile(i, b);
            if (d < best) { best = d; bestIdx = i; }
        }
        return bestIdx;
    }

    // Lay down one tile ahead, sampling the continuous walk. The tile's FIRST ring reuses the previous
    // tile's LAST sample + forward (carry-over), so adjacent tiles share an identical boundary ring —
    // same position AND same frame → the seam is invisible (no bump, no normal crease).
    void AppendTileAhead()
    {
        int R = Mathf.Max(2, ringsPerTile);
        var cen = new NativeArray<float3>(R, Allocator.TempJob);
        var rt  = new NativeArray<float3>(R, Allocator.TempJob);
        var up  = new NativeArray<float3>(R, Allocator.TempJob);

        float step = tileLength / (R - 1);

        // first ring = the carried boundary from the previous tile (or the current head on the very first)
        Vector3 startWorld = _haveCarry ? _carryPt : _lead.head;
        Vector3 prevPt = startWorld;
        Vector3 prevFwd = _haveCarry ? _carryFwd : (Quaternion.Euler(0, _lead.yaw, 0) * Vector3.forward);

        // make sure the walk head IS the start point so the next samples continue from here
        _lead.head = startWorld;

        // Decide (once per tile) whether to START a sharp sustained turn — a real corner / U-turn.
        MaybeStartSharpTurn();

        for (int i = 0; i < R; i++)
        {
            Vector3 cur; Vector3 fwd;
            if (i == 0) { cur = startWorld; fwd = prevFwd; }
            else
            {
                cur = StepWalk(step);
                fwd = cur - prevPt; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = prevFwd;
                fwd.Normalize();
            }
            cen[i] = (float3)(cur - startWorld);
            rt[i] = (float3)Vector3.Cross(Vector3.up, fwd).normalized;
            up[i] = new float3(0, 1, 0);
            prevPt = cur; prevFwd = fwd;
        }

        // Wrap the managed profile in short-lived NativeArrays for the build (disposed below — never leaks).
        var prof = new NativeArray<float2>(_profile, Allocator.TempJob);
        var seg  = new NativeArray<int>(_seg, Allocator.TempJob);
        var cum  = new NativeArray<float>(_cum, Allocator.TempJob);

        RoadTile tile = TakeTile();
        tile.transform.SetPositionAndRotation(startWorld, Quaternion.identity);
        tile.Build(cen, rt, up, R, prof, seg, cum, _profGroundHalf, roadThickness, _meta,
                   new[] { roadMaterial, footpathMaterial, groundMaterial, markingMaterial }, useBurst);

        // Capture the WORLD centreline ring points (the actual curved path) BEFORE disposing cen — so
        // CenterlineAt follows the curve instead of chording the endpoints. cen[i] is tile-local (relative to
        // startWorld); ring 0 = rear (startWorld), ring R-1 = front (endWorld).
        var ringPts = new Vector3[R];
        for (int i = 0; i < R; i++) ringPts[i] = startWorld + (Vector3)cen[i];

        cen.Dispose(); rt.Dispose(); up.Dispose();
        prof.Dispose(); seg.Dispose(); cum.Dispose();

        Vector3 endWorld = _lead.head;
        _live.Insert(0, new TileSpan { tile = tile, startPt = startWorld, endPt = endWorld, pts = ringPts });

        // carry this tile's last sample + forward to the next tile's first ring
        _carryPt = endWorld;
        _carryFwd = prevFwd;
        _haveCarry = true;
    }

    // Roll once per tile to begin a hard sustained turn (corner / U-turn), respecting the cooldown.
    void MaybeStartSharpTurn()
    {
        if (_sharpActive) return;
        if (_sinceSharp < sharpTurnCooldown) return;
        Random.state = _rng;
        bool go = Random.value < sharpTurnChance;
        float sign = Random.value < 0.5f ? -1f : 1f;
        float target = Random.Range(70f, Mathf.Max(80f, sharpTurnMaxSweep));   // corner..U-turn
        _rng = Random.state;
        if (!go) return;
        _sharpActive = true;
        _sharpSign = (int)sign;
        _sharpSwept = 0f;
        _sharpTarget = target;
    }

    // Continuous procedural walk. turnRate is degrees per TurnRefDistance metres, so the road curves the
    // SAME amount regardless of ring density or tile length (longer step → proportionally more turn).
    const float TurnRefDistance = 30f;
    Vector3 StepWalk(float step)
    {
        Random.state = _rng;
        float k = step / TurnRefDistance;
        float deltaYaw;

        if (_sharpActive)
        {
            // hold a hard turn until the target sweep is reached, then ease the last bit and end it
            float remaining = _sharpTarget - _sharpSwept;
            float thisStep = Mathf.Min(sharpTurnRate * k, Mathf.Max(2f, remaining));
            deltaYaw = _sharpSign * thisStep;
            _sharpSwept += thisStep;
            if (_sharpSwept >= _sharpTarget) { _sharpActive = false; _sinceSharp = 0f; _lead.turnRate = 0f; }
        }
        else
        {
            if (Random.value < behaviourChangeChance)
                _lead.turnRate = Random.value < straightChance ? 0f : Random.Range(-maxTurnRate, maxTurnRate);
            deltaYaw = (_lead.turnRate + Random.Range(-wobble, wobble)) * k;
            _sinceSharp += step;
        }

        _lead.yaw += deltaYaw;
        _lead.head += Quaternion.Euler(0f, _lead.yaw, 0f) * Vector3.forward * step;
        _rng = Random.state;
        return _lead.head;
    }

    RoadTile TakeTile()
    {
        RoadTile t = _pool.Count > 0 ? _pool.Pop() : NewTile();
        t.Acquire();
        return t;
    }

    RoadTile NewTile()
    {
        EnsureParent();
        var go = new GameObject("RoadTile");
        go.transform.SetParent(_tilesParent, false);
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;   // never bake into the scene
        return go.AddComponent<RoadTile>();
    }

    void ReleaseSpan(int index)
    {
        TileSpan s = _live[index];
        s.tile.Release();
        _pool.Push(s.tile);
        _live.RemoveAt(index);
    }

    /// World frame at the leading edge (front tile's far end), so Phase C can place footpath/lane content
    /// aligned to the road there.
    public bool TryGetLeadFrame(out Vector3 pos, out Vector3 fwd, out Vector3 right)
    {
        pos = default; fwd = Vector3.forward; right = Vector3.right;
        if (_live.Count == 0) return false;
        TileSpan s = _live[0];                       // front-most
        pos = s.endPt;
        Vector3 d = s.endPt - s.startPt; d.y = 0f;
        fwd = d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
        right = Vector3.Cross(Vector3.up, fwd).normalized;
        return true;
    }

    /// Fill `outPts` with the live road CENTRELINE as a WORLD polyline, ordered BACK (index 0, behind the bus) →
    /// FRONT (last index, the leading edge ahead of the bus). Deduped where adjacent tiles share a boundary knot.
    /// For road-relative content that wants to march the road GEOMETRY directly in world space — this is
    /// completely INDEPENDENT of the bus's tracked position (_busTileF), so it never suffers the spawn-time lag
    /// that made bus-relative SampleRoad place content far ahead. The caller owns `outPts` (reuse it → no GC).
    public void GetCenterline(System.Collections.Generic.List<Vector3> outPts)
    {
        outPts.Clear();
        // _live[0] = front-most tile, _live[count-1] = back-most. Iterate back→front; within a tile pts[0]=rear
        // (startPt) .. pts[R-1]=front (endPt), so 0..R-1 is also back→front. Adjacent tiles share a boundary knot.
        for (int t = _live.Count - 1; t >= 0; t--)
        {
            var pts = _live[t].pts;
            if (pts == null) continue;
            for (int k = 0; k < pts.Length; k++)
            {
                Vector3 p = pts[k];
                if (outPts.Count > 0 && (outPts[outPts.Count - 1] - p).sqrMagnitude < 1e-4f) continue;  // dedupe seam
                outPts.Add(p);
            }
        }
    }

    // --- Road sampling for traffic / world content (road-relative = floating-origin-invariant) ---
    public float TileLength => tileLength;
    public int LiveTileCount => _live.Count;
    /// How far (metres) of road exists ahead of / behind the bus right now. Traffic clamps its range to this.
    /// Uses the continuous bus position so it matches SampleRoad exactly.
    public float MetresAhead => (_live.Count - 1 - _busTileF) * tileLength;
    public float MetresBehind => _busTileF * tileLength;

    /// Sample a point on the road at `metresFromBus` (signed: + ahead, - behind) along the centreline,
    /// then offset `lateral` metres to the RIGHT of the road. Returns the world position + the road frame
    /// there (fwd = road direction of travel, right = +X side). Everything is road-relative, so a
    /// floating-origin recenter never changes the result. Returns false if that distance is off the live road.
    ///
    /// The frame (fwd/right) is derived from a FINITE DIFFERENCE of two nearby centreline points, not the
    /// raw span direction — so it stays CONTINUOUS across span boundaries (the old per-span direction
    /// snapped at each join, which flipped the lateral offset and made vehicles teleport sideways on curves).
    public bool SampleRoad(float metresFromBus, float lateral, out Vector3 pos, out Vector3 fwd, out Vector3 right)
    {
        pos = default; fwd = Vector3.forward; right = Vector3.right;
        if (_live.Count < 2 || tileLength <= 0f) return false;

        // tile index increases toward the BACK (_live[0]=front). Ahead of the bus = LOWER tileF.
        // Use the CONTINUOUS bus position (_busTileF), not the discrete index — otherwise traffic jumps a
        // whole tile every time a front tile is appended (index++) or the bus crosses a tile boundary.
        float tileF = _busTileF - metresFromBus / tileLength;
        if (tileF < 0f || tileF > _live.Count - 1) return false;   // off the live road

        Vector3 center = CenterlineAt(tileF);

        // forward = the travel direction (toward lower metresFromBus = toward the FRONT = lower tileF).
        // Use a WIDER baseline (~half a tile) so the tangent is smooth through curves — a tiny eps picks up
        // piecewise-linear kinks and makes vehicles/peds jitter their heading on turns.
        float eps = 0.4f;
        float tB = Mathf.Min(_live.Count - 1, tileF + eps);
        float tF = Mathf.Max(0f, tileF - eps);
        Vector3 ahead = CenterlineAt(tF);
        Vector3 behind = CenterlineAt(tB);
        Vector3 d = ahead - behind; d.y = 0f;
        fwd = d.sqrMagnitude < 1e-8f ? Vector3.forward : d.normalized;
        // `right` = the SAME per-point perpendicular the road MESH uses for its cross-section at this ring
        // (mesh: rt[i] = Cross(up, fwd) at each ring). Returning this exact normal (not Cross(up, smoothedFwd))
        // is what makes a point at lateral=RoadHalf land EXACTLY on the mesh footpath edge through curves, and
        // keeps callers' outward direction consistent with where their offset point was placed. (Earlier the
        // smoothed/averaged offset diverged from the mesh normal on bends → buildings/peds drifted onto the road.)
        right = Perp(tileF);
        if (right.sqrMagnitude < 1e-8f) right = Vector3.Cross(Vector3.up, fwd).normalized;

        pos = Mathf.Abs(lateral) > 0.01f ? center + right * lateral : center;
        return true;
    }

    /// Continuous centreline position for a fractional tile index. t=0 within a span sits at its endPt (the
    /// front edge), t=1 at startPt (the rear edge) — and because adjacent spans share that boundary point
    /// exactly, the piecewise-linear result is C0-continuous (no position jump at joins).
    Vector3 CenterlineAt(float tileF)
    {
        tileF = Mathf.Clamp(tileF, 0f, _live.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(tileF), 0, _live.Count - 1);
        float t = tileF - i;                          // 0 = front edge (endPt), 1 = rear edge (startPt)
        TileSpan s = _live[i];

        // Follow the actual CURVED path via the stored ring points (pts[0]=rear/startPt … pts[R-1]=front/endPt).
        // Endpoint-only Lerp chorded straight across curves (the "centreline goes straight on a corner" bug).
        if (s.pts != null && s.pts.Length >= 2)
        {
            int R = s.pts.Length;
            float ring = (1f - t) * (R - 1);          // t=0→front=pts[R-1]; t=1→rear=pts[0]
            int r0 = Mathf.Clamp(Mathf.FloorToInt(ring), 0, R - 1);
            int r1 = Mathf.Min(r0 + 1, R - 1);
            return Vector3.Lerp(s.pts[r0], s.pts[r1], ring - r0);
        }
        return Vector3.Lerp(s.endPt, s.startPt, t);   // fallback (shouldn't happen)
    }

    // road-right (perpendicular) at a fractional tile index, from the local tangent. Baseline ≈ ONE mesh ring
    // step (tileLength/(ringsPerTile-1)) so this normal matches the mesh's per-ring rt[i] — keeps lateral-offset
    // points (buildings/peds/stops) sitting exactly on the mesh cross-section through curves.
    Vector3 Perp(float tileF)
    {
        float e = 1f / Mathf.Max(2, ringsPerTile - 1);   // one ring spacing, in tile-fraction units
        Vector3 a = CenterlineAt(Mathf.Max(0f, tileF - e));
        Vector3 b = CenterlineAt(Mathf.Min(_live.Count - 1, tileF + e));
        Vector3 d = a - b; d.y = 0f;
        Vector3 f = d.sqrMagnitude < 1e-8f ? Vector3.forward : d.normalized;
        return Vector3.Cross(Vector3.up, f).normalized;
    }

    // --- spawn ---
    // Geometric (NO raycast — raycasts depend on async collider bake timing and can hit the bus itself).
    // The road's top surface at a LANE is exactly the centreline Y (profile Y = 0 on the road), so the
    // spawn Y is simply the span-start Y. Deterministic and order-independent.
    public bool TryGetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = default;
        if (_live.Count == 0) return false;
        // MIDDLE tile, MIDDLE of its span → road both ahead and behind the bus from the very first frame.
        int idx = Mathf.Clamp(_live.Count / 2, 0, _live.Count - 1);
        TileSpan s = _live[idx];
        Vector3 mid = (s.startPt + s.endPt) * 0.5f;
        Vector3 fwd = (s.endPt - s.startPt); fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float laneX = _zone.LaneCenterX(_zone.lanesPerDirection - 1, true);
        pos = mid + right * laneX;            // Y = midpoint Y = the lane surface
        rot = Quaternion.LookRotation(fwd, Vector3.up);
        return true;
    }

    void TryPlaceBus()
    {
        if (_busPlaced) return;
        ResolveTarget();
        if (target == null) return;

        var bc = target.GetComponent<BusController>() ?? target.GetComponentInParent<BusController>();
        // Wait until the bus is FULLY initialised: BusController.Start detaches the sphere (parent==null)
        // and measures the radius. Teleporting before that desyncs the rig → the "spawn in the sky" bug.
        if (bc != null && bc.sphere != null && bc.sphere.transform.parent != null) return;

        if (!TryGetSpawnPose(out Vector3 pos, out Quaternion rot)) return;
        if (bc != null) bc.TeleportTo(pos, rot); else target.SetPositionAndRotation(pos, rot);
        _busPlaced = true;

        // seed the chain tracker at the spawn span (the middle tile — matches TryGetSpawnPose)
        _busTileIndex = Mathf.Clamp(_live.Count / 2, 0, _live.Count - 1);
        _busTileF = _busTileIndex + 0.5f;     // bus spawns mid-span; seed continuous pos so traffic frame-1 is sane
        // DON'T set _busTileFInit here: leaving it false makes the FIRST TrackBusIndex SNAP _busTileF to the bus's
        // true projected position (raw) instead of slowly EASING toward it. The ease left _busTileF ~2 tiles
        // (~120m) off the real bus for ~1s after spawn → road-relative content (buildings) seeded against a
        // wrong metres=0 and landed far ahead. Snapping on frame 1 keeps SampleRoad(0,0) ≈ the bus from the start.
        _busTileFInit = false;
        _busIndexValid = true;
    }

    // --- floating origin ---
    public void OnOriginShifted(Vector3 delta)
    {
        // Shift EVERY cached world position by the same delta. Missing even one desyncs it from the rest:
        // forgetting _carryPt made the NEXT tile spawn at the pre-shift spot, far from the shifted road —
        // the giant connector gap / "road generating way further away" bug.
        _lead.head += delta;
        _carryPt += delta;
        for (int i = 0; i < _live.Count; i++)
        {
            _live[i].startPt += delta; _live[i].endPt += delta;
            var pts = _live[i].pts;                    // shift the curved-path ring points too, or CenterlineAt
            if (pts != null) for (int k = 0; k < pts.Length; k++) pts[k] += delta;   // desyncs after a recenter
        }
        OnOriginShiftedEvent?.Invoke(delta);
        // tile transforms are children of this object, which the scene-wide shift already moved.
    }

    static Material MakeUrp(string name, Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        return m;
    }

    // Diagnostic: draw the chain (front=green → back=red), connect consecutive tiles to reveal any split,
    // and mark the tile the streamer THINKS the bus is on (magenta) + a line to the actual bus.
    void OnDrawGizmosSelected()
    {
        if (_live == null || _live.Count == 0) return;
        for (int i = 0; i < _live.Count; i++)
        {
            float t = _live.Count > 1 ? i / (float)(_live.Count - 1) : 0f;
            Gizmos.color = Color.Lerp(Color.green, Color.red, t);
            Vector3 a = _live[i].startPt, b = _live[i].endPt;
            Gizmos.DrawLine(a, b);
            if (i < _live.Count - 1)                       // connector to next tile — a gap here = a real split
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(_live[i].endPt, _live[i + 1].startPt);
            }
        }
        if (_busIndexValid && _busTileIndex >= 0 && _busTileIndex < _live.Count)
        {
            Vector3 mid = (_live[_busTileIndex].startPt + _live[_busTileIndex].endPt) * 0.5f;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(mid, 2f);
            if (target != null) Gizmos.DrawLine(mid, target.position);
        }
    }
}
