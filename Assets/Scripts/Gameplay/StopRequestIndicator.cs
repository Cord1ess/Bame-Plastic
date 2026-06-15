using UnityEngine;

/// A "STOP REQUESTED" sign that pops up on the bus ROOF when an aboard rider rings the bell (BusPassengers.
/// StopRequested) — the driver's cue to pull over so they can alight. Procedural (a small emissive quad with a
/// generated arrow/▼ texture, billboarded toward the camera), play-only, parented to the bus so it rides along.
/// Pulses while active, hidden otherwise. Derived from the SYNCED aboard set, so it shows on every MP client.
public class StopRequestIndicator : MonoBehaviour
{
    // self-attach to the bus once the game scene is loaded, so no other file has to reference this type.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        var bus = Object.FindAnyObjectByType<BusPassengers>();
        if (bus != null && bus.GetComponent<StopRequestIndicator>() == null)
            bus.gameObject.AddComponent<StopRequestIndicator>();
    }

    [Tooltip("Height (m) above the bus roof to float the sign.")]
    public float heightAboveRoof = 1.2f;
    [Tooltip("Sign size (m).")]
    public float size = 1.6f;
    [Tooltip("Sign tint.")]
    public Color color = new Color(1f, 0.85f, 0.2f, 1f);

    BusPassengers _bus;
    WorldSign _sign;
    float _topY;

    void Start()
    {
        if (!Application.isPlaying) return;
        _bus = GetComponent<BusPassengers>();
        _topY = ComputeRoofLocalY();
        Build();
    }

    float ComputeRoofLocalY()
    {
        // top of the bus in WORLD, converted to a local height above this transform — so the sign sits on the roof.
        var rends = GetComponentsInChildren<MeshRenderer>();
        bool has = false; Bounds b = default;
        foreach (var r in rends) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        float topWorld = has ? b.max.y : transform.position.y + 3f;
        return topWorld - transform.position.y;
    }

    void Build()
    {
        // an ICON badge (downward ▼ "get off") with a small text label above — build-safe (no URP shader lookup).
        _sign = WorldSign.Create(transform, "StopRequestSign (runtime)", WorldSign.Icon.Alight, 1.3f);
        _sign.gameObject.hideFlags = HideFlags.DontSave;
        _sign.SetText("STOP REQUESTED");
        _sign.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_sign == null || _bus == null) return;
        // in MP, conductor clients' mirrored riders don't run the ride timer, so read the driver-synced flag.
        var gn = BamePlastic.Net.GameNet.Instance;
        bool show = (gn != null && gn.Active) ? gn.StopRequestedNet : _bus.StopRequested;
        if (_sign.gameObject.activeSelf != show) _sign.gameObject.SetActive(show);
        if (!show) return;

        // float above the roof, billboard toward the camera, gentle pulse on the text colour
        _sign.transform.localPosition = new Vector3(0f, _topY + heightAboveRoof, 0f);
        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
        _sign.SetColor(Color.Lerp(new Color(1f, 0.9f, 0.5f), color, pulse));
        _sign.FaceCamera();
    }

    void OnDestroy()
    {
        if (_sign != null) Destroy(_sign.gameObject);
    }
}
