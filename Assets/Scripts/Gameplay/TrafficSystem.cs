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
    [Tooltip("UPPER BOUND on vehicles alive at once (both sides). Actual count is limited by spacing.")]
    [Min(0)] public int maxVehicles = 75;
    [Tooltip("Target vehicles on the SAME-direction (going) side.")]
    [Min(0)] public int maxSameDir = 25;
    [Tooltip("Target vehicles on the ONCOMING (upcoming) side — HIGHER so the oncoming lane feels busier.")]
    [Min(0)] public int maxOncoming = 50;
    [Tooltip("Max new vehicles spawned per frame while filling toward the targets (ramps the road up fast " +
             "without a one-frame hitch; the road reaches the target over a few frames).")]
    [Min(1)] public int maxSpawnPerFrame = 8;
    [Tooltip("Don't spawn within this margin (m) of the live road's far edge (avoids visible pop-in there).")]
    public float edgeMargin = 20f;

    [Header("Spacing (keeps gaps so the bus can weave)")]
    [Tooltip("Min clear gap (m) along the road between two vehicles sharing the same side+lane before a new " +
             "one may spawn there. Bigger = roomier; smaller = denser/tighter to thread.")]
    public float minGap = 13f;
    [Tooltip("Two vehicles count as 'same lane' (and must respect minGap) if their lateral offsets are within " +
             "this many metres. Below it = sharing space; beyond = a different lane, no gap needed.")]
    public float sameLaneWidth = 2.5f;
    [Tooltip("How many random spots to try per spawn before giving up this frame (keeps it cheap).")]
    [Min(1)] public int spawnAttempts = 6;

    [Header("Spawn ahead / cull behind (camera can't look back)")]
    [Tooltip("Vehicles spawn no closer than this far AHEAD of the bus — set it NEAR the smog distance (~180-220m, " +
             "DayNightController.smogFullDistance is ~230) so they emerge from the haze in the distance and drive " +
             "toward you, NEVER popping in nearby. Too low and you'll see them appear right in front.")]
    public float spawnMinAhead = 185f;
    [Tooltip("Recycle a vehicle once it's this far BEHIND the bus. Small, since the player can't look back.")]
    public float cullBehind = 18f;

    [Header("Mix (relative weights)")]
    public float rickshawWeight = 0.74f;   // rickshaws + CNGs dominate (nimble, small — leave gaps to thread)
    public float carWeight = 0.16f;        // cars/bikes
    public float busWeight = 0.05f;
    public float truckWeight = 0.05f;      // normal trucks (heavy-ish, not a wall)
    [Tooltip("Hard cap on AI buses alive at once (big blockers — keep the road threadable for the guide line).")]
    [Min(0)] public int maxBuses = 2;
    [Tooltip("Chance per second an oncoming small vehicle cuts ACROSS to our side and merges (Dhaka chaos hazard).")]
    [Range(0f, 0.3f)] public float crossChancePerSec = 0.03f;

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
    // DEDICATED RNG (System.Random) seeded from the shared session seed → traffic is deterministic per client AND
    // independent of Unity's global Random (which every other system consumes at different rates → would desync /
    // corrupt determinism). This is the robust MP-safe choice. Helpers below.
    System.Random _rand;
    bool _ready;
    float RandF() => (float)_rand.NextDouble();
    float RandRange(float a, float b) => a + (b - a) * RandF();

    /// Live vehicles, for the DriverGuide planner to read (positions/speeds in road-relative space).
    public IReadOnlyList<TrafficVehicle> Live => _live;
    int _nextId = 1;

    // rivals: real TrafficVehicles with a RivalBrain. Ticked/separated/avoided like everyone else, but NEVER
    // pooled away — when one falls behind we reposition it ahead so it keeps competing all shift.
    readonly List<TrafficVehicle> _rivals = new List<TrafficVehicle>();
    /// All deployed rivals — a RivalBrain reads this to QUEUE behind other rivals camping the same stop.
    public IReadOnlyList<TrafficVehicle> Rivals => _rivals;

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
        float metres = spawnMinAhead + RandRange(10f, 40f);
        v.Acquire(TrafficVehicle.Kind.Bus, _nextId++, metres, lat, RandRange(busSpeed.x, busSpeed.y), +1,
                  SizeFor(TrafficVehicle.Kind.Bus), color);
        v.Brain = brain;                       // attach AFTER Acquire (Acquire doesn't touch Brain)
        v.SetSolid(true);                      // rivals are SOLID physics walls — the bus clashes naturally
        _live.Add(v);
        _rivals.Add(v);
        return v;
    }

    public static TrafficSystem Instance { get; private set; }

    void Awake()
    {
        Instance = this;
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
        // shared session seed (all MP clients match) ⊕ a constant so traffic isn't correlated with the road seed;
        // falls back to this component's seed or a time seed in solo/edit.
        int s = seed != 0 ? seed
              : (SessionContext.Instance != null && SessionContext.Instance.Seed != 0
                    ? SessionContext.Instance.Seed ^ 0x7A77C0DE
                    : System.Environment.TickCount);
        _rand = new System.Random(s);
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

        // LANE CROSSING: occasionally a small oncoming vehicle (rickshaw/car) cuts ACROSS the road onto our side
        // and merges into our flow — Dhaka chaos + a hazard to dodge. Rolled here (deterministic RNG), only for an
        // oncoming small vehicle that's well AHEAD (so the player sees the whole manoeuvre) and not a rival.
        for (int i = 0; i < _live.Count; i++)
        {
            TrafficVehicle v = _live[i];
            if (v.Brain != null || v.Crossing || v.dir != -1) continue;
            if (v.kind == TrafficVehicle.Kind.Bus || v.kind == TrafficVehicle.Kind.Truck) continue;   // only small ones cut across
            if (v.metresFromBus < 25f || v.metresFromBus > ahead - 20f) continue;
            if (RandF() < crossChancePerSec * dt) v.BeginCross();
        }

        // top up PER SIDE to its own target — the oncoming side carries more (maxOncoming) so it feels busier;
        // the going side stays at maxSameDir. Fill whichever side is furthest below its target each frame. New
        // vehicles spawn FAR AHEAD (off-screen, where the road is generating) and drive toward you — no pop-in.
        // top up both sides toward their targets. Spawning a few PER FRAME (not one) lets the road fill quickly;
        // a failed placement on one side doesn't abort the whole top-up — we try the other side, and only give
        // up after several consecutive fails (genuinely no room this frame; retry next frame).
        int guard = 0, fails = 0;
        int perFrame = maxSpawnPerFrame > 0 ? maxSpawnPerFrame : 8;   // guard: a 0-deserialize wouldn't stall spawning
        int budgetThisFrame = Mathf.Min(perFrame, maxVehicles - _live.Count);
        while (budgetThisFrame > 0 && _live.Count < maxVehicles && guard++ < maxVehicles && fails < 6)
        {
            int same = CountDir(+1), onc = CountDir(-1);
            float sameDeficit = maxSameDir - same, oncDeficit = maxOncoming - onc;
            if (sameDeficit <= 0f && oncDeficit <= 0f) break;        // both sides full
            int dir = oncDeficit >= sameDeficit ? -1 : +1;            // fill the emptier side
            if (SpawnOne(ahead, dir)) { budgetThisFrame--; fails = 0; }
            else
            {
                // that side had no room this attempt — try the OTHER side before counting a fail
                if (SpawnOne(ahead, -dir)) { budgetThisFrame--; fails = 0; }
                else fails++;
            }
        }
    }

    int CountDir(int dir)
    {
        int n = 0;
        for (int i = 0; i < _live.Count; i++) if (_live[i].dir == dir && _live[i].Brain == null) n++;  // rivals excluded
        return n;
    }

    bool SpawnOne(float ahead, int dir)
    {
        if (ahead <= spawnMinAhead + 2f) return false;

        TrafficVehicle.Kind kind = PickKind();
        if (kind == TrafficVehicle.Kind.Bus && CountAheadBuses() >= maxBuses)
            kind = TrafficVehicle.Kind.Car;   // bus cap reached → downgrade to a car

        // hunt for a clear spot across BOTH distance AND lane — the road has several lanes per direction, so a
        // full lane at one distance doesn't block another lane. Each attempt re-rolls the lateral lane.
        float metres = 0f, lat = 0f;
        bool placed = false;
        for (int attempt = 0; attempt < spawnAttempts; attempt++)
        {
            float candidate = RandRange(spawnMinAhead, ahead);
            float candLat = LateralFor(kind, dir);
            if (HasClearGap(candidate, candLat, dir)) { metres = candidate; lat = candLat; placed = true; break; }
        }
        if (!placed) return false;

        TrafficVehicle v = Take();
        v.Acquire(kind, _nextId++, metres, lat, SpeedFor(kind), dir, SizeFor(kind), ColorFor(kind));
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

    // HARD anti-overlap: FULLY resolve any overlapping pair every frame (not a slow nudge) so vehicles can NEVER
    // pass through / clip each other — covers ALL pairs (incl. oncoming + crossing), not just same-direction.
    // Deterministic (index order + position decide the split), no physics → MP-safe. O(N^2), fine at this N.
    // A couple of relaxation iterations settle 3-way pileups cleanly.
    void SeparateOverlaps(float dt)
    {
        const float lateralBuffer = 0.3f;
        for (int iter = 0; iter < 2; iter++)
        for (int a = 0; a < _live.Count; a++)
        {
            TrafficVehicle va = _live[a];
            // skip crossing (traversing the median) + a NORMAL vehicle mid-tumble. Rivals (Solid) ALWAYS separate
            // from each other even while the bus is shoving them — else two clashing rivals (always _shoveT>0)
            // would never part and would drive THROUGH each other.
            if (va.Crossing || (va.Knocked && !va.Solid)) continue;
            for (int b = a + 1; b < _live.Count; b++)
            {
                TrafficVehicle vb = _live[b];
                if (vb.Crossing || (vb.Knocked && !vb.Solid)) continue;

                float dM = vb.metresFromBus - va.metresFromBus;
                float dL = vb.lateral - va.lateral;
                float overlapM = (va.HalfLen + vb.HalfLen) - Mathf.Abs(dM);
                float overlapL = (va.HalfWidth + vb.HalfWidth + lateralBuffer) - Mathf.Abs(dL);
                if (overlapM <= 0f || overlapL <= 0f) continue;           // not overlapping in both → fine

                // push them FULLY apart along the axis with the smaller overlap (cheaper escape), splitting the
                // correction 50/50. This closes the overlap THIS frame — no slow-nudge lag → no visible clipping.
                if (overlapL <= overlapM)
                {
                    float sgn = dL >= 0f ? 1f : -1f;
                    float half = overlapL * 0.5f + 0.01f;
                    va.Nudge(0f, -sgn * half);
                    vb.Nudge(0f,  sgn * half);
                }
                else
                {
                    float sgn = dM >= 0f ? 1f : -1f;
                    float half = overlapM * 0.5f + 0.01f;
                    va.Nudge(-sgn * half, 0f);
                    vb.Nudge( sgn * half, 0f);
                }
            }
        }
    }

    TrafficVehicle.Kind PickKind()
    {
        float total = Mathf.Max(0.0001f, rickshawWeight + carWeight + busWeight + truckWeight);
        float r = RandF() * total;
        if (r < rickshawWeight) return TrafficVehicle.Kind.Rickshaw;
        if (r < rickshawWeight + carWeight) return TrafficVehicle.Kind.Car;
        if (r < rickshawWeight + carWeight + busWeight) return TrafficVehicle.Kind.Bus;
        return TrafficVehicle.Kind.Truck;
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
        float inner = _zone.MedianHalf + 0.6f;     // keep off the median line
        float outer = _zone.DriveHalf - 0.6f;      // keep off the kerb
        // each kind BIASES a zone but SPREADS across it (real lanes) so a whole kind isn't one packed lane:
        //   rickshaw = outer half (edge), bus = inner half (median side), car = anywhere across the drive.
        float tMin, tMax;
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: tMin = 0.55f; tMax = 1.0f;  break;   // outer half
            case TrafficVehicle.Kind.Bus:      tMin = 0.0f;  tMax = 0.45f; break;   // inner half
            case TrafficVehicle.Kind.Truck:    tMin = 0.0f;  tMax = 0.5f;  break;   // inner-ish like big vehicles
            default:                            tMin = 0.1f;  tMax = 0.95f; break;   // cars fill the road
        }
        float t = RandRange(tMin, tMax);
        float x = Mathf.Lerp(inner, outer, t);
        return sideSign * x;
    }

    float SpeedFor(TrafficVehicle.Kind kind)
    {
        Vector2 r = kind == TrafficVehicle.Kind.Rickshaw ? rickshawSpeed
                  : (kind == TrafficVehicle.Kind.Bus || kind == TrafficVehicle.Kind.Truck) ? busSpeed : carSpeed;
        return RandRange(r.x, r.y);
    }

    static Vector3 SizeFor(TrafficVehicle.Kind kind)
    {
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: return new Vector3(1.3f, 1.9f, 2.6f);    // a bit bigger
            case TrafficVehicle.Kind.Bus:      return new Vector3(3.2f, 4.2f, 13.5f);
            case TrafficVehicle.Kind.Truck:    return new Vector3(2.5f, 3.0f, 8.0f);    // own size (smaller than bus)
            default:                            return new Vector3(2.2f, 1.8f, 5.0f);   // CARS bigger (were too small)
        }
    }

    static Color ColorFor(TrafficVehicle.Kind kind)
    {
        switch (kind)
        {
            case TrafficVehicle.Kind.Rickshaw: return new Color(0.2f, 0.55f, 0.35f);   // CNG-green-ish
            case TrafficVehicle.Kind.Bus:      return new Color(0.75f, 0.65f, 0.25f);
            case TrafficVehicle.Kind.Truck:    return new Color(0.55f, 0.5f, 0.45f);
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
        float metres = Mathf.Clamp(spawnMinAhead + RandRange(10f, 40f), spawnMinAhead, Mathf.Max(spawnMinAhead + 1f, ahead));
        float lat = LateralFor(TrafficVehicle.Kind.Bus, +1) + RandRange(-0.3f, 0.3f);
        v.Nudge(metres - v.metresFromBus, lat - v.lateral);
        v.speed = RandRange(busSpeed.x, busSpeed.y);
    }
}
