using UnityEngine;

/// Headlights + taillights for the player bus, auto-toggled by the day/night cycle: ON at NIGHT and in the
/// EARLY MORNING before the sun rises (and through dusk). Two spot headlights throw light forward; a soft red
/// point taillight glows at the rear. Created in code (no prefab edits); intensity fades with `Darkness` so they
/// don't pop on/off. Play-only, auto-spawned in the game scene; finds the bus + the day/night controller.
public class BusLights : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<BusLights>() != null) return;
        new GameObject("BusLights").AddComponent<BusLights>();
    }

    public float headlightIntensity = 3.2f, headlightRange = 45f, headlightAngle = 70f;
    public float taillightIntensity = 1.4f, taillightRange = 8f;

    BusController _bus;
    DayNightController _dn;
    Light _hL, _hR, _tail;
    bool _built;

    bool _dnSearched;
    void Update()
    {
        if (_bus == null) _bus = BusController.Instance;
        // search for the day/night controller ONCE (not every frame); it may legitimately be absent.
        if (_dn == null && !_dnSearched) { _dn = FindAnyObjectByType<DayNightController>(); _dnSearched = true; }
        if (_bus == null) return;

        if (!_built) Build();

        float dark = _dn != null ? _dn.Darkness : 0f;
        // fade with darkness; fully off in daylight (dark==0)
        if (_hL != null) { _hL.intensity = headlightIntensity * dark; _hL.enabled = dark > 0.02f; }
        if (_hR != null) { _hR.intensity = headlightIntensity * dark; _hR.enabled = dark > 0.02f; }
        if (_tail != null) { _tail.intensity = taillightIntensity * dark; _tail.enabled = dark > 0.02f; }
    }

    void Build()
    {
        Transform model = _bus.busModel != null ? _bus.busModel : _bus.transform;
        // measure the bus so the lights sit at the right spots
        Bounds b = MeasureBounds(model);
        float halfLen = Mathf.Max(1f, b.size.z * 0.5f);
        float halfWid = Mathf.Max(0.4f, b.size.x * 0.5f);
        float lowY = b.min.y + b.size.y * 0.35f;

        // parent the lights to the MODEL so they tilt/move with the bus
        _hL = MakeSpot("Headlight_L", model, new Vector3(-halfWid * 0.7f, lowY, halfLen * 0.95f), new Color(1f, 0.96f, 0.85f));
        _hR = MakeSpot("Headlight_R", model, new Vector3(halfWid * 0.7f, lowY, halfLen * 0.95f), new Color(1f, 0.96f, 0.85f));
        _tail = MakePoint("Taillight", model, new Vector3(0f, lowY, -halfLen * 0.98f), new Color(1f, 0.15f, 0.1f));
        _built = true;
    }

    Light MakeSpot(string name, Transform parent, Vector3 localPos, Color c)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;   // points +Z (forward) like the bus
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = c; l.range = headlightRange; l.spotAngle = headlightAngle;
        l.intensity = 0f; l.enabled = false;
        l.shadows = LightShadows.None;   // cosmetic glow — NO realtime shadows (heavy on WebGL)
        return l;
    }

    Light MakePoint(string name, Transform parent, Vector3 localPos, Color c)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = taillightRange; l.intensity = 0f; l.enabled = false;
        l.shadows = LightShadows.None;
        return l;
    }

    static Bounds MeasureBounds(Transform t)
    {
        bool has = false; Bounds b = default;
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return new Bounds(t.position, new Vector3(2.5f, 3f, 11f));
        // express in local space of t
        return new Bounds(t.InverseTransformPoint(b.center), t.InverseTransformVector(b.size));
    }
}
