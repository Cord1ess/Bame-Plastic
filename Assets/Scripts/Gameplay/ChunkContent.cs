using System.Collections.Generic;
using UnityEngine;

/// Hosts a chunk's BUS STOP so it rides the treadmill pool. The generator marks every ~3-4 chunks as a
/// stop. A stop places an indicator on the LEFT of the road and borrows waiting passengers from the
/// global PassengerPool, arranged in clumps + scattered randoms. Two-phase pickup:
///   1) bus within gatherRange  -> ~half the crowd walks to the curb ("crowds up" early),
///   2) bus within boardRange + slow -> the curb crowd walks to the door and boards 1-by-1.
/// Un-boarded passengers are returned to the pool when the stop resets; nothing is Instantiated here.
public class ChunkContent : MonoBehaviour
{
    [Header("Stop placement")]
    [Tooltip("How far LEFT of the road the stop + waiting crowd sit (we drive on the left).")]
    public float stopSideOffset = 6f;
    [Tooltip("Distance LEFT of road centre that the crowd gathers at the curb (closer than the stop).")]
    public float curbOffset = 2.5f;

    [Header("Waiting crowd (clumps + randoms)")]
    public int maxWaiting = 30;
    public int clumps = 3;
    public int clumpSize = 7;
    public float clumpDistanceMin = 5f;
    public float clumpDistanceMax = 16f;
    public float clumpRadius = 3f;
    public int randoms = 9;
    public float randomSpread = 18f;

    [Header("Fares")]
    public int baseFare = 20;
    public int fareVariance = 15;

    [Header("Crowd-up & boarding")]
    [Tooltip("Bus this close -> selected passengers move to the curb and wait.")]
    public float gatherRange = 45f;
    [Tooltip("Bus this close AND slow -> the gathered crowd walks to the door and boards.")]
    public float boardRange = 22f;
    [Range(0f, 1f)] public float boardFraction = 0.5f;

    static readonly Color[] Palette =
    {
        new Color(0.85f,0.3f,0.3f),  new Color(0.3f,0.5f,0.85f),  new Color(0.9f,0.75f,0.3f),
        new Color(0.35f,0.7f,0.45f), new Color(0.7f,0.45f,0.8f),  new Color(0.9f,0.6f,0.3f),
        new Color(0.85f,0.85f,0.85f),new Color(0.45f,0.7f,0.72f),
    };

    GameObject _box;
    readonly List<Passenger> _borrowed = new List<Passenger>();
    bool _isStop, _gathered, _boardingDone;
    Vector3 _stopPos, _curbBase;

    // Called by LevelLayoutGenerator when the chunk goes live.
    public void OnActivated(bool isStop)
    {
        ReturnAllWaiting();                       // give back un-boarded passengers from a previous life
        _isStop = isStop;
        _gathered = false;
        _boardingDone = false;
        if (isStop) ActivateStop();
        else if (_box != null) _box.SetActive(false);
    }

