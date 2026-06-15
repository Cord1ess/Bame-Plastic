using System.Collections.Generic;
using UnityEngine;

/// PHASE C — bus stops + waiting crowds on the endless tiled road.
///
/// Driven by TiledRoadStreamer.OnForwardAdvanced: every few new sections it drops a bus stop on the
/// LEFT footpath (our driving side under LHT), borrows a waiting crowd from the shared PassengerPool,
/// and runs the same two-phase pickup:
///   1) bus within gatherRange  -> part of the crowd walks to the curb,
///   2) bus within boardRange + slow (BusPassengers.CanBoard) -> the curb crowd boards 1-by-1.
/// Stops ride the road (parented to it, so floating-origin still works) and are recycled — un-boarded
/// passengers Returned to the pool — once the bus is well past them. Nothing is Instantiated mid-game.
public class SplineStopSpawner : MonoBehaviour
{
    public static SplineStopSpawner Instance { get; private set; }

    [Header("Placement")]
    [Tooltip("Drop a stop on roughly every Nth advanced section (randomised a little). Lower = MORE FREQUENT stops.")]
    public int sectionsPerStop = 2;
    [Tooltip("Max live stops at once (older ones recycle as the bus passes).")]
    public int maxLiveStops = 6;
    [Tooltip("Recycle a stop once the bus is this far PAST it (metres behind).")]
    public float recycleDistance = 70f;
    [Header("Shelter model")]
    [Tooltip("Extra Y rotation (deg) for the shelter — 180 flips it to face the road the right way.")]
    public float busStopYawOffset = 180f;
    [Tooltip("Uniform scale for the shelter model (bigger = a more prominent stop).")]
    public float busStopScale = 1.8f;

    [Header("Waiting crowd")]
    [Tooltip("Initial crowd seeded when a stop appears. Keep LOW if FootpathPedestrians (L5) is feeding it — " +
             "the crowd then visibly grows as walkers arrive. Raise toward crowdMax if you want full stops " +
             "without walkers.")]
    public int crowdMin = 14;
    public int crowdMax = 26;
    public float crowdSpread = 6f;
    public float curbDepth = 1.2f;

    [Header("Fares")]
    public int baseFare = 20;
    public int fareVariance = 15;

    [Header("Crowd-up & boarding")]
    public float gatherRange = 45f;
    public float boardRange = 22f;
    [Tooltip("How many of the waiting crowd board AUTOMATICALLY when the bus pulls up. The REST stay waiting — " +
             "Conductor 1 must shout (mic) or manually grab them to get more on. ~10 keeps the bus from filling " +
             "itself for free.")]
    public int autoBoardCount = 10;

    [Header("Pool (auto-created if missing)")]
    public int ensurePoolSize = 250;

    static readonly Color[] Palette =
    {
        new Color(0.85f,0.3f,0.3f),  new Color(0.3f,0.5f,0.85f),  new Color(0.9f,0.75f,0.3f),
        new Color(0.35f,0.7f,0.45f), new Color(0.7f,0.45f,0.8f),  new Color(0.9f,0.6f,0.3f),
        new Color(0.85f,0.85f,0.85f),new Color(0.45f,0.7f,0.72f),
    };

    class Stop
    {
        public GameObject box;
        public Vector3 stopPos, curbBase;
        public readonly List<Passenger> crowd = new List<Passenger>();
        public bool gathered, boardingDone;
    }

    TiledRoadStreamer _tiled;
    RoadZone _zone;
    readonly List<Stop> _live = new List<Stop>();
    readonly Stack<Stop> _freeStops = new Stack<Stop>();
    int _sectionsSinceStop;
    int _colorRot;

    void Awake()
    {
        Instance = this;
        _tiled = GetComponent<TiledRoadStreamer>();
        EnsurePool();
    }

    void OnEnable()
    {
        if (_tiled == null) _tiled = GetComponent<TiledRoadStreamer>();
        if (_tiled != null) _tiled.OnForwardAdvanced += OnSection;
    }
    void OnDisable()
    {
        if (_tiled != null) _tiled.OnForwardAdvanced -= OnSection;
    }

    RoadZone RoadZoneRef => _tiled != null ? _tiled.Zone : null;
    bool LeadFrame(out Vector3 p, out Vector3 f, out Vector3 r)
    {
        if (_tiled != null) return _tiled.TryGetLeadFrame(out p, out f, out r);
        p = default; f = Vector3.forward; r = Vector3.right; return false;
    }

