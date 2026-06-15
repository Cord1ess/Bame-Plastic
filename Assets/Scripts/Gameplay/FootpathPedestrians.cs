using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// L5 — pedestrians strolling the footpath who CONVERT into waiting fares at bus stops. Makes the street
/// feel alive (people walking, not static clumps) and feeds the competitive loop: a stop's crowd GROWS over
/// time as walkers arrive, so timing matters — hit it early for a thin crowd, or let it build (and risk a
/// rival camping it). Walkers are pooled Passengers in the new `Walking` state, ridden road-relative on the
/// footpath (floating-origin-safe like traffic), and handed to SplineStopSpawner.TryJoinNearestStop on
/// arrival. Borrows from the same PassengerPool the stops use.
[RequireComponent(typeof(TiledRoadStreamer))]
public class FootpathPedestrians : MonoBehaviour
{
    public static FootpathPedestrians Instance { get; private set; }
    [Header("Population")]
    [Tooltip("How many pedestrians stroll EACH footpath at once (both sides → ~2x this total).")]
    public int maxWalkersPerSide = 38;
    [Tooltip("Spawn no closer than this far ahead of the bus (m). Keep BEYOND the visible-ahead range so " +
             "walkers are already there when they come into view — never seen popping in.")]
    public float spawnMinAhead = 75f;
    [Tooltip("Spawn no farther than this ahead (m).")]
    public float spawnMaxAhead = 140f;
    [Tooltip("Recycle a walker once it's this far BEHIND the bus (m) — just off-camera, so it vanishes unseen.")]
    public float cullBehind = 14f;

    [Header("Walk")]
    public Vector2 walkSpeed = new Vector2(1.0f, 1.8f);    // m/s, leisurely
    [Tooltip("Chance a walker heads the same way as the bus (vs strolling toward it).")]
    [Range(0f, 1f)] public float sameWayChance = 0.5f;
    [Tooltip("Random sideways wander across the footpath width (m).")]
    public float lateralWander = 0.8f;

    [Header("Fares carried")]
    public int baseFare = 20;
    public int fareVariance = 15;

    [Header("Conversion to fares")]
    [Tooltip("Base chance a footpath walker is even WILLING to become a passenger (decided once at spawn). Most " +
             "people on the street aren't waiting for YOUR bus — ~0.2 means roughly 2 in 10 will ever board/join.")]
    [Range(0f, 1f)] public float convertChance = 0.2f;
    [Tooltip("Extra conversion chance added at FULL mic shout (Conductor 1 calling out). At 0.2 base + 0.3 this " +
             "tops out ~0.5 (≈5 in 10) when shouting hard.")]
    [Range(0f, 1f)] public float convertChanceShoutBonus = 0.3f;

    [Header("Board off the street")]
    [Tooltip("When the bus is stopped/slow, a player-side walker within this distance (m) of the door boards " +
             "directly — pick people up anywhere, not just at stops.")]
    public float boardWhenStoppedRange = 6f;

    class Walker
    {
        public Passenger p;
        public float metres;       // road-relative, signed (+ ahead of bus)
        public float lateral;      // footpath lateral (m right of centre)
        public float speed;
        public int dir;            // +1 same way as bus, -1 toward the bus
        public int side;           // +1 = player/forward footpath (joins stops), -1 = far footpath (ambience)
        public float homeLateral;  // the footpath lateral they DRIFT back to when no bus/car is near
        public bool crossing;      // mid road-crossing to the other footpath
        public float crossTargetLat;   // the far footpath lateral we're crossing toward
        public float crossCooldown;    // seconds until this walker may consider crossing again
        public bool willConvert;   // decided at spawn: does THIS walker ever become a fare? (most don't)
    }

    [Header("Avoidance + crossing")]
    [Tooltip("Peds steer away when the bus/a car comes within this distance (m). They can NEVER be overlapped — " +
             "a hard push-out keeps them out of the bus/car footprint.")]
    public float avoidRadius = 1.5f;
    [Tooltip("How hard the bus/car push moves the walker out (higher = shoved away faster). The push moves their " +
             "ACTUAL position — it does NOT spring back.")]
    public float dodgeLerp = 0.2f;
    [Tooltip("How fast a walker drifts back toward their footpath once nothing is near (gentle return, no snap).")]
    public float returnSpeed = 0.5f;
    [Tooltip("Crossing speed (m/s laterally) — a brisk walk across the road.")]
    public float crossSpeed = 2.4f;
    [Tooltip("Min seconds between a walker's crossings.")]
    public float crossCooldownSec = 20f;

