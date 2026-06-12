using UnityEngine;

/// Bus-attached "driver instinct" guide: a flat, wide strip rooted at the FRONT of the bus (roof height),
/// that smoothly swings OUT to overtake/whizz past vehicles ahead and tucks back in — the Dhaka driver's
/// read of the road. The path is a lateral profile that aims at a clear offset beside the nearest blocker
/// (so you SEE the overtake angle), smoothed for a flowing curve. Built in bus-local→world space, drawn as
/// a procedural flat mesh with a directly-set colour (green=go, amber/red=blocked). Driver-only, local.
public class DriverGuide : MonoBehaviour
{
    [Header("Refs (auto-found)")]
    public BusController bus;
    public TiledRoadStreamer road;
    public TrafficSystem traffic;

    [Header("Shape")]
    [Tooltip("Length of the guide strip (m).")]
    public float length = 22f;
    [Range(6, 80)] public int segments = 40;
    [Tooltip("Height above the road (m) — strip lies level at ~roof height so it's visible from the chase cam.")]
    public float height = 3.0f;
    [Tooltip("Strip width (m) — thin & wide, lying flat.")]
    public float width = 1.0f;
    public float tipNarrow = 0.6f;          // tip width = width * tipNarrow

    [Header("Avoidance / overtake")]
    [Tooltip("How far ahead (m) the path looks for vehicles to swing around.")]
    public float scanRange = 32f;
    [Tooltip("Sideways gap (m) the path keeps from a vehicle's edge as it passes. Smaller = tighter squeeze.")]
    public float clearance = 1.6f;
    [Tooltip("How far BEFORE a vehicle the swing-out begins (m). Bigger = earlier, smoother, more visible angle.")]
    public float leadIn = 14f;
    [Tooltip("Smoothing passes on the path (rounds the swing into a flowing curve).")]
    [Range(0, 10)] public int smoothPasses = 4;
    [Tooltip("How fast the path eases to the new plan. High = reactive/snappy. 0 = instant (no smoothing).")]
    public float responsiveness = 30f;

    [Header("Assist")]
    [Range(0f, 1f)] public float steerAssist = 0.3f;

    [Header("Colours")]
    public Color goColor = new Color(0.2f, 1f, 0.45f, 1f);
    public Color slowColor = new Color(1f, 0.8f, 0.1f, 1f);
    public Color stopColor = new Color(1f, 0.25f, 0.2f, 1f);

    Transform _quadT;
    MeshFilter _mf; MeshRenderer _mr; Mesh _mesh; Material _mat;
    int _n;
    float[] _lat;          // smoothed lateral offset per sample (the path)
    float _advisory;
    bool _init;
    Vector3[] _v; Vector2[] _uv; int[] _tris;

    public float SpeedAdvisory => _advisory;

    void Awake() { EnsureRig(); }

    void Resolve()
    {
        if (bus == null) bus = BusController.Instance != null ? BusController.Instance : FindAnyObjectByType<BusController>();
        if (road == null) road = FindAnyObjectByType<TiledRoadStreamer>();
        if (traffic == null) traffic = FindAnyObjectByType<TrafficSystem>();
    }

