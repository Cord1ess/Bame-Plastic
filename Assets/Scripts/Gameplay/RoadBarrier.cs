using System.Collections.Generic;
using UnityEngine;

/// An invisible physical wall just past the footpath on BOTH sides of the endless road, so the bus can't drive
/// off the edge. Instead of giving every building a collider (expensive, and the bus would catch on uneven
/// fronts), this streams a chain of pooled BoxColliders that ride the road's curve — one cheap, smooth,
/// continuous barrier per side at RoadZone.RoadHalf (the outer footpath edge). Solid (non-trigger) so the
/// bus's sphere Rigidbody bumps off it. Road-relative (SampleRoad) so it follows curves + floating origin;
/// pooled + recycled like the buildings. PLAY-ONLY (no [ExecuteAlways]).
[RequireComponent(typeof(TiledRoadStreamer))]
public class RoadBarrier : MonoBehaviour
{
    [Tooltip("Lateral inset (m) from the footpath edge (RoadZone.RoadHalf). 0 = right at the edge; small + " +
             "pushes the wall slightly onto the footpath so the bus stops before the edge.")]
    public float inset = 0.2f;
    [Tooltip("Wall height (m). Tall enough the bus can't ride over it.")]
    public float height = 4f;
    [Tooltip("Wall thickness (m).")]
    public float thickness = 0.6f;
    [Tooltip("Length (m) of each barrier segment along the road. Shorter = hugs curves tighter.")]
    public float segmentLength = 8f;
    [Tooltip("Build the wall out to this far ahead/behind the bus (m).")]
    public float range = 200f;

    class Seg { public GameObject go; public BoxCollider box; public float metres; public int side; }

    TiledRoadStreamer _road;
    RoadZone _zone;
    Transform _parent;
    readonly List<Seg> _live = new List<Seg>();
    readonly Stack<GameObject> _pool = new Stack<GameObject>();
    readonly float[] _next = { -9999f, -9999f };
    bool _ready;

    void Awake() { _road = GetComponent<TiledRoadStreamer>(); _zone = GetComponent<RoadZone>(); }

    void Start()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("RoadBarrier"); go.transform.SetParent(transform, false); _parent = go.transform;
        _ready = true;
    }

    void Update()
    {
        if (!_ready || _zone == null) return;
        float busSpeed = BusController.Instance != null ? BusController.Instance.SpeedMps : 0f;
        float dt = Time.deltaTime;
        float ahead = Mathf.Min(range, _road.MetresAhead - 2f);
        float back = -Mathf.Min(range, _road.MetresBehind - 2f);

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Seg s = _live[i];
            s.metres -= busSpeed * dt;
            if (s.metres < back - segmentLength) { Recycle(i); continue; }
            Place(s);
        }

        for (int sideIdx = 0; sideIdx < 2; sideIdx++)
        {
            int side = sideIdx == 0 ? -1 : 1;
            if (_next[sideIdx] < -9000f) _next[sideIdx] = back;
            _next[sideIdx] -= busSpeed * dt;
            int guard = 0;
            while (_next[sideIdx] < ahead && guard++ < 64)
            {
                Spawn(side, _next[sideIdx] + segmentLength * 0.5f);
                _next[sideIdx] += segmentLength;
            }
        }
    }

    void Spawn(int side, float metres)
    {
        GameObject go = _pool.Count > 0 ? _pool.Pop() : NewSeg();
        var seg = new Seg { go = go, box = go.GetComponent<BoxCollider>(), metres = metres, side = side };
        go.transform.SetParent(_parent, true);
        go.SetActive(true);
        Place(seg);
        _live.Add(seg);
    }

    GameObject NewSeg()
    {
        var go = new GameObject("BarrierSeg");
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(thickness, height, segmentLength);
        return go;
    }

    void Place(Seg s)
    {
        // sit at the footpath edge (RoadHalf - inset) on this side, centred vertically on the wall height
        float lateral = s.side * (_zone.RoadHalf - inset);
        if (!_road.SampleRoad(s.metres, lateral, out Vector3 pos, out Vector3 fwd, out _)) { s.go.SetActive(false); return; }
        if (!s.go.activeSelf) s.go.SetActive(true);
        s.go.transform.position = pos + Vector3.up * (height * 0.5f);
        s.go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // length runs along the road
    }

    void Recycle(int i)
    {
        Seg s = _live[i];
        s.go.SetActive(false);
        s.go.transform.SetParent(_parent, false);
        _pool.Push(s.go);
        _live.RemoveAt(i);
    }

    public void OnOriginShifted(Vector3 delta) { /* road-relative — no-op */ }
}
