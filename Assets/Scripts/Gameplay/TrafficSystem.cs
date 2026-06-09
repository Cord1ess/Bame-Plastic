using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// L1 traffic: deterministic, pooled, road-relative vehicles populating both directions of the endless
/// road around the bus. Cars/rickshaws/buses spawn from a seeded RNG (so all multiplayer clients generate
/// identical traffic — no syncing), drive kinematically, ride the road via TrafficVehicle's logical
/// road-relative position, and recycle when they fall off the live road. No avoidance/knockback yet (L2/L3).
///
/// FLOATING-ORIGIN: traffic positions are road-relative (metresFromBus), so a recenter changes nothing —
/// OnOriginShifted is a no-op by design. That's the payoff of the road-relative model.
[RequireComponent(typeof(TiledRoadStreamer))]
public class TrafficSystem : MonoBehaviour
{
    [Header("Population")]
    [Tooltip("UPPER BOUND on vehicles alive at once. Actual count is limited by spacing — the street fills " +
             "only as far as the gaps allow, so raising this past what fits does nothing.")]
    [Min(0)] public int maxVehicles = 8;
    [Tooltip("Don't spawn within this margin (m) of the live road's far edge (avoids visible pop-in there).")]
    public float edgeMargin = 20f;

    [Header("Spacing (keeps gaps so the bus can weave)")]
    [Tooltip("Min clear gap (m) along the road between two vehicles sharing the same side+lane before a new " +
             "one may spawn there. Bigger = roomier; smaller = denser/tighter to thread.")]
    public float minGap = 20f;
    [Tooltip("Two vehicles count as 'same lane' (and must respect minGap) if their lateral offsets are within " +
             "this many metres. Below it = sharing space; beyond = a different lane, no gap needed.")]
    public float sameLaneWidth = 2.5f;
    [Tooltip("How many random spots to try per spawn before giving up this frame (keeps it cheap).")]
    [Min(1)] public int spawnAttempts = 6;

    [Header("Spawn ahead / cull behind (camera can't look back)")]
    [Tooltip("Vehicles spawn no closer than this far AHEAD of the bus, so they appear in the distance and " +
             "drive toward you — never pop in right in front. Should be beyond the visible-ahead range.")]
    public float spawnMinAhead = 70f;
    [Tooltip("Recycle a vehicle once it's this far BEHIND the bus. Small, since the player can't look back.")]
    public float cullBehind = 18f;

    [Header("Mix (relative weights)")]
    public float rickshawWeight = 0.65f;   // bikes/rickshaws dominate (nimble, small — leave gaps to thread)
    public float carWeight = 0.25f;
    public float busWeight = 0.10f;
    [Tooltip("Hard cap on AI buses alive at once (big blockers — keep the road threadable for the guide line).")]
    [Min(0)] public int maxBuses = 2;

    [Header("Speeds (m/s)")]
    public Vector2 rickshawSpeed = new Vector2(5f, 8f);
    public Vector2 carSpeed = new Vector2(10f, 16f);
    public Vector2 busSpeed = new Vector2(8f, 13f);

    [Header("Determinism")]
    [Tooltip("0 = use the road's auto-seed offset so traffic is stable per run; set to pin a specific mix.")]
    public int seed = 0;

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<TrafficVehicle> _live = new List<TrafficVehicle>();
    readonly Stack<TrafficVehicle> _pool = new Stack<TrafficVehicle>();
    Random.State _rng;
    bool _ready;

    /// Live vehicles, for the DriverGuide planner to read (positions/speeds in road-relative space).
    public IReadOnlyList<TrafficVehicle> Live => _live;
    int _nextId = 1;

    // rivals: real TrafficVehicles with a RivalBrain. Ticked/separated/avoided like everyone else, but NEVER
    // pooled away — when one falls behind we reposition it ahead so it keeps competing all shift.
    readonly List<TrafficVehicle> _rivals = new List<TrafficVehicle>();

