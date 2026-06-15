using UnityEngine;
using UnityEngine.UI;

/// On-screen MIC indicator + fare-combo display for the conductors. Shows ONLY while the local player is
/// controlling a conductor (C1 = door / C2 = inside): a loudness meter (green→red) labelled with what shouting
/// does for that role (C1 "CALL PASSENGERS", C2 "FARE BONUS"), plus C2's live COMBO multiplier. Reads MicInput +
/// the RoleController. Play-only, auto-spawned; pixel-UI theme. No gameplay logic here — pure readout.
public class ConductorMicHud : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<ConductorMicHud>() != null) return;
        new GameObject("ConductorMicHud").AddComponent<ConductorMicHud>();
    }

    GameObject _root;
    Image _micFill; RectTransform _micFillRt; float _micInner;
    Text _roleLabel, _comboLabel, _micState;
    RoleController _rc;

    void Start()
    {
        var canvas = PixelUI.Canvas(transform, "ConductorMicCanvas", 40);
        _root = new GameObject("MicHud", typeof(RectTransform));
        _root.transform.SetParent(canvas.transform, false);
        // STRETCH the root to fill the canvas so the panel's (0.5,0) anchor = the SCREEN bottom-centre
        // (a default RectTransform sits at the canvas CENTRE → that was why the meter showed in the middle).
        var rrt = (RectTransform)_root.transform;
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

        // pinned to the BOTTOM-CENTRE edge (the conductor mic meter + combo readout)
        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(0.5f, 0f), new Vector2(0, 28), new Vector2(420, 96));
        Transform pan = panel.transform;

        _roleLabel = PixelUI.Label(pan, "Role", "MIC", 15, TextAnchor.UpperLeft, PixelUI.Gold);
        var rl = _roleLabel.rectTransform; rl.anchorMin = rl.anchorMax = new Vector2(0, 1); rl.pivot = new Vector2(0, 1);
        rl.anchoredPosition = new Vector2(18, -10); rl.sizeDelta = new Vector2(300, 20);
        _roleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

        _micState = PixelUI.Label(pan, "MicState", "", 15, TextAnchor.UpperRight, PixelUI.InkDim);
        var ms = _micState.rectTransform; ms.anchorMin = ms.anchorMax = new Vector2(1, 1); ms.pivot = new Vector2(1, 1);
        ms.anchoredPosition = new Vector2(-18, -10); ms.sizeDelta = new Vector2(110, 20);

        // loudness meter
        _micFillRt = PixelUI.BeveledBar(pan, "MicBar", new Vector2(0, 0), new Vector2(18, 18), new Vector2(384, 30),
                                        PixelUI.Green, out _micFill, out _micInner);

        // combo (C2)
        _comboLabel = PixelUI.Label(pan, "Combo", "", 26, TextAnchor.MiddleCenter, PixelUI.Gold);
        var cl = _comboLabel.rectTransform; cl.anchorMin = cl.anchorMax = new Vector2(0.5f, 0.5f); cl.pivot = new Vector2(0.5f, 0.5f);
        cl.anchoredPosition = new Vector2(0, 18); cl.sizeDelta = new Vector2(380, 30);

        _root.SetActive(false);
    }

    void Update()
    {
        if (_rc == null) _rc = FindAnyObjectByType<RoleController>();
        bool c1 = _rc != null && _rc.ControllingConductor1;
        bool c2 = _rc != null && _rc.ControllingConductor2;
        bool show = c1 || c2;
        if (_root.activeSelf != show) _root.SetActive(show);
        if (!show) return;

        var mic = MicInput.Instance;
        bool micOk = mic != null && mic.MicAvailable && mic.Enabled;
        float loud = mic != null ? mic.Loudness : 0f;

        _roleLabel.text = c1 ? "SHOUT LOCATIONS TO GET MORE PASSENGERS"
                             : "ARGUE LOUDER TO GET MORE BHARA";
        _micState.text = micOk ? (mic.Shouting ? "SHOUTING!" : "") : "no mic";
        _micState.color = mic != null && mic.Shouting ? PixelUI.Red : PixelUI.InkDim;

        // meter
        if (_micFillRt != null)
        {
            float w = _micInner * Mathf.Clamp01(loud);
            _micFillRt.sizeDelta = new Vector2(w, _micFillRt.sizeDelta.y);
            if (_micFill != null) _micFill.color = Color.Lerp(PixelUI.Green, PixelUI.Red, Mathf.Clamp01(loud));
        }

        // combo (C2 only)
        if (c2 && _rc.InsideConductor != null && _rc.InsideConductor.Combo > 1)
            _comboLabel.text = "COMBO  x" + _rc.InsideConductor.Combo;
        else
            _comboLabel.text = "";
    }
}
