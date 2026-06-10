using UnityEngine;
using UnityEngine.UI;

/// Conductor 2 — the INSIDE conductor. Rides the bus cabin and, when controlled, walks the aisle (WASD,
/// clamped to the footprint). As he moves, the NEAREST un-paid rider is ALWAYS outlined (snappy, instant).
/// One press of Action collects that rider's OWED fare (a 10/20/30/50 tier that grew with their time aboard).
/// No haggle, no auto-fare — if a rider leaves before you reach them, that fare is lost.
public class InsideConductor : MonoBehaviour
{
    public float moveSpeed = 3f;
    [Tooltip("Max distance to outline + collect from a rider.")]
    public float reachRange = 2.0f;
    public float shoveRange = 1.8f;
    [Tooltip("Speed multiplier while squeezing past standing riders.")]
    public float crowdSlowFactor = 0.45f;
    public float crowdSlowRadius = 1.0f;

    BillboardCharacter _view;
    bool _controlled;
    Vector3 _center, _half;

    Passenger _target;                 // the currently-outlined rider
    Transform _marker;                 // selection outline (a glowing quad above the target)
    float _flashUntil;

    GameObject _ui;
    Text _text;

    public void Setup(BillboardCharacter view, Transform cabin, Vector3 cabinCenter, Vector3 cabinSize)
    {
        _view = view;
        _center = cabinCenter;
        _half = cabinSize * 0.5f;
        transform.SetParent(cabin, false);
        transform.localPosition = _center;
        if (_view != null) _view.ApplyHeight();
        BuildMarker();
        BuildUI();
    }

    public void SetControlled(bool on)
    {
        _controlled = on;
        if (!on)
        {
            _target = null;
            if (_marker != null) _marker.gameObject.SetActive(false);
            if (_ui != null) _ui.SetActive(false);
        }
    }

    void Update()
    {
        if (!_controlled) return;

        // --- walk the aisle box, slowing past standing riders ---
        BusPassengers bp = BusPassengers.Instance;
        Vector3 wc = bp != null ? bp.WalkCenter : _center;
        float hw = bp != null ? bp.walkHalfWidth : 0.3f;
        float hl = bp != null ? bp.walkHalfLength : 3.0f;
        float spd = moveSpeed * (NearStanding() ? crowdSlowFactor : 1f);
        Vector2 mv = GameInput.Instance.move.ReadValue<Vector2>();
        Vector3 p = transform.localPosition;
        p.x += mv.x * spd * Time.deltaTime;
        p.z += mv.y * spd * Time.deltaTime;
        p.x = Mathf.Clamp(p.x, wc.x - hw, wc.x + hw);
        p.z = Mathf.Clamp(p.z, wc.z - hl, wc.z + hl);
        p.y = wc.y;
        transform.localPosition = p;

        // --- snappy selection: always outline the nearest collectable rider in reach ---
        UpdateTarget(bp);

        // --- one press = collect their owed fare ---
        if (GameInput.Instance.action.WasPressedThisFrame() && _target != null)
            Collect(_target);

        // --- (kept) shove a standing aisle rider into a seat to free aisle space ---
        if (GameInput.Instance.altAction.WasPressedThisFrame()) TryShove();

        if (_ui != null) _ui.SetActive(_target != null || Time.time < _flashUntil);
        if (_text != null && _target != null && Time.time >= _flashUntil)
            _text.text = "Tk " + _target.OwedFare + "   [E] collect";
    }

    // pick the nearest un-paid aboard rider within reach; move the outline marker onto it (or hide it).
    void UpdateTarget(BusPassengers bp)
    {
        _target = null;
        float bestSqr = reachRange * reachRange;
        if (bp != null)
        {
            var list = bp.Aboard;
            for (int i = 0; i < list.Count; i++)
            {
                Passenger p = list[i];
                if (p == null || !p.CanCollect) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; _target = p; }
            }
        }

        if (_marker != null)
        {
            bool show = _target != null;
            if (_marker.gameObject.activeSelf != show) _marker.gameObject.SetActive(show);
            if (show)
            {
                _marker.position = _target.transform.position + Vector3.up * 2.1f;
                _marker.rotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
            }
        }
    }

    void Collect(Passenger p)
    {
        int amt = p.Collect();
        if (amt > 0)
        {
            if (ShiftManager.Instance != null) ShiftManager.Instance.AddEarnings(amt);
            Flash("Collected  Tk " + amt + " !");
        }
    }

    void TryShove()
    {
        Passenger best = null;
        float bestSqr = shoveRange * shoveRange;
        BusPassengers bp = BusPassengers.Instance;
        if (bp == null) return;
        var list = bp.Aboard;
        for (int i = 0; i < list.Count; i++)
        {
            Passenger p = list[i];
            if (p == null || !p.IsStanding) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = p; }
        }
        if (best != null && bp.ShovePassenger(best)) Flash("Pushed back!");
    }

    bool NearStanding()
    {
        BusPassengers bp = BusPassengers.Instance;
        if (bp == null) return false;
        float r2 = crowdSlowRadius * crowdSlowRadius;
        var list = bp.Aboard;
        for (int i = 0; i < list.Count; i++)
        {
            Passenger p = list[i];
            if (p == null || !p.IsStanding) continue;
            if ((p.transform.position - transform.position).sqrMagnitude < r2) return true;
        }
        return false;
    }

    void Flash(string msg)
    {
        if (_text != null) _text.text = msg;
        _flashUntil = Time.time + 1.1f;
    }

    // a small glowing quad that floats over the selected rider = the selection outline.
    void BuildMarker()
    {
        if (_marker != null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.name = "FareTarget";
        go.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
        var r = go.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (sh != null) { r.material = new Material(sh); if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", new Color(1f, 0.78f, 0.3f)); if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", new Color(1f, 0.78f, 0.3f)); }
        _marker = go.transform;
        _marker.gameObject.SetActive(false);
    }

    void BuildUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);

        GameObject canvasGO = new GameObject("FareUI");
        Canvas c = canvasGO.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler sc = canvasGO.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);

        GameObject t = new GameObject("FareText");
        t.transform.SetParent(canvasGO.transform, false);
        _text = t.AddComponent<Text>();
        _text.font = font;
        _text.fontSize = 40;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.color = new Color(1f, 0.85f, 0.35f);
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = _text.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.28f);
        rt.anchorMax = new Vector2(0.5f, 0.28f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(900, 80);

        _ui = canvasGO;
        _ui.SetActive(false);
    }
}
