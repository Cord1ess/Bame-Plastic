using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// Interactive PixelUI widgets — buttons, toggles, sliders, dropdowns, input fields, tabs — in the same
/// dusk-slate cut-corner pixel theme as PixelUI. Flat fills + hard colour-swap states (NO gradients, no
/// tweens) and a thin accent underline on hover for a crisp, detailed-but-minimal retro feel. All code-built.
public static class PixelUIWidgets
{
    // ---- Button ----

    /// A pixel button: cut-corner panel, label, hard hover/press colour states + an accent underline on hover.
    public static PixelButton Button(Transform parent, string name, string label, Vector2 anchor, Vector2 pos,
                                     Vector2 size, Action onClick, Color? accent = null)
    {
        var img = MakePanel(parent, name, anchor, pos, size, PixelUI.PanelFill);
        // a Selectable makes this focusable by the EventSystem so a GAMEPAD/KEYBOARD can navigate to it. Visuals
        // are driven by PixelButton's own handlers (incl. ISelect/ISubmit), so the Selectable does no colour
        // transition itself (Transition.None) — it only provides focus + Automatic directional navigation.
        var sel = img.gameObject.AddComponent<Selectable>();
        sel.transition = Selectable.Transition.None;
        sel.targetGraphic = img;
        var nav = sel.navigation; nav.mode = Navigation.Mode.Automatic; sel.navigation = nav;
        var btn = img.gameObject.AddComponent<PixelButton>();
        btn.Init(img, accent ?? PixelUI.Gold, onClick);

        var txt = PixelUI.Label(img.transform, "Label", label, Mathf.RoundToInt(size.y * 0.42f),
                                TextAnchor.MiddleCenter, PixelUI.Ink);
        Stretch(txt.rectTransform, 10f);
        btn.SetLabel(txt);

        // thin accent underline, hidden until hover (the "detail")
        var underline = PixelUI.Block(img.transform, "Underline", accent ?? PixelUI.Gold);
        var urt = underline.rectTransform;
        urt.anchorMin = new Vector2(0.5f, 0f); urt.anchorMax = new Vector2(0.5f, 0f); urt.pivot = new Vector2(0.5f, 0f);
        urt.anchoredPosition = new Vector2(0f, 7f); urt.sizeDelta = new Vector2(size.x * 0.5f, 3f);
        btn.SetUnderline(underline);
        return btn;
    }

    // ---- Toggle / checkbox ----

    public static PixelToggle Toggle(Transform parent, string name, string label, Vector2 anchor, Vector2 pos,
                                     Vector2 size, bool value, Action<bool> onChange)
    {
        var row = MakeRect(parent, name, anchor, pos, size);
        float box = size.y;
        var boxImg = MakePanel(row, "Box", new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(box, box), PixelUI.PanelAlt);
        var check = PixelUI.Block(boxImg.transform, "Check", PixelUI.Green);
        Stretch(check.rectTransform, PixelUI.BorderPx + 3f);

        var txt = PixelUI.Label(row, "Label", label, Mathf.RoundToInt(box * 0.5f), TextAnchor.MiddleLeft, PixelUI.Ink);
        var trt = txt.rectTransform;
        trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0, 0.5f);
        trt.offsetMin = new Vector2(box + 14f, 0); trt.offsetMax = new Vector2(0, 0);