    /// Spawn (or returns) a persistent rival bus driven by a RivalBrain. Called by RivalManager at start.
    public TrafficVehicle SpawnRival(string rivalName, Color color)
    {
        if (!_ready) return null;
        EnsureParent();
        var v = NewVehicle();
        var brain = v.gameObject.GetComponent<RivalBrain>();
        if (brain == null) brain = v.gameObject.AddComponent<RivalBrain>();
        brain.SetName(rivalName);

        float lat = LateralFor(TrafficVehicle.Kind.Bus, +1);
        float metres = spawnMinAhead + Random.Range(10f, 40f);
        v.Acquire(TrafficVehicle.Kind.Bus, _nextId++, metres, lat, Random.Range(busSpeed.x, busSpeed.y), +1,
                  SizeFor(TrafficVehicle.Kind.Bus), color);
        v.Brain = brain;                       // attach AFTER Acquire (Acquire doesn't touch Brain)
        _live.Add(v);
        _rivals.Add(v);
        return v;
    }

    void Awake()
    {
        _road = GetComponent<TiledRoadStreamer>();
        _zone = GetComponent<RoadZone>();
    }

    void OnEnable()
    {
        // recenter is a no-op for road-relative traffic, but subscribe so the contract is explicit.
        if (_road != null) _road.OnOriginShiftedEvent += OnOriginShifted;
    }
    void OnDisable()
    {
        if (_road != null) _road.OnOriginShiftedEvent -= OnOriginShifted;
    }

    void OnOriginShifted(Vector3 delta) { /* no-op: traffic is road-relative (metresFromBus) */ }

    void Start()
    {
        if (!Application.isPlaying) return;
        Random.InitState(seed != 0 ? seed : 0xB00 ^ Random.Range(int.MinValue, int.MaxValue));
        _rng = Random.state;
        EnsureParent();
        _ready = true;
    }

    void EnsureParent()
    {
        if (_parent != null) return;
        var go = new GameObject("Traffic");
        go.transform.SetParent(transform, false);
        _parent = go.transform;
    }

    void Update()
    {
        if (!_ready) return;
        BusController bus = BusController.Instance;
        float busSpeed = bus != null ? bus.SpeedMps : 0f;
        float busLateral = _road.BusLateral;            // bus's offset on the road (for traffic avoidance)
        float dt = Time.deltaTime;

        float ahead = _road.MetresAhead - edgeMargin;   // far edge of usable road ahead

        // tick + recycle. Each vehicle senses the whole live list + the bus (L2 avoidance). Cull anything
        // that fell BEHIND the bus past cullBehind (off-screen — camera can't look back), ran off the live
        // road, or somehow got past the far edge ahead.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            TrafficVehicle v = _live[i];
            bool onRoad = v.Tick(_live, busSpeed, busLateral, dt);
            float m = v.metresFromBus;
            bool gone = !onRoad || m < -cullBehind || m > ahead + 5f;
            if (!gone) continue;
            if (v.Brain != null) RepositionRivalAhead(v, ahead);   // rivals never get pooled — re-deploy ahead
            else Recycle(i);
        }

        // Deterministic soft separation: a cheap safety net so vehicles never STACK even if avoidance failed
        // to resolve in time (two converging on one gap). Same-direction pairs that overlap in BOTH metres
        // and lateral get nudged apart in logical space (no physics → stays deterministic across clients).
        SeparateOverlaps(dt);