    void EnsureBox()
    {
        if (_box != null) return;
        _box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _box.name = "BusStopBox";
        Collider col = _box.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _box.transform.SetParent(transform, false);
        Renderer br = _box.GetComponent<Renderer>();
        if (br != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                Material m = new Material(sh);
                m.SetColor("_BaseColor", new Color(0.95f, 0.5f, 0.1f));
                br.material = m;
            }
        }
    }

    void ActivateStop()
    {
        EnsureBox();
        Vector3 anchor = FindAnchor().position;
        float groundY = GroundTopY(anchor.y);

        // Chunks spawn unrotated and the straight road runs +Z, so "left" is -X.
        _stopPos = new Vector3(anchor.x - stopSideOffset, groundY, anchor.z);
        _curbBase = new Vector3(anchor.x - curbOffset, groundY, anchor.z);

        _box.SetActive(true);
        _box.transform.position = _stopPos + Vector3.up * 0.75f;
        SetWorldScale(_box.transform, new Vector3(1.2f, 1.5f, 1.2f));

        SpawnWaiting(groundY);
    }

    void SpawnWaiting(float groundY)
    {
        if (PassengerPool.Instance == null) return;
        int placed = 0;
        for (int c = 0; c < clumps && placed < maxWaiting; c++)
        {
            float cd = Random.Range(clumpDistanceMin, clumpDistanceMax);
            float ca = Random.Range(0f, Mathf.PI * 2f);
            Vector3 center = _stopPos + new Vector3(Mathf.Cos(ca) * cd, 0f, Mathf.Sin(ca) * cd);
            for (int k = 0; k < clumpSize && placed < maxWaiting; k++)
            {
                Vector2 o = Random.insideUnitCircle * clumpRadius;
                if (TakePlace(new Vector3(center.x + o.x, groundY, center.z + o.y), placed)) placed++;
            }
        }
        for (int n = 0; n < randoms && placed < maxWaiting; n++)
        {
            Vector2 o = Random.insideUnitCircle * randomSpread;
            if (TakePlace(new Vector3(_stopPos.x + o.x, groundY, _stopPos.z + o.y), placed)) placed++;
        }
    }

    bool TakePlace(Vector3 worldPos, int colorIdx)
    {
        Passenger p = PassengerPool.Instance.Take();
        if (p == null) return false;                  // pool exhausted — fine, just fewer people
        p.transform.SetParent(transform, true);       // ride this chunk while waiting
        p.transform.position = worldPos;
        p.ResetWaiting(Random.Range(baseFare, baseFare + fareVariance + 1), Palette[colorIdx % Palette.Length]);
        _borrowed.Add(p);
        return true;
    }

    void ReturnAllWaiting()
    {
        for (int i = 0; i < _borrowed.Count; i++)
        {
            Passenger p = _borrowed[i];
            if (p == null) continue;
            // Only reclaim ones that never boarded; aboard passengers are owned by the bus now.
            if ((p.state == Passenger.State.Waiting || p.state == Passenger.State.Gathering) && PassengerPool.Instance != null)
                PassengerPool.Instance.Return(p);
        }
        _borrowed.Clear();
    }

    void Update()
    {
        if (!_isStop || _borrowed.Count == 0) return;
        BusPassengers bus = BusPassengers.Instance;
        if (bus == null) return;

        float dSqr = (bus.transform.position - _stopPos).sqrMagnitude;

        // Phase 1 — crowd up at the curb as the bus approaches.
        if (!_gathered && dSqr <= gatherRange * gatherRange)
        {
            _gathered = true;
            int g = 0;
            for (int i = 0; i < _borrowed.Count; i++)
                if (_borrowed[i].state == Passenger.State.Waiting && Random.value < boardFraction)
                    _borrowed[i].BeginGather(CurbPoint(g++));
        }

        // Phase 2 — bus pulled up slow & close: the curb crowd boards.
        if (_gathered && !_boardingDone && bus.CanBoard && dSqr <= boardRange * boardRange)
        {
            _boardingDone = true;
            for (int i = 0; i < _borrowed.Count; i++)
                if (_borrowed[i].state == Passenger.State.Gathering)
                    _borrowed[i].BeginBoarding(bus);
        }
    }

    // A spot along the curb (a loose line in front of the stop, with a little depth jitter).
    Vector3 CurbPoint(int g)
    {
        return _curbBase + new Vector3(Random.Range(-0.8f, 0.8f), 0f, (g - 3f) * 1.3f);
    }

    Transform FindAnchor()
    {
        TriggerExit te = GetComponentInChildren<TriggerExit>(true);
        return te != null ? te.transform : transform;
    }

    // Top Y of the chunk's biggest flat mesh (the road/ground) so passengers stand on the ground.
    float GroundTopY(float fallback)
    {
        Renderer best = null;
        float bestArea = 0f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer)) continue;
            if (_box != null && r.gameObject == _box) continue;
            Vector3 sz = r.bounds.size;
            float area = sz.x * sz.z;
            if (area > bestArea) { bestArea = area; best = r; }
        }
        return best != null ? best.bounds.max.y : fallback;
    }

    void SetWorldScale(Transform t, Vector3 worldScale)
    {
        Vector3 p = transform.lossyScale;
        t.localScale = new Vector3(worldScale.x / NonZero(p.x), worldScale.y / NonZero(p.y), worldScale.z / NonZero(p.z));
    }

    static float NonZero(float v) => Mathf.Abs(v) < 1e-6f ? 1f : v;
}
