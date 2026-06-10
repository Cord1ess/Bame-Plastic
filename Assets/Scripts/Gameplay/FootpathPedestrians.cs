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
    public int maxWalkersPerSide = 20;
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

    class Walker
    {
        public Passenger p;
        public float metres;       // road-relative, signed (+ ahead of bus)
        public float lateral;      // footpath lateral (m right of centre)
        public float speed;
        public int dir;            // +1 same way as bus, -1 toward the bus
        public int side;           // +1 = player/forward footpath (joins stops), -1 = far footpath (ambience)
    }

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<Walker> _live = new List<Walker>();
    readonly Stack<Walker> _free = new Stack<Walker>();
    bool _ready;

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

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Walker w = _live[i];
            if (w.p == null || w.p.state != Passenger.State.Walking) { Recycle(i, false); continue; }

            // advance along the footpath relative to the bus, sample the road, place the walker
            w.metres += (w.dir * w.speed - busSpeed) * dt;
            if (w.metres < -cullBehind || w.metres > ahead + 5f) { Recycle(i, true); continue; }

            if (_road.SampleRoad(w.metres, w.lateral, out Vector3 pos, out Vector3 fwd, out _))
            {
                w.p.transform.position = pos;
                Vector3 face = fwd * w.dir; face.y = 0f;
                if (face.sqrMagnitude > 1e-5f) w.p.transform.rotation = Quaternion.LookRotation(face, Vector3.up);

                // Only PLAYER-SIDE walkers (side +1) join stops as fares — the far footpath is ambience.
                if (w.side > 0 && SplineStopSpawner.Instance != null &&
                    SplineStopSpawner.Instance.TryJoinNearestStop(w.p, pos))
                { Recycle(i, false); continue; }   // ownership passed to the stop; don't pool it
            }
        }

        // top up BOTH footpaths to maxWalkersPerSide each
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

    bool Spawn(float ahead, int side)
    {
        if (ahead <= spawnMinAhead + 2f) return false;
        if (CountSide(side) >= maxWalkersPerSide) return false;
        Passenger p = PassengerPool.Instance.Take();
        if (p == null) return false;                  // pool drained — fine, fewer walkers

        Walker w = _free.Count > 0 ? _free.Pop() : new Walker();
        w.p = p;
        w.side = side;
        w.metres = Random.Range(spawnMinAhead, Mathf.Min(spawnMaxAhead, ahead));
        w.lateral = FootpathLateral(side) + Random.Range(-lateralWander, lateralWander);
        w.dir = Random.value < sameWayChance ? +1 : -1;
        w.speed = Random.Range(walkSpeed.x, walkSpeed.y);

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