        var tog = row.gameObject.AddComponent<PixelToggle>();
        tog.Init(boxImg, check, value, onChange);
        return tog;
    }

    // ---- Slider (0..1) ----

    public static PixelSlider Slider(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                     float value, Action<float> onChange, Color? fillColor = null)
    {
        var track = MakePanel(parent, name, anchor, pos, size, PixelUI.PanelAlt);
        float pad = PixelUI.BorderPx + 2f;
        var fill = PixelUI.Block(track.transform, "Fill", fillColor ?? PixelUI.Cyan);
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0, 0.5f); frt.anchorMax = new Vector2(0, 0.5f); frt.pivot = new Vector2(0, 0.5f);
        frt.anchoredPosition = new Vector2(pad, 0);
        float inner = size.x - pad * 2f;
        frt.sizeDelta = new Vector2(inner * Mathf.Clamp01(value), size.y - pad * 2f);

        // a chunky knob
        var knob = MakePanel(track.transform, "Knob", new Vector2(0, 0.5f), Vector2.zero, new Vector2(size.y * 0.9f, size.y * 1.25f), PixelUI.Frame);

        var sl = track.gameObject.AddComponent<PixelSlider>();
        sl.Init(track.rectTransform, fill, knob.rectTransform, inner, pad, value, onChange);
        return sl;
    }

    // ---- Stepper dropdown (◀ value ▶) — simpler & more pixel than a popup list ----

    public static PixelStepper Stepper(Transform parent, string name, string[] options, Vector2 anchor, Vector2 pos,
                                       Vector2 size, int index, Action<int> onChange)
    {
        var panel = MakePanel(parent, name, anchor, pos, size, PixelUI.PanelAlt);
        float arrow = size.y;
        var left  = Button(panel.transform, "Prev", "<", new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(arrow, size.y - 4), null);
        var right = Button(panel.transform, "Next", ">", new Vector2(1, 0.5f), new Vector2(-2, 0), new Vector2(arrow, size.y - 4), null);
        var val = PixelUI.Label(panel.transform, "Value", "", Mathf.RoundToInt(size.y * 0.4f), TextAnchor.MiddleCenter, PixelUI.Ink);
        var vrt = val.rectTransform;
        vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(1, 1); vrt.pivot = new Vector2(0.5f, 0.5f);
        vrt.offsetMin = new Vector2(arrow, 0); vrt.offsetMax = new Vector2(-arrow, 0);

        var st = panel.gameObject.AddComponent<PixelStepper>();
        st.Init(options, index, val, left, right, onChange);
        return st;
    }

    // ---- Text input field ----

    public static InputField Input(Transform parent, string name, string value, string placeholder, Vector2 anchor,
                                   Vector2 pos, Vector2 size, Action<string> onChange = null, int maxLen = 0)
    {
        var panel = MakePanel(parent, name, anchor, pos, size, PixelUI.PanelAlt);
        var input = panel.gameObject.AddComponent<InputField>();
        input.targetGraphic = panel;

        var txt = PixelUI.Label(panel.transform, "Text", value, Mathf.RoundToInt(size.y * 0.42f), TextAnchor.MiddleLeft, PixelUI.Ink);
        Stretch(txt.rectTransform, 14f);
        var ph = PixelUI.Label(panel.transform, "Placeholder", placeholder, Mathf.RoundToInt(size.y * 0.42f), TextAnchor.MiddleLeft, PixelUI.InkDim);
        Stretch(ph.rectTransform, 14f);

        input.textComponent = txt;
        input.placeholder = ph;
        input.text = value;
        input.characterLimit = maxLen;
        input.caretColor = PixelUI.Ink;
        input.customCaretColor = true;
        input.selectionColor = new Color(PixelUI.Gold.r, PixelUI.Gold.g, PixelUI.Gold.b, 0.4f);
        if (onChange != null) input.onValueChanged.AddListener(v => onChange(v));
        return input;
    }

    // ---- Tabs ----

    /// A row of tab buttons; calls onSelect(index) and visually marks the active one. Returns the bar so you
    /// can position content under it.
    public static PixelTabs Tabs(Transform parent, string name, string[] labels, Vector2 anchor, Vector2 pos,
                                 Vector2 size, Action<int> onSelect)
    {
        var bar = MakeRect(parent, name, anchor, pos, size);
        float w = size.x / Mathf.Max(1, labels.Length);
        var tabs = bar.gameObject.AddComponent<PixelTabs>();
        var btns = new PixelButton[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            btns[i] = Button(bar, "Tab" + i, labels[i], new Vector2(0, 0.5f), new Vector2(i * w + 2, 0),
                             new Vector2(w - 4, size.y), () => tabs.Select(idx));
        }
        tabs.Init(btns, onSelect);
        return tabs;
    }

    // ---- shared helpers ----

    static Image MakePanel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color tint)
    {
        var img = PixelUI.Panel(parent, name, anchor, pos, size);
        img.color = tint;
        return img;
    }

    static Transform MakeRect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return go.transform;
    }

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
    }
}