    [Header("Designated crossing points")]
    [Tooltip("People only cross at SPECIFIC points (like a zebra crossing), not anywhere. Metres of road between " +
             "consecutive crossing points — bigger = rarer crossings. ~220 ≈ one every couple of stops.")]
    public float crossingPointSpacing = 220f;
    [Tooltip("How close (m along the road) a walker must be to a crossing point to start crossing there.")]
    public float crossingPointRadius = 9f;
    [Tooltip("Per-second chance a walker AT a crossing point decides to cross (so 3–4 gather + cross, not all).")]
    [Range(0f, 1f)] public float crossAtPointChance = 0.5f;
    [Tooltip("Max walkers that may be mid-cross at one crossing point at once (keeps it to a small group).")]
    public int maxCrossersPerPoint = 4;

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<Walker> _live = new List<Walker>();
    readonly Stack<Walker> _free = new Stack<Walker>();
    // designated crossing points, tracked road-relative (metres ahead of bus). Streamed like everything else.
    readonly List<float> _crossPoints = new List<float>();
    float _nextCrossPointAt;     // the road-distance cursor for placing the next crossing point
    bool _ready;
    bool _seeded;   // did the one-time full-span initial fill (incl. beside the bus)

    void Awake() { Instance = this; _road = GetComponent<TiledRoadStreamer>(); _zone = GetComponent<RoadZone>(); }

    /// Adopt a passenger that just ALIGHTED from the bus as a walking pedestrian on the player-side footpath,
    /// at the bus's current position, strolling away. Reuses the same GameObject (no pool pop) so there's no
    /// flicker. Returns false if we can't take it (no room / not ready).
    public bool AdoptAsWalker(Passenger p)
    {
        if (!_ready || p == null) return false;
        int side = +1;                                   // player-side footpath (where the door is)
        if (CountSide(side) >= maxWalkersPerSide + 4) return false;   // a little headroom over the spawn cap

        Walker w = _free.Count > 0 ? _free.Pop() : new Walker();
        w.p = p;
        w.side = side;
        // place at the bus's current road position, on the footpath
        float metresAtBus = 1.5f;                        // just ahead of the door
        w.metres = metresAtBus;
        w.lateral = FootpathLateral(side) + Random.Range(-lateralWander, lateralWander);
        w.homeLateral = w.lateral;
        w.dir = +1;                                      // walk along with traffic, away from the bus
        w.speed = Random.Range(walkSpeed.x, walkSpeed.y);

        p.transform.SetParent(_parent, true);
        p.BeginWalking(0, Color.grey);                   // a plain pedestrian again (already paid/rode)
        if (_road.SampleRoad(w.metres, w.lateral, out Vector3 pos, out _, out _)) p.transform.position = pos;
        _live.Add(w);
        return true;
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("Pedestrians"); go.transform.SetParent(transform, false); _parent = go.transform;
        _nextCrossPointAt = crossingPointSpacing * 0.5f;   // first crossing point a little ahead, not at the bus
        _ready = true;
    }

    // footpath lateral for a given SIDE: +1 = player/forward footpath, -1 = the far footpath across the road.
    // Centred in the footpath band, outside the drive lanes.
    float FootpathLateral(int side)
    {
        if (_zone == null) return -10f * side;
        float centre = (_zone.DriveHalf + _zone.RoadHalf) * 0.5f;
        float forwardSign = _zone.leftHandTraffic ? -1f : 1f;  // player/forward footpath is -X under LHT
        return centre * forwardSign * side;                     // side -1 flips to the opposite footpath
    }

