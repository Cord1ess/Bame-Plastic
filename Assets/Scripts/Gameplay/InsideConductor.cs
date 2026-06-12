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
    bool _ai;                          // SOLO: auto-walk + auto-collect when unmanned
    Vector3 _center, _half;

    Passenger _target;                 // the currently-outlined rider
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
        BuildUI();
    }

    void ClearSelection()
    {
        if (_selected != null) { _selected.SetSelected(false); _selected = null; }
        _target = null;
    }

    public void SetControlled(bool on)
    {
        _controlled = on;
        if (on) _ai = false;           // human took over
        if (!on)
        {
            ClearSelection();
            if (_ui != null) _ui.SetActive(false);
        }
    }

    /// SOLO: enable/disable the auto-inside-conductor (walks the aisle, collects fares automatically).
    public void SetAI(bool on)
    {
        if (_controlled) { _ai = false; return; }
        _ai = on;
        if (!on) ClearSelection();
    }

    void Update()
    {
        if (BusController.GamePaused) return;          // shared pause freezes him too
        if (_ai && !_controlled) { AiTick(); return; }
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

        // --- one press = collect their owed fare (with a cooldown so he can't spam — he takes his time) ---
        if (GameInput.Instance.action.WasPressedThisFrame() && _target != null)
        {
            if (Time.time >= _collectReadyAt) Collect(_target);
            else Flash("…counting change");        // too soon — feedback that he's still busy
        }

        // --- COMBO decay: the streak expires if he doesn't collect within the window ---
        if (_combo > 0 && Time.time > _comboExpireAt) _combo = 0;

        // --- (kept) shove a standing aisle rider into a seat to free aisle space ---
        if (GameInput.Instance.altAction.WasPressedThisFrame()) TryShove();

        if (_ui != null) _ui.SetActive(_target != null || Time.time < _flashUntil);
        if (_text != null && _target != null && Time.time >= _flashUntil)
            _text.text = "Tk " + _target.OwedFare + "   [E] collect";
    }

    // ---------- SOLO auto-inside-conductor: walk to collectable riders and collect their fares ----------
    void AiTick()
    {
        BusPassengers bp = BusPassengers.Instance;
        if (bp == null) return;
        Vector3 wc = bp.WalkCenter; float hw = bp.walkHalfWidth, hl = bp.walkHalfLength;

        // find the nearest collectable rider (no reach limit — we'll walk to them)
        Passenger goal = null; float bestSqr = float.MaxValue;
        var list = bp.Aboard;
        for (int i = 0; i < list.Count; i++)
        {
            Passenger p = list[i];
            if (p == null || !p.CanCollect) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; goal = p; }
        }

        if (goal != null)
        {
            // step toward the rider's aisle-projected position (clamped to the walk box)
            Vector3 gl = transform.parent != null ? transform.parent.InverseTransformPoint(goal.transform.position) : goal.transform.position;
            Vector3 p2 = transform.localPosition;
            float spd = moveSpeed * (NearStanding() ? crowdSlowFactor : 1f);
            p2.x = Mathf.MoveTowards(p2.x, Mathf.Clamp(gl.x, wc.x - hw, wc.x + hw), spd * Time.deltaTime);
            p2.z = Mathf.MoveTowards(p2.z, Mathf.Clamp(gl.z, wc.z - hl, wc.z + hl), spd * Time.deltaTime);
            p2.y = wc.y;
            transform.localPosition = p2;

            if ((goal.transform.position - transform.position).sqrMagnitude < reachRange * reachRange)
                Collect(goal);
        }
    }

    Passenger _selected;   // the rider currently outlined (so we can clear it when the target changes)

    // pick the nearest un-paid aboard rider within reach; OUTLINE that rider's billboard (selection feedback).
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

        if (_selected != _target)
        {
            if (_selected != null) _selected.SetSelected(false);   // clear the old outline
            if (_target != null) _target.SetSelected(true);        // outline the new target
            _selected = _target;
        }
    }

    [Header("Fare collection feel")]
    [Tooltip("Seconds he must take between fares (no spamming — he counts the change).")]
    public float collectCooldown = 0.7f;
    [Tooltip("Seconds to collect the next fare and keep the combo alive.")]
    public float comboWindow = 4f;
    [Tooltip("Base bonus per combo step (× combo). SHOUTING into the mic multiplies it.")]
    public int comboBaseBonus = 3;

    float _collectReadyAt;
    int _combo;
    float _comboExpireAt;

    public int Combo => _combo;     // HUD reads this

    void Collect(Passenger p)
    {
        _collectReadyAt = Time.time + collectCooldown;   // gate the next collect (anti-spam)

        var gn = BamePlastic.Net.GameNet.Instance;
        if (gn != null && gn.Active && !gn.IsDriver)
        {
            // MULTIPLAYER conductor: send the INTENT; the DRIVER runs the real Collect + earnings and broadcasts
            // the result. Combo/mic bonus is LOCAL feel — track the streak here for the HUD; the driver still
            // owns the authoritative earnings (the base fare). Local feedback so it feels instant.
            gn.SendCollectIntent(p.NetId);
            BumpCombo();
            Flash("Collecting…  x" + _combo);
            return;
        }

        // solo OR the driver itself: collect locally.
        int amt = p.Collect();
        if (amt > 0)
        {
            BumpCombo();
            // SHOUT bonus: louder = bigger combo bonus (calling out fares). 0 mic → tiny bonus; full shout → big.
            float mic = MicInput.Instance != null ? MicInput.Instance.Loudness : 0f;
            int bonus = Mathf.RoundToInt(comboBaseBonus * _combo * (0.5f + 1.5f * mic));
            int total = amt + bonus;

            if (ShiftManager.Instance != null) ShiftManager.Instance.AddEarnings(total);
            Flash(bonus > 0 ? ("Tk " + amt + "  +" + bonus + "  COMBO x" + _combo + (mic > 0.55f ? " (LOUD!)" : ""))
                            : ("Collected  Tk " + amt + " !"));
            if (gn != null && gn.Active && gn.IsDriver) gn.DriverFareCollected(p, total, (byte)gn.LocalRole);
        }
    }

    void BumpCombo()
    {
        if (Time.time > _comboExpireAt) _combo = 0;   // expired → reset before counting this one
        _combo++;
        _comboExpireAt = Time.time + comboWindow;
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
