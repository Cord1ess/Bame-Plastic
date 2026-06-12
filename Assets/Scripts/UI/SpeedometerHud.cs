using UnityEngine;
using UnityEngine.UI;

/// Bottom-right speed readout in the PIXEL theme (PixelUI): a bordered panel with a big km/h number, gear,
/// and a beveled RPM bar that fills within each gear and snaps back on every upshift (BusController.Rpm01).
/// Auto-spawns; reads BusController.Instance. No scene setup.
public class SpeedometerHud : MonoBehaviour
{
    /// Spawn the speedometer. Called by ShiftManager.BeginShift() when GAMEPLAY actually starts — so it never
    /// appears during the living menu. (No auto-spawn: that fired in the menu too.)
    public static void Spawn()
    {
        if (FindAnyObjectByType<SpeedometerHud>() != null) return;
        var go = new GameObject("SpeedometerHud");
        go.AddComponent<SpeedometerHud>();
        SceneHierarchy.Parent(go, SceneHierarchy.Category.UI);
    }

    BusController _bus;
    Text _speed, _gear;
    Image _rpmFill;
    RectTransform _rpmFillRT;
    float _rpmInner;

    void Start() { BuildUI(); }

    int _lastSpeed = int.MinValue, _lastGear = int.MinValue;

    void Update()
    {
        if (_bus == null) _bus = BusController.Instance;
        if (_bus == null) return;

        // only rebuild the text strings when the displayed integer actually CHANGES (avoids a string alloc every
        // frame → no per-frame GC on WebGL). Speed/gear change a few times a second, not 60×.
        int spd = Mathf.RoundToInt(_bus.SpeedKmh);
        if (spd != _lastSpeed) { _speed.text = spd.ToString(); _lastSpeed = spd; }
        int gear = _bus.Gear;
        if (gear != _lastGear) { _gear.text = "GEAR " + gear; _lastGear = gear; }

        float rpm = Mathf.Clamp01(_bus.Rpm01);
        _rpmFillRT.sizeDelta = new Vector2(_rpmInner * rpm, _rpmFillRT.sizeDelta.y);
        _rpmFill.color = rpm > 0.85f ? PixelUI.Red : PixelUI.Cyan;     // redline flash
    }

    void BuildUI()
    {
        Canvas c = PixelUI.Canvas(transform, "SpeedoCanvas", 9);

        // bordered panel anchored bottom-right
        var panel = PixelUI.Panel(c.transform, "SpeedoPanel", new Vector2(1, 0), new Vector2(-32, 32), new Vector2(300, 188));

        // big km/h number
        _speed = PixelUI.Label(panel.transform, "Speed", "0", 88, TextAnchor.UpperRight, PixelUI.Text);
        var sp = _speed.rectTransform;
        sp.anchorMin = sp.anchorMax = new Vector2(1, 1); sp.pivot = new Vector2(1, 1);
        sp.anchoredPosition = new Vector2(-20, -14); sp.sizeDelta = new Vector2(260, 96);

        var unit = PixelUI.Label(panel.transform, "Unit", "KM/H", 22, TextAnchor.UpperRight, PixelUI.TextDim);
        var ur = unit.rectTransform;
        ur.anchorMin = ur.anchorMax = new Vector2(1, 1); ur.pivot = new Vector2(1, 1);
        ur.anchoredPosition = new Vector2(-20, -104); ur.sizeDelta = new Vector2(260, 28);

        // gear badge (own small panel)
        var gearBadge = PixelUI.Panel(panel.transform, "GearBadge", new Vector2(0, 1), new Vector2(16, -16), new Vector2(132, 44), true);
        _gear = PixelUI.Label(gearBadge.transform, "Gear", "GEAR 1", 22, TextAnchor.MiddleCenter, PixelUI.Gold);
        var gr = _gear.rectTransform;
        gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one; gr.offsetMin = Vector2.zero; gr.offsetMax = Vector2.zero;

        // RPM bar across the bottom of the panel
        _rpmFillRT = PixelUI.BeveledBar(panel.transform, "RpmBar", new Vector2(0.5f, 0), new Vector2(0, 16),
                                        new Vector2(268, 26), PixelUI.Cyan, out _rpmFill, out _rpmInner);
    }
}