    void EnsureRig()
    {
        if (_quadT != null) return;
        var go = new GameObject("GuideStrip");
        go.transform.SetParent(transform, false);
        _quadT = go.transform;
        _mf = go.AddComponent<MeshFilter>();
        _mr = go.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = "GuideStrip" }; _mesh.MarkDynamic();
        _mf.sharedMesh = _mesh;
        // URP Unlit, OPAQUE, double-sided, colour set directly via _BaseColor each frame. Opaque (not
        // transparent) avoids URP's finicky transparency-keyword setup that made it flicker/vanish.
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit"); if (sh == null) sh = Shader.Find("Unlit/Color");
        _mat = new Material(sh);
        if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);                 // double-sided
        _mr.sharedMaterial = _mat;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;
    }

    void EnsureBuffers()
    {
        _n = Mathf.Max(3, segments + 1);
        if (_lat == null || _lat.Length != _n)
        {
            _lat = new float[_n];
            int vc = _n * 2;
            _v = new Vector3[vc]; _uv = new Vector2[vc];
            _tris = new int[(_n - 1) * 12];   // double-sided
            for (int s = 0; s < _n - 1; s++)
            {
                int b = s * 2, t = s * 12;
                _tris[t] = b; _tris[t + 1] = b + 2; _tris[t + 2] = b + 1;
                _tris[t + 3] = b + 1; _tris[t + 4] = b + 2; _tris[t + 5] = b + 3;
                _tris[t + 6] = b; _tris[t + 7] = b + 1; _tris[t + 8] = b + 2;
                _tris[t + 9] = b + 1; _tris[t + 10] = b + 3; _tris[t + 11] = b + 2;
            }
            _init = false;
        }
    }

    void LateUpdate()
    {
        Resolve();
        EnsureRig();
        // player setting can hide the guide line entirely (and skip its steer-assist) without removing the system
        if (bus == null || !SettingsStore.GuideLine) { if (_mr != null) _mr.enabled = false; return; }
        _mr.enabled = true;
        EnsureBuffers();
        PlanPath();
        BuildStrip();
        ApplyAssist();
    }

    void BusFrame(out Vector3 origin, out Vector3 fwd, out Vector3 right)
    {
        fwd = bus.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        right = Vector3.Cross(Vector3.up, fwd).normalized;
        origin = bus.transform.position + fwd * FrontOffset() + Vector3.up * height;
    }

    float FrontOffset()
    {
        var cb = bus.transform.Find("CollisionBox");
        var bc = cb != null ? cb.GetComponent<BoxCollider>() : null;
        return bc != null ? (bc.center.z + bc.size.z * 0.5f) : 4.5f;
    }

    // Plan the lateral offset profile: start at the bus lateral; for each forward vehicle in range, push the
    // path to the clearer side across [vehicle-leadIn .. vehicle+behind] so it SWINGS OUT to overtake and
    // tucks back. Then smooth (flowing curve) and ease toward the plan (reactive but not jittery).
    void PlanPath()
    {
        float stepLen = length / (_n - 1);
        road.SampleBand(true, out float bandMin, out float bandMax);
        float busLat = Mathf.Clamp(road.BusLateral, bandMin, bandMax);
        float halfBus = HalfBusWidth();

        var raw = new float[_n];
        for (int i = 0; i < _n; i++) raw[i] = busLat;

        // the path must keep the WHOLE BUS inside the drive band, so a valid lateral is within these limits
        float minLat = bandMin + halfBus;
        float maxLat = Mathf.Max(minLat, bandMax - halfBus);

        var live = traffic != null ? traffic.Live : null;
        float worst = 0f;
        if (live != null)
        {
            for (int k = 0; k < live.Count; k++)
            {
                TrafficVehicle v = live[k];
                if (v == null || !v.InUse || v.dir <= 0) continue;
                if (v.metresFromBus < -2f || v.metresFromBus > scanRange) continue;

                float leftEdge = v.lateral - v.HalfWidth;
                float rightEdge = v.lateral + v.HalfWidth;
                // room to pass the bus on each side: from the band edge to the vehicle edge, minus what the
                // bus needs (its own width + clearance). Only a side the BUS ACTUALLY FITS is a real option.
                float fitLeft = (leftEdge - clearance - halfBus) - minLat;     // >=0 → bus fits on the left
                float fitRight = maxLat - (rightEdge + clearance + halfBus);    // >=0 → bus fits on the right
                bool leftOK = fitLeft >= 0f, rightOK = fitRight >= 0f;

                if (!leftOK && !rightOK)
                {
                    // gap too small for the bus on either side → DON'T swing into it; flag blocked (slow/stop)
                    worst = Mathf.Max(worst, Mathf.InverseLerp(scanRange, 0f, Mathf.Max(0f, v.metresFromBus)));
                    continue;
                }
                bool passLeft = leftOK && (!rightOK || fitLeft >= fitRight);
                float pass = passLeft ? leftEdge - clearance - halfBus : rightEdge + clearance + halfBus;
                pass = Mathf.Clamp(pass, minLat, maxLat);

                float from = v.metresFromBus - leadIn;
                float to = v.metresFromBus + v.HalfLen + clearance + 2f;
                for (int i = 0; i < _n; i++)
                {
                    float d = i * stepLen;
                    if (d < from || d > to) continue;
                    float w = d <= v.metresFromBus ? Mathf.InverseLerp(from, v.metresFromBus, d) : 1f;
                    float want = Mathf.Lerp(raw[i], pass, w);
                    raw[i] = passLeft ? Mathf.Min(raw[i], want) : Mathf.Max(raw[i], want);
                }
            }
        }

        // keep the path within the band (whole bus inside the drive lanes)
        for (int i = 0; i < _n; i++) raw[i] = Mathf.Clamp(raw[i], minLat, maxLat);
        // pin the ROOT SECTION (first ~15%) to the bus lateral so the near end never wanders — the swing
        // happens further out where you can see/react to it, not at the bus itself.
        int rootHold = Mathf.Max(1, Mathf.RoundToInt(_n * 0.15f));
        for (int i = 0; i < rootHold; i++)
        {
            float t = i / (float)rootHold;          // 0 at bus → 1 at end of hold
            raw[i] = Mathf.Lerp(busLat, raw[i], t * t);
        }
        for (int p = 0; p < smoothPasses; p++)
        {
            for (int i = 1; i < _n - 1; i++) raw[i] = (raw[i - 1] + raw[i] * 2f + raw[i + 1]) * 0.25f;
            raw[0] = busLat;
        }

        // ease toward the plan — but the ROOT is INSTANT (pinned to the bus, never lags/wobbles) and the
        // ease ramps up toward the tip, so the far end reacts hardest while the root stays put.
        float baseA = aimSnapped(responsiveness);
        for (int i = 0; i < _n; i++)
        {
            float u = i / (float)(_n - 1);
            float ai = _init ? Mathf.Clamp01(baseA * (0.4f + 0.6f * u)) : 1f;   // root slower-ish, tip full
            float val = Mathf.Lerp(_lat[i], raw[i], ai);
            _lat[i] = float.IsNaN(val) ? busLat : val;                          // NaN guard (never vanish)
        }
        _lat[0] = busLat;                          // root pinned exactly to the bus, every frame
        _init = true;
        _advisory = worst;
    }

    float aimSnapped(float r) => r <= 0f ? 1f : 1f - Mathf.Exp(-r * Time.deltaTime);

    float HalfBusWidth()
    {
        var cb = bus.transform.Find("CollisionBox");
        var bc = cb != null ? cb.GetComponent<BoxCollider>() : null;
        return bc != null ? bc.size.x * 0.5f : 1.4f;
    }

    // Build a FLAT strip: each sample is a road point at (distance, _lat[i]); two verts offset by ±halfWidth
    // along the road's right. Lies level at roof height. Solid colour set on the material.
    void BuildStrip()
    {
        float stepLen = length / (_n - 1);
        BusFrame(out Vector3 origin, out Vector3 fwd0, out Vector3 right0);
        // guard against a bad frame (NaN/huge) ever collapsing the mesh → the strip vanishing
        if (float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z)) { _mr.enabled = false; return; }
        _mr.enabled = true;
        float busLat = road.BusLateral;
        if (float.IsNaN(busLat)) busLat = 0f;

        for (int i = 0; i < _n; i++)
        {
            float d = i * stepLen;
            // world centre = bus front + along fwd by d, shifted laterally by (_lat[i]-busLat) along right.
            Vector3 centre = origin + fwd0 * d + right0 * (_lat[i] - busLat);

            // local right = derivative of the path (so the strip width follows the curve)
            Vector3 dirRight = right0;
            if (i < _n - 1)
            {
                float dn = (i + 1) * stepLen;
                Vector3 next = origin + fwd0 * dn + right0 * (_lat[Mathf.Min(i + 1, _n - 1)] - busLat);
                Vector3 along = (next - centre); along.y = 0f;
                if (along.sqrMagnitude > 1e-5f) dirRight = Vector3.Cross(Vector3.up, along.normalized);
            }
            float u = i / (float)(_n - 1);
            float hw = Mathf.Lerp(width, width * tipNarrow, u) * 0.5f;
            int idx = i * 2;
            _v[idx] = centre - dirRight * hw;
            _v[idx + 1] = centre + dirRight * hw;
            _uv[idx] = new Vector2(0f, u); _uv[idx + 1] = new Vector2(1f, u);
        }

        _mesh.Clear();
        _mesh.vertices = _v;
        _mesh.uv = _uv;
        _mesh.triangles = _tris;
        _mesh.RecalculateBounds();

        Color c = _advisory < 0.5f ? Color.Lerp(goColor, slowColor, _advisory * 2f)
                                   : Color.Lerp(slowColor, stopColor, (_advisory - 0.5f) * 2f);
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
    }

    void ApplyAssist()
    {
        if (steerAssist <= 0f || bus == null) return;
        int idx = Mathf.Clamp(_n / 4, 1, _n - 1);          // aim a bit ahead on the path
        float err = _lat[idx] - road.BusLateral;
        bus.ApplySteerAssist(Mathf.Clamp(err * 0.5f, -1f, 1f) * steerAssist);
    }

    void OnDestroy()
    {
        if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
        if (_mat != null) { if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat); }
    }
}