        // top up: new vehicles spawn FAR AHEAD (in the distance, off-screen where the road is generating)
        // and drive toward the bus — never pop in nearby.
        int guard = 0;
        while (_live.Count < maxVehicles && guard++ < maxVehicles)
        {
            if (!SpawnOne(ahead)) break;   // no room ahead (road too short right now) — try again next frame
        }
    }

    bool SpawnOne(float ahead)
    {
        // need a window between spawnMinAhead and the far edge to place a vehicle ahead
        if (ahead <= spawnMinAhead + 2f) return false;

        Random.state = _rng;

        // pick the vehicle's identity first (kind/dir/lane), then hunt for a spot ahead that has a clear gap
        // from any other vehicle in the same side+lane. If no clear spot in a few tries, give up this frame
        // (so the street fills only as densely as the spacing allows — never stacking vehicles).
        TrafficVehicle.Kind kind = PickKind();
        if (kind == TrafficVehicle.Kind.Bus && CountAheadBuses() >= maxBuses)
            kind = TrafficVehicle.Kind.Car;   // bus cap reached → downgrade to a car
        int dir = Random.value < 0.55f ? +1 : -1;
        float lat = LateralFor(kind, dir) + Random.Range(-0.4f, 0.4f);

        float metres = 0f;
        bool placed = false;
        for (int attempt = 0; attempt < spawnAttempts; attempt++)
        {
            float candidate = Random.Range(spawnMinAhead, ahead);
            if (HasClearGap(candidate, lat, dir)) { metres = candidate; placed = true; break; }
        }

        if (!placed) { _rng = Random.state; return false; }

        float spd = SpeedFor(kind);
        Vector3 size = SizeFor(kind);
        Color col = ColorFor(kind);

        _rng = Random.state;

        TrafficVehicle v = Take();
        v.Acquire(kind, _nextId++, metres, lat, spd, dir, size, col);
        _live.Add(v);
        return true;
    }

    // True if no existing vehicle in the SAME side+lane sits within minGap metres of `metres`. Vehicles on
    // a different lateral band (|Δlat| > sameLaneWidth) or opposite direction don't block — so traffic packs
    // side-by-side across the road but keeps a longitudinal gap within each lane for weaving.
    bool HasClearGap(float metres, float lat, int dir)
    {
        for (int i = 0; i < _live.Count; i++)
        {
            TrafficVehicle o = _live[i];
            if (o.dir != dir) continue;                                 // oncoming lane — separate space
            if (Mathf.Abs(o.lateral - lat) > sameLaneWidth) continue;   // different lane — fine alongside
            if (Mathf.Abs(o.metresFromBus - metres) < minGap) return false;
        }
        return true;
    }

    // Anti-stack: nudge any same-direction pair that's overlapping in BOTH axes apart, along whichever axis
    // they overlap LESS (cheaper escape). Deterministic (index order decides who moves which way), no physics.
    // Skips knocked vehicles (those are mid-tumble). O(N^2) — fine at this N.
    void SeparateOverlaps(float dt)
    {
        const float lateralBuffer = 0.25f;
        const float pushSpeed = 6f;       // m/s of separation correction
        for (int a = 0; a < _live.Count; a++)
        {
            TrafficVehicle va = _live[a];
            if (va.Knocked) continue;
            for (int b = a + 1; b < _live.Count; b++)
            {
                TrafficVehicle vb = _live[b];
                if (vb.Knocked || vb.dir != va.dir) continue;             // only same-direction pairs

                float dM = vb.metresFromBus - va.metresFromBus;
                float dL = vb.lateral - va.lateral;
                float overlapM = (va.HalfLen + vb.HalfLen) - Mathf.Abs(dM);
                float overlapL = (va.HalfWidth + vb.HalfWidth + lateralBuffer) - Mathf.Abs(dL);
                if (overlapM <= 0f || overlapL <= 0f) continue;           // not overlapping in both → fine

                float step = pushSpeed * dt;
                if (overlapL < overlapM)                                  // easier to part sideways
                {
                    float dir = dL >= 0f ? 1f : -1f;                      // push b to +side, a to -side
                    va.Nudge(0f, -dir * step);
                    vb.Nudge(0f,  dir * step);
                }
                else                                                      // part longitudinally
                {
                    float dir = dM >= 0f ? 1f : -1f;
                    va.Nudge(-dir * step, 0f);
                    vb.Nudge( dir * step, 0f);
                }
            }
        }
    }

    TrafficVehicle.Kind PickKind()
    {
        float total = Mathf.Max(0.0001f, rickshawWeight + carWeight + busWeight);
        float r = Random.value * total;
        if (r < rickshawWeight) return TrafficVehicle.Kind.Rickshaw;
        if (r < rickshawWeight + carWeight) return TrafficVehicle.Kind.Car;
        return TrafficVehicle.Kind.Bus;
    }

    int CountAheadBuses()
    {
        int n = 0;
        for (int i = 0; i < _live.Count; i++)
            if (_live[i].kind == TrafficVehicle.Kind.Bus && _live[i].metresFromBus > 0f) n++;
        return n;
    }

    // Lateral target by kind + direction. forwardSide sign: -X for your direction (LHT), +X oncoming.
    float LateralFor(TrafficVehicle.Kind kind, int dir)
    {
        bool forward = dir > 0;
        float sideSign = (forward == _zone.leftHandTraffic) ? -1f : 1f;   // forward+LHT → -X
        float inner = _zone.MedianHalf;
        float outer = _zone.DriveHalf;
        float t;                                  // 0 = inner (near median), 1 = outer (near footpath)
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: t = 0.82f; break;   // hug the edge
            case TrafficVehicle.Kind.Bus:      t = 0.30f; break;   // toward inner
            default:                            t = 0.55f; break;   // cars mid
        }
        float x = Mathf.Lerp(inner, outer, t);
        return sideSign * x;
    }

    float SpeedFor(TrafficVehicle.Kind kind)
    {
        Vector2 r = kind == TrafficVehicle.Kind.Rickshaw ? rickshawSpeed
                  : kind == TrafficVehicle.Kind.Bus ? busSpeed : carSpeed;
        return Random.Range(r.x, r.y);
    }

    static Vector3 SizeFor(TrafficVehicle.Kind kind)
    {
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: return new Vector3(1.0f, 1.6f, 2.0f);
            case TrafficVehicle.Kind.Bus:      return new Vector3(2.4f, 3.0f, 9.0f);
            default:                            return new Vector3(1.8f, 1.5f, 4.2f);
        }
    }

    static Color ColorFor(TrafficVehicle.Kind kind)
    {
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: return new Color(0.2f, 0.55f, 0.35f);   // CNG-green-ish
            case TrafficVehicle.Kind.Bus:      return new Color(0.75f, 0.65f, 0.25f);
            default:                            return new Color(0.6f, 0.62f, 0.66f);
        }
    }

    TrafficVehicle Take()
    {
        TrafficVehicle v = _pool.Count > 0 ? _pool.Pop() : NewVehicle();
        return v;
    }

    TrafficVehicle NewVehicle()
    {
        EnsureParent();
        var go = new GameObject("Vehicle");
        go.transform.SetParent(_parent, false);
        var v = go.AddComponent<TrafficVehicle>();
        v.Init(_road);
        return v;
    }

    void Recycle(int index)
    {
        TrafficVehicle v = _live[index];
        v.Release();
        _pool.Push(v);
        _live.RemoveAt(index);
    }

    // A rival fell off the live road (usually behind) — re-deploy it ahead so it keeps competing, instead of
    // pooling it. Keeps it in _live; just resets its road-relative position to a fresh forward lane.
    void RepositionRivalAhead(TrafficVehicle v, float ahead)
    {
        float metres = Mathf.Clamp(spawnMinAhead + Random.Range(10f, 40f), spawnMinAhead, Mathf.Max(spawnMinAhead + 1f, ahead));
        float lat = LateralFor(TrafficVehicle.Kind.Bus, +1) + Random.Range(-0.3f, 0.3f);
        v.Nudge(metres - v.metresFromBus, lat - v.lateral);
        v.speed = Random.Range(busSpeed.x, busSpeed.y);
    }
}