    void Update()
    {
        if (!_ready || PassengerPool.Instance == null) return;
        BusController bus = BusController.Instance;
        float busSpeed = bus != null ? bus.SpeedMps : 0f;
        float dt = Time.deltaTime;
        float ahead = _road.MetresAhead - 5f;

        UpdateCrossingPoints(busSpeed, dt, ahead);

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Walker w = _live[i];
            if (w.p == null || w.p.state != Passenger.State.Walking) { Recycle(i, false); continue; }

            // advance along the footpath relative to the bus, sample the road, place the walker
            w.metres += (w.dir * w.speed - busSpeed) * dt;
            if (w.metres < -cullBehind || w.metres > ahead + 5f) { Recycle(i, true); continue; }

            if (w.crossCooldown > 0f) w.crossCooldown -= dt;

            // CROSSING: ease the walker's base lateral across the road toward the far footpath, but ONLY while the
            // path at the road centre is clear of the bus + cars (else pause mid-stride — never walk into them).
            if (w.crossing)
            {
                if (CrossingClear(w.metres))
                {
                    w.lateral = Mathf.MoveTowards(w.lateral, w.crossTargetLat, crossSpeed * dt);
                    if (Mathf.Approximately(w.lateral, w.crossTargetLat))
                    {
                        w.crossing = false; w.side = -w.side; w.crossCooldown = crossCooldownSec;   // arrived
                        w.homeLateral = w.crossTargetLat;   // their footpath is now the far side
                    }
                }
                // else: blocked — hold position this frame (wait for the gap)
            }
            else if (w.crossCooldown <= 0f && NearCrossingPoint(w.metres) && Random.value < crossAtPointChance * dt
                     && CrossersAt(w.metres) < maxCrossersPerPoint)
            {
                // at a DESIGNATED crossing point → cross to the opposite footpath (a small group, not everyone)
                w.crossTargetLat = FootpathLateral(-w.side);
                w.crossing = true;
            }

            // AVOIDANCE: push the walker's ACTUAL lateral away from the bus/cars (NOT a temporary offset that
            // springs back — that was fighting a continuous push and snapping them through the bus). The push
            // PERMANENTLY moves `w.lateral`; when nothing is near, they DRIFT slowly back toward their footpath.
            float push = AvoidanceOffset(w.metres, w.lateral);
            if (Mathf.Abs(push) > 0.001f)
                w.lateral += push * dodgeLerp * dt;                       // shoved out, and it STAYS
            else if (!w.crossing)
                w.lateral = Mathf.MoveTowards(w.lateral, w.homeLateral, returnSpeed * dt);  // clear → ease home

            if (_road.SampleRoad(w.metres, w.lateral, out Vector3 pos, out Vector3 fwd, out _))
            {
                // HARD PUSH-OUT: if despite steering the walker is still inside the bus/car footprint, shove them
                // clear THIS frame so nothing can ever overlap/run them over. FEED IT BACK into w.lateral so the
                // shove persists (no spring-back) and converts the world push to a lateral the walker keeps.
                Vector3 cleared = HardPushOut(pos);
                if ((cleared - pos).sqrMagnitude > 1e-6f)
                {
                    if (_road.SampleRoad(w.metres, 0f, out Vector3 mid, out _, out Vector3 right))
                    { Vector3 d = cleared - mid; d.y = 0f; w.lateral = Vector3.Dot(d, right); }
                    pos = cleared;
                }
                w.p.transform.position = pos;
                Vector3 face = (w.crossing ? CrossFacing(w) : fwd * w.dir); face.y = 0f;
                if (face.sqrMagnitude > 1e-5f) w.p.transform.rotation = Quaternion.LookRotation(face, Vector3.up);

                // BOARD WHEN STOPPED ANYWHERE: if the bus is pulled up (slow) and this player-side walker is near
                // the door, they hop on directly — but ONLY if they're a willing fare (most aren't; shouting helps).
                BusPassengers bp = BusPassengers.Instance;
                if (w.side > 0 && bp != null && bp.CanBoard && WantsToConvert(w) &&
                    (pos - bp.DoorPosition).sqrMagnitude < boardWhenStoppedRange * boardWhenStoppedRange)
                {
                    w.p.ResetWaiting(baseFare, Color.grey);   // becomes a fare
                    w.p.BeginBoarding(bp);                     // → queues at the door, threads the aisle
                    Recycle(i, false); continue;               // the bus owns it now
                }

                // Only PLAYER-SIDE, WILLING walkers join stops as fares — the rest just stroll past (ambience).
                if (w.side > 0 && WantsToConvert(w) && SplineStopSpawner.Instance != null &&
                    SplineStopSpawner.Instance.TryJoinNearestStop(w.p, pos))
                { Recycle(i, false); continue; }   // ownership passed to the stop; don't pool it
            }
        }

        // ONE-TIME SEED: once the road is ready, fill the WHOLE visible footpath span — incl. BESIDE and just
        // behind the bus (the bus sits mid-street, so its left/right must already be peopled at start, not empty).
        // Ongoing top-ups below stay ahead-only (so new arrivals aren't seen popping in).
        if (!_seeded && ahead > spawnMinAhead)
        {
            int total0 = maxWalkersPerSide * 2, guard0 = 0;
            while (_live.Count < total0 && guard0++ < total0 * 2)
            {
                int side = CountSide(+1) <= CountSide(-1) ? +1 : -1;
                if (!Spawn(side, -cullBehind + 2f, Mathf.Min(spawnMaxAhead, ahead))) break;
            }
            _seeded = true;
        }