    void EnsurePool()
    {
        if (PassengerPool.Instance != null) return;
        // Create inactive, set the size, THEN activate — so PassengerPool.Awake builds with our size,
        // not the default. (Awake runs on activation, not on AddComponent, for an inactive object.)
        GameObject go = new GameObject("PassengerPool (auto)");
        go.SetActive(false);
        PassengerPool pool = go.AddComponent<PassengerPool>();
        pool.poolSize = ensurePoolSize;
        go.SetActive(true);
        SceneHierarchy.Parent(go, SceneHierarchy.Category.World);
    }

    void OnSection(Vector3 leadKnotWorld)
    {
        _zone = RoadZoneRef;
        if (_zone == null || PassengerPool.Instance == null) return;
        if (_live.Count >= maxLiveStops) return;

        if (++_sectionsSinceStop < Random.Range(sectionsPerStop, sectionsPerStop + 2)) return;

        if (!LeadFrame(out Vector3 pos, out Vector3 fwd, out Vector3 right)) return;
        // DON'T place a stop on a curve (the shelter ends up skewed / over the road, and gathering looks wrong).
        // Only commit on a roughly-straight stretch; otherwise wait for the next section (keeps the counter so we
        // don't pile up — it resets only on a successful placement).
        if (!IsStraightHere()) return;
        _sectionsSinceStop = 0;
        PlaceStop(pos, fwd, right);
    }

    // Is the road roughly straight at the lead frame? Compares the forward direction a little ahead vs behind the
    // lead; a big heading change = a curve, so we skip placing a stop there.
    bool IsStraightHere()
    {
        if (_tiled == null) return true;
        float m = _tiled.MetresAhead;
        if (!_tiled.SampleRoad(m - 12f, 0f, out _, out Vector3 f0, out _)) return true;
        if (!_tiled.SampleRoad(m - 28f, 0f, out _, out Vector3 f1, out _)) return true;
        return Vector3.Angle(f0, f1) < 7f;   // < ~7° over ~16 m = straight enough
    }

    void PlaceStop(Vector3 framePos, Vector3 fwd, Vector3 right)
    {
        // LEFT footpath under LHT is on -X (right points +X), between the lane edge and the kerb.
        Vector3 left = -right;
        // SHELTER sits HALF ON THE FOOTPATH, HALF ON THE GROUND — pushed out to the footpath/ground boundary
        // (RoadHalf is the footpath outer edge) so it's off the walking path, not planted in the middle of it.
        float shelterLateral = _zone.RoadHalf;                       // the kerb/ground seam
        Vector3 stopPos = framePos + left * shelterLateral;
        // PASSENGERS gather on the FOOTPATH (between the lane edge and the kerb), nearer the road than the shelter.
        Vector3 curbBase = framePos + left * (_zone.DriveHalf + curbDepth);
        float groundY = framePos.y;
        stopPos.y = groundY; curbBase.y = groundY;

        Stop st = _freeStops.Count > 0 ? _freeStops.Pop() : new Stop();
        st.gathered = false; st.boardingDone = false;
        st.crowd.Clear();
        st.stopPos = stopPos; st.curbBase = curbBase;

        EnsureBox(st);
        st.box.SetActive(true);
        // a real shelter model is base-pivoted → sit on the ground; the cube fallback is centre-pivoted → lift it.
        float lift = _stopIsCube ? 0.75f : 0f;
        // the shelter faces +Z; the model was authored facing the WRONG way down the road, so rotate it 180°
        // (cube fallback keeps the plain facing). Real shelter is also scaled UP to read as a proper stop.
        Quaternion yaw = Quaternion.LookRotation(fwd, Vector3.up);
        if (!_stopIsCube)
        {
            yaw *= Quaternion.Euler(0f, busStopYawOffset, 0f);
            st.box.transform.localScale = Vector3.one * busStopScale;
        }
        st.box.transform.SetPositionAndRotation(stopPos + Vector3.up * lift, yaw);

        // crowd waits ON THE FOOTPATH (between the kerb and the shelter), NOT out on the ground where the shelter is.
        Vector3 footCenter = framePos + (-right) * ((_zone.DriveHalf + _zone.RoadHalf) * 0.5f);
        footCenter.y = groundY;
        int want = Random.Range(crowdMin, crowdMax + 1);
        for (int i = 0; i < want; i++)
        {
            Passenger p = PassengerPool.Instance.Take();
            if (p == null) break;                       // pool drained — fine, fewer people
            Vector2 o = Random.insideUnitCircle * crowdSpread;
            Vector3 wp = footCenter + right * (o.x * 0.35f) + fwd * o.y;   // hug the footpath (thin across its width)
            wp.y = groundY;
            p.transform.SetParent(transform, true);     // ride the road
            p.transform.position = wp;
            p.ResetWaiting(Random.Range(baseFare, baseFare + fareVariance + 1), Palette[_colorRot++ % Palette.Length]);
            st.crowd.Add(p);
        }

        _live.Add(st);
    }

