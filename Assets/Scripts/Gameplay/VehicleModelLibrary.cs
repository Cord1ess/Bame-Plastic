using System.Collections.Generic;
using UnityEngine;

/// Maps each traffic Kind (Rickshaw / Car / Bus) to the real 3D model prefabs the team organised under
/// Resources/Vehicles/<category>/ and hands out visual instances for TrafficVehicle (replacing the cube).
///   Cars/      → Car kind          Buses/ + Trucks/ → Bus kind          Rickshaws/ + Small/ → Rickshaw kind
/// EVERY prefab in a category folder is auto-loaded (no hardcoded names — drop a new prefab in and it's used).
/// Prefabs are raw model scale, so each is AUTO-FITTED to the gameplay collision box + base-seated at spawn
/// (the box stays the physics source of truth → visuals never change handling). Missing folder → cube fallback.
public class VehicleModelLibrary : MonoBehaviour
{
    public static VehicleModelLibrary Instance { get; private set; }

    [Header("Optional overrides (drag a prefab to force one look)")]
    public GameObject carOverride;
    public GameObject busOverride;
    public GameObject rivalBusOverride;

    // category folders under Resources/Vehicles. Each Kind draws from one or more.
    static readonly string[] CarFolders      = { "Vehicles/Cars" };
    static readonly string[] BusFolders      = { "Vehicles/Buses" };
    static readonly string[] TruckFolders    = { "Vehicles/Trucks" };
    static readonly string[] RickshawFolders = { "Vehicles/Rickshaws", "Vehicles/Small" };

    GameObject[] _cars, _buses, _trucks, _rickshaws;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<VehicleModelLibrary>() != null) return;
        new GameObject("VehicleModelLibrary").AddComponent<VehicleModelLibrary>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _cars      = LoadFolders(CarFolders);
        _buses     = LoadFolders(BusFolders);
        _trucks    = LoadFolders(TruckFolders);
        _rickshaws = LoadFolders(RickshawFolders);
        // trucks fall back to buses (and vice-versa) if a folder is empty, so a Kind never has no model
        if (_trucks.Length == 0) _trucks = _buses;
        if (_buses.Length == 0) _buses = _trucks;
        Debug.Log($"[Vehicles] loaded {_cars.Length} cars, {_buses.Length} buses, {_trucks.Length} trucks, {_rickshaws.Length} rickshaws/small.");
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    static GameObject[] LoadFolders(string[] folders)
    {
        var list = new List<GameObject>();
        foreach (var f in folders)
        {
            var loaded = Resources.LoadAll<GameObject>(f);
            if (loaded != null) list.AddRange(loaded);
        }
        return list.ToArray();
    }

    /// Instantiate a visual model for `kind` under `parent`, auto-fitted to the gameplay box `size`.
    /// `variantSeed` (vehicle id) picks a stable variant. Returns null if no model → caller uses its cube.
    public Transform CreateModel(TrafficVehicle.Kind kind, int variantSeed, Vector3 size, Transform parent, bool rival = false)
    {
        GameObject prefab = PickPrefab(kind, variantSeed, rival);
        if (prefab == null) return null;
        var go = Instantiate(prefab, parent);
        go.name = "Model";
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        FitTo(go.transform, size);
        return go.transform;
    }

    GameObject PickPrefab(TrafficVehicle.Kind kind, int seed, bool rival)
    {
        if (rival && rivalBusOverride != null) return rivalBusOverride;
        switch (kind)
        {
            case TrafficVehicle.Kind.Bus:
                return busOverride != null ? busOverride : Pick(_buses, seed);
            case TrafficVehicle.Kind.Truck:
                return Pick(_trucks, seed);
            case TrafficVehicle.Kind.Rickshaw:
                return Pick(_rickshaws, seed);
            default:
                return carOverride != null ? carOverride : Pick(_cars, seed);
        }
    }

    static GameObject Pick(GameObject[] arr, int seed)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[((seed % arr.Length) + arr.Length) % arr.Length];   // stable per-vehicle choice
    }

    // Uniform-scale the model to FILL its box along the LENGTH (z), but never let WIDTH (x) overflow the box
    // (would poke into the next lane). Then base-seat (on the ground, centred X/Z). One uniform scale keeps the
    // model's real proportions; the box (gameplay) stays authoritative.
    static void FitTo(Transform model, Vector3 size)
    {
        var b = MeasureLocal(model);
        if (b.size == Vector3.zero) return;

        // model's longer horizontal axis = LENGTH, shorter = WIDTH. Box's longer = length, shorter = width.
        // FILL THE BOX LENGTH (so two vehicles' visuals touch when their boxes touch — no gap). Most models are
        // proportionally NARROWER than the box, so width is fine; only if the model would overflow the box width
        // by a lot do we clamp (rare) — a small width slack inside the box is invisible, a length gap is not.
        float mLen = Mathf.Max(b.size.x, b.size.z), mWid = Mathf.Min(b.size.x, b.size.z);
        float boxLen = Mathf.Max(size.x, size.z), boxWid = Mathf.Min(size.x, size.z);
        float scale = boxLen / Mathf.Max(mLen, 1e-4f);                 // fill the length
        float widthAtScale = mWid * scale;
        if (widthAtScale > boxWid * 1.25f)                            // only clamp a GROSS width overflow
            scale = (boxWid * 1.25f) / Mathf.Max(mWid, 1e-4f);
        model.localScale = Vector3.one * scale;

        b = MeasureLocal(model);
        model.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
    }

    // bounds of the model in its PARENT's local space (where the box `size` lives)
    static Bounds MeasureLocal(Transform t)
    {
        var parent = t.parent;
        bool has = false; Bounds b = default;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            Bounds wb = r.bounds;
            Vector3 c = parent != null ? parent.InverseTransformPoint(wb.center) : wb.center;
            var lb = new Bounds(c, wb.size);
            if (!has) { b = lb; has = true; } else b.Encapsulate(lb);
        }
        return has ? b : new Bounds(Vector3.zero, Vector3.zero);
    }
}