        // top up BOTH footpaths to maxWalkersPerSide each (ahead-only, so they're never seen appearing)
        int total = maxWalkersPerSide * 2;
        int guard = 0;
        while (_live.Count < total && guard++ < total)
        {
            // spawn on whichever side is currently shorter, so both stay populated
            int near = CountSide(+1), far = CountSide(-1);
            int side = near <= far ? +1 : -1;
            if (!Spawn(ahead, side)) break;
        }
    }

    int CountSide(int side)
    {
        int n = 0;
        for (int i = 0; i < _live.Count; i++) if (_live[i].side == side) n++;
        return n;
    }

    // ---- designated crossing points (zebra-style): people only cross the road AT these, not anywhere ----
    void UpdateCrossingPoints(float busSpeed, float dt, float ahead)
    {
        // points are static in the world → recede relative to the moving bus; cull ones well behind.
        for (int i = _crossPoints.Count - 1; i >= 0; i--)
        {
            _crossPoints[i] -= busSpeed * dt;
            if (_crossPoints[i] < -cullBehind - 10f) _crossPoints.RemoveAt(i);
        }
        _nextCrossPointAt -= busSpeed * dt;
        // place the next crossing point once its cursor falls within the built-ahead road (spaced out → rare).
        while (_nextCrossPointAt < ahead)
        {
            if (_nextCrossPointAt > -cullBehind) _crossPoints.Add(_nextCrossPointAt);
            _nextCrossPointAt += crossingPointSpacing;
        }
    }

    bool NearCrossingPoint(float metres)
    {
        for (int i = 0; i < _crossPoints.Count; i++)
            if (Mathf.Abs(_crossPoints[i] - metres) <= crossingPointRadius) return true;
        return false;
    }

    // how many walkers are currently crossing near the same point as `metres` (cap the group size).
    int CrossersAt(float metres)
    {
        int n = 0;
        for (int i = 0; i < _live.Count; i++)
            if (_live[i].crossing && Mathf.Abs(_live[i].metres - metres) <= crossingPointRadius * 2f) n++;
        return n;
    }

    // Is this walker willing to become a fare right now? Base willingness is rolled once at spawn (most aren't).
    // Conductor 1 SHOUTING (mic) can upgrade an unwilling walker — a one-time chance scaled by loudness, applied
    // permanently so the conversion doesn't re-roll every frame (which would eventually convert everyone).
    bool WantsToConvert(Walker w)
    {
        if (w.willConvert) return true;
        var mic = MicInput.Instance;
        float loud = mic != null ? mic.Loudness : 0f;
        if (loud < 0.35f) return false;                                  // not shouting → unwilling stays unwilling
        // louder shout → better odds, capped by convertChanceShoutBonus, rolled per-frame-but-rare via dt scaling
        float perSec = convertChanceShoutBonus * Mathf.InverseLerp(0.35f, 1f, loud);
        if (Random.value < perSec * Time.deltaTime * 3f) { w.willConvert = true; return true; }
        return false;
    }

    // ---- AVOIDANCE: peds steer away from the bus + cars; a hard push-out guarantees no overlap ----

    // desired LATERAL dodge (m, signed) at a walker spot: if the bus/a car is near, push toward the nearer kerb.
    float AvoidanceOffset(float metres, float lateral)
    {
        if (!_road.SampleRoad(metres, lateral, out Vector3 here, out _, out Vector3 right)) return 0f;
        Vector3 push = Vector3.zero;

        // the bus
        var bus = BusController.Instance;
        if (bus != null) AccumPush(ref push, here, bus.transform.position, BusAvoidRadius(bus));
        // cars
        var traffic = TrafficSystem.Instance;
        if (traffic != null)
        {
            var live = traffic.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var v = live[i]; if (v == null) continue;
                AccumPush(ref push, here, v.transform.position, avoidRadius + v.HalfWidth);
            }
        }
        if (push.sqrMagnitude < 1e-5f) return 0f;
        // project the world push onto the road's lateral (right) axis → a signed lateral dodge
        push.y = 0f;
        return Vector3.Dot(push, right);
    }

    static void AccumPush(ref Vector3 push, Vector3 here, Vector3 obstacle, float radius)
    {
        Vector3 d = here - obstacle; d.y = 0f;
        float dist = d.magnitude;
        if (dist > radius || dist < 1e-4f) { if (dist < 1e-4f) push += Vector3.right * radius; return; }
        push += d.normalized * (radius - dist);   // stronger the closer they are
    }

    // hard push-out in WORLD space: if a walker ended up inside the bus/car footprint, shove them just outside it.
    Vector3 HardPushOut(Vector3 pos)
    {
        var bus = BusController.Instance;
        if (bus != null) pos = PushOutOf(pos, bus.transform.position, BusAvoidRadius(bus) * 0.8f);
        var traffic = TrafficSystem.Instance;
        if (traffic != null)
        {
            var live = traffic.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var v = live[i]; if (v == null) continue;
                pos = PushOutOf(pos, v.transform.position, (v.HalfWidth + 0.6f));
            }
        }
        return pos;
    }

    static Vector3 PushOutOf(Vector3 pos, Vector3 centre, float minDist)
    {
        Vector3 d = pos - centre; d.y = 0f;
        float dist = d.magnitude;
        if (dist >= minDist) return pos;
        Vector3 dir = dist > 1e-4f ? d.normalized : Vector3.right;
        Vector3 outp = centre + dir * minDist;
        return new Vector3(outp.x, pos.y, outp.z);   // keep the walker's own Y (footpath height)
    }

    float BusAvoidRadius(BusController bus)
    {
        float half = bus.collisionBoxSize != Vector3.zero
            ? Mathf.Max(bus.collisionBoxSize.x, bus.collisionBoxSize.z) * 0.5f : 1.4f;
        return avoidRadius + half;
    }

    // crossing is clear if no bus/car is near the road-centre point at this arc-length (so peds wait for a gap).
    bool CrossingClear(float metres)
    {
        if (!_road.SampleRoad(metres, 0f, out Vector3 mid, out _, out _)) return false;
        var bus = BusController.Instance;
        if (bus != null && (bus.transform.position - mid).sqrMagnitude < 64f) return false;   // bus within 8m
        var traffic = TrafficSystem.Instance;
        if (traffic != null)
        {
            var live = traffic.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var v = live[i]; if (v == null) continue;
                if ((v.transform.position - mid).sqrMagnitude < 36f) return false;             // a car within 6m
            }
        }
        return true;
    }

    Vector3 CrossFacing(Walker w)
    {
        if (!_road.SampleRoad(w.metres, w.lateral, out _, out _, out Vector3 right)) return Vector3.forward;
        return (w.crossTargetLat > w.lateral ? right : -right);   // face the way they're crossing
    }

    bool Spawn(float ahead, int side) => Spawn(side, spawnMinAhead, Mathf.Min(spawnMaxAhead, ahead));

    // spawn one walker at a random metres in [minM, maxM]. Ongoing top-ups use spawnMinAhead.. (so new walkers
    // aren't seen popping in); the one-time INITIAL seed uses the full visible span incl. BESIDE/behind the bus.
    bool Spawn(int side, float minM, float maxM)
    {
        if (maxM <= minM + 2f) return false;
        if (CountSide(side) >= maxWalkersPerSide) return false;
        Passenger p = PassengerPool.Instance.Take();
        if (p == null) return false;                  // pool drained — fine, fewer walkers

        Walker w = _free.Count > 0 ? _free.Pop() : new Walker();
        w.p = p;
        w.side = side;
        w.metres = Random.Range(minM, maxM);
        w.lateral = FootpathLateral(side) + Random.Range(-lateralWander, lateralWander);
        w.homeLateral = w.lateral;
        w.dir = Random.value < sameWayChance ? +1 : -1;
        w.speed = Random.Range(walkSpeed.x, walkSpeed.y);
        w.willConvert = side > 0 && Random.value < convertChance;   // most walkers never become fares (decided once)

        p.transform.SetParent(_parent, true);
        p.BeginWalking(baseFare + Random.Range(0, fareVariance + 1), Color.grey);
        if (_road.SampleRoad(w.metres, w.lateral, out Vector3 pos, out _, out _)) p.transform.position = pos;
        _live.Add(w);
        return true;
    }

    // returnToPool=true → this walker wandered off (recycle it to the pool). false → ownership already left
    // us (joined a stop, or it's no longer Walking), so just drop our Walker record.
    void Recycle(int index, bool returnToPool)
    {
        Walker w = _live[index];
        if (returnToPool && w.p != null && PassengerPool.Instance != null) PassengerPool.Instance.Return(w.p);
        w.p = null;
        _free.Push(w);
        _live.RemoveAt(index);
    }
}