    static GameObject _stopPrefab;        // Resources/Vehicles/busstop (loaded once)
    static bool _stopPrefabTried;
    bool _stopIsCube;                     // current shelters are the cube fallback (no model available)

    void EnsureBox(Stop st)
    {
        if (st.box != null) return;

        if (!_stopPrefabTried) { _stopPrefab = Resources.Load<GameObject>("Vehicles/Props/busstop"); _stopPrefabTried = true; }

        if (_stopPrefab != null)
        {
            st.box = Instantiate(_stopPrefab, transform);
            st.box.name = "SplineBusStop";
            foreach (var col in st.box.GetComponentsInChildren<Collider>(true)) Destroy(col);  // visual only
            _stopIsCube = false;
            return;
        }

        // fallback: the old orange cube (game still works with no model)
        st.box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        st.box.name = "SplineBusStop";
        Collider c = st.box.GetComponent<Collider>();
        if (c != null) Destroy(c);
        st.box.transform.SetParent(transform, false);
        st.box.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
        Renderer br = st.box.GetComponent<Renderer>();
        if (br != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                Material m = new Material(sh);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.95f, 0.5f, 0.1f));
                br.material = m;
            }
        }
        _stopIsCube = true;
    }

    void Update()
    {
        BusPassengers bus = BusPassengers.Instance;
        if (bus == null) return;
        Vector3 busPos = bus.transform.position;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Stop st = _live[i];
            float dSqr = (busPos - st.stopPos).sqrMagnitude;

            // Phase 1 — crowd up at the curb as the bus approaches. Only the FIRST `autoBoardCount` of the crowd
            // gather to board automatically; the rest stay Waiting (Conductor 1 shouts/grabs them manually).
            if (!st.gathered && dSqr <= gatherRange * gatherRange)
            {
                st.gathered = true;
                int g = 0;
                for (int c = 0; c < st.crowd.Count && g < autoBoardCount; c++)
                    if (st.crowd[c] != null && st.crowd[c].state == Passenger.State.Waiting)
                        st.crowd[c].BeginGather(CurbPoint(st, g++));
            }

            // Phase 2 — bus pulled up slow & close: the gathered (auto-board) crowd boards, AND any rider who rang
            // the bell (wants off) alights here. The non-gathered waiters stay for the conductor to call/grab.
            if (st.gathered && !st.boardingDone && bus.CanBoard && dSqr <= boardRange * boardRange)
            {
                st.boardingDone = true;
                for (int c = 0; c < st.crowd.Count; c++)
                    if (st.crowd[c] != null && st.crowd[c].state == Passenger.State.Gathering)
                        st.crowd[c].BeginBoarding(bus);
                bus.ReleaseAlightersAtStop();   // people who want off step off here
            }

            // Recycle once the bus is well past this stop.
            if (Vector3.Distance(busPos, st.stopPos) > recycleDistance && IsBehind(busPos, st.stopPos))
                Recycle(i);
        }
    }

    // Is the stop behind the bus's travel? Cheap heuristic: project onto the bus forward.
    bool IsBehind(Vector3 busPos, Vector3 stopPos)
    {
        Transform b = BusPassengers.Instance.transform;
        return Vector3.Dot(stopPos - busPos, b.forward) < 0f;
    }

    Vector3 CurbPoint(Stop st, int g)
    {
        // a loose line along the kerb in front of the stop
        Vector3 along = (st.box != null) ? st.box.transform.forward : Vector3.forward;
        return st.curbBase + along * ((g - 3f) * 1.3f);
    }

    void Recycle(int index)
    {
        Stop st = _live[index];
        for (int c = 0; c < st.crowd.Count; c++)
        {
            Passenger p = st.crowd[c];
            if (p == null) continue;
            // only reclaim ones that never boarded; aboard passengers belong to the bus now
            if ((p.state == Passenger.State.Waiting || p.state == Passenger.State.Gathering)
                && PassengerPool.Instance != null)
                PassengerPool.Instance.Return(p);
        }
        st.crowd.Clear();
        if (st.box != null) st.box.SetActive(false);
        _live.RemoveAt(index);
        _freeStops.Push(st);
    }

    // ---- Pedestrian API (L5): walking pedestrians convert into a stop's waiting crowd ----

    [Header("Walkers (L5)")]
    [Tooltip("A walking pedestrian joins a stop when within this distance (m) of it.")]
    public float joinRange = 7f;
    [Tooltip("Cap on a stop's crowd (seed + joined walkers) so it doesn't balloon forever. Keep 20–30.")]
    public int crowdCap = 30;

    /// A footpath walker reached the kerb: if a live stop is within joinRange, add it to that stop's waiting
    /// crowd (re-parented to ride the road) and return true. The walker keeps the fare it was carrying.
    public bool TryJoinNearestStop(Passenger walker, Vector3 worldPos)
    {
        if (walker == null) return false;
        Stop best = null; float bestD2 = joinRange * joinRange;
        for (int i = 0; i < _live.Count; i++)
        {
            Stop st = _live[i];
            if (st.boardingDone || st.crowd.Count >= crowdCap) continue;     // closed or full
            float d2 = (st.stopPos - worldPos).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = st; }
        }
        if (best == null) return false;

        Vector3 wp = worldPos; wp.y = best.stopPos.y;
        walker.transform.SetParent(transform, true);     // ride the road like the rest of the crowd
        walker.transform.position = wp;
        walker.ConvertToWaiting(Palette[_colorRot++ % Palette.Length]);
        best.crowd.Add(walker);
        // if the bus is already crowding-up this stop, send the new arrival to the curb too
        if (best.gathered) walker.BeginGather(CurbPoint(best, best.crowd.Count));
        return true;
    }

    // ---- Rival API (L4): rivals find stops ahead and steal waiting passengers ----

    /// World position of the nearest stop AHEAD of `from` (along `fwd`) that still has waiting passengers,
    /// plus how many wait there. Returns false if none. Rivals use this to pick a stop to camp.
    public bool TryGetStopAhead(Vector3 from, Vector3 fwd, float maxDist, out Vector3 stopPos, out int waiting)
    {
        stopPos = default; waiting = 0;
        float best = maxDist * maxDist; bool found = false;
        for (int i = 0; i < _live.Count; i++)
        {
            Stop st = _live[i];
            Vector3 to = st.stopPos - from;
            if (Vector3.Dot(to, fwd) <= 0f) continue;             // must be ahead
            int w = CountWaiting(st);
            if (w <= 0) continue;
            float d2 = to.sqrMagnitude;
            if (d2 < best) { best = d2; stopPos = st.stopPos; waiting = w; found = true; }
        }
        return found;
    }

    /// A rival pulled up at `nearStopPos` grabs up to `maxCount` waiting passengers: removes them from the
    /// crowd, returns them to the pool (they "boarded the rival"), and returns the total fare stolen. The
    /// player can't get those fares anymore — that's the competition.
    public int ClaimWaitingPassengers(Vector3 nearStopPos, int maxCount)
    {
        Stop st = FindStopNear(nearStopPos);
        if (st == null) return 0;
        int taken = 0, fare = 0;
        for (int c = 0; c < st.crowd.Count && taken < maxCount; c++)
        {
            Passenger p = st.crowd[c];
            if (p == null) continue;
            if (p.state != Passenger.State.Waiting && p.state != Passenger.State.Gathering) continue;
            fare += p.Fare;
            if (PassengerPool.Instance != null) PassengerPool.Instance.Return(p);
            st.crowd[c] = null;
            taken++;
        }
        st.crowd.RemoveAll(x => x == null);
        return fare;
    }

    int CountWaiting(Stop st)
    {
        int n = 0;
        for (int c = 0; c < st.crowd.Count; c++)
        {
            Passenger p = st.crowd[c];
            if (p != null && (p.state == Passenger.State.Waiting || p.state == Passenger.State.Gathering)) n++;
        }
        return n;
    }

    Stop FindStopNear(Vector3 pos)
    {
        float best = 64f; Stop found = null;   // within 8m
        for (int i = 0; i < _live.Count; i++)
        {
            float d2 = (_live[i].stopPos - pos).sqrMagnitude;
            if (d2 < best) { best = d2; found = _live[i]; }
        }
        return found;
    }

    /// Is there a live bus stop within `range` metres of `pos`? Used by the auto-conductor to decide it's
    /// safe to hop off (he only leaves the bus at a stop, not randomly mid-road).
    public bool IsNearStop(Vector3 pos, float range)
    {
        float r2 = range * range;
        for (int i = 0; i < _live.Count; i++)
            if ((_live[i].stopPos - pos).sqrMagnitude <= r2) return true;
        return false;
    }

    /// Called by FloatingOrigin after a recenter. Stop boxes + waiting passengers are children of the
    /// road and move with the scene shift automatically, but our cached world positions (used for the
    /// distance checks) must be shifted to match.
    public void OnOriginShifted(Vector3 delta)
    {
        for (int i = 0; i < _live.Count; i++)
        {
            _live[i].stopPos += delta;
            _live[i].curbBase += delta;
        }
    }

    void OnDrawGizmosSelected()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            Stop st = _live[i];
            Gizmos.color = new Color(0.95f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(st.stopPos, 1f);
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.25f);    // gather range
            Gizmos.DrawWireSphere(st.stopPos, gatherRange);
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);    // board range
            Gizmos.DrawWireSphere(st.stopPos, boardRange);
        }
    }
}
