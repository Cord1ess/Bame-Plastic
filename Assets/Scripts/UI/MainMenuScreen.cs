using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Title screen — LEFT-ALIGNED and VERTICALLY CENTRED (the bus sits on the right, menu on the left). Game
/// name, accent rule, and the primary menu stacked down the left, centred as a block on screen. On entering
/// play, each element SLIDES IN FROM THE LEFT and eases to rest (no overshoot) with a staggered delay.
public class MainMenuScreen
{
    readonly GameObject _root;
    readonly List<SpringIn> _springs = new List<SpringIn>();   // ordered top→bottom for the stagger

    const float MarginX = 90f;     // left margin (anchored-X)

    public MainMenuScreen(Transform parent, MenuController menu)
    {
        _root = new GameObject("TitleScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);   // hide FIRST so a build error can never leave a stray panel on screen

        // A left-pinned, VERTICALLY-CENTRED column. Everything is laid out relative to this, so the whole
        // block sits centred on screen height while hugging the left edge.
        var colGO = new GameObject("Column", typeof(RectTransform));
        colGO.transform.SetParent(_root.transform, false);
        var col = (RectTransform)colGO.transform;
        col.anchorMin = new Vector2(0, 0.5f); col.anchorMax = new Vector2(0, 0.5f); col.pivot = new Vector2(0, 0.5f);
        col.anchoredPosition = new Vector2(MarginX, 0f);
        col.sizeDelta = new Vector2(640, 700);
        Transform c = colGO.transform;

        // y positions measured from the column centre (top = +, bottom = -)
        var title = Place(PixelUI.Label(c, "Title", "BAME PLASTIC", 88, TextAnchor.MiddleLeft, PixelUI.Ink, outline: true), 280, new Vector2(1000, 110));
        var sub   = Place(PixelUI.Label(c, "Subtitle", "DHAKA BUS CO-OP", 30, TextAnchor.MiddleLeft, PixelUI.Gold, outline: true), 206, new Vector2(640, 40));
        var rule  = Place(PixelUI.Block(c, "Rule", PixelUI.Gold), 172, new Vector2(300, 3));

        float bw = 380f, bh = 72f, gap = 88f, by0 = 70f;
        AddButton(c, "Play", "PLAY ONLINE", by0,            bw, bh, menu.PlayMultiplayer, PixelUI.Gold);
        AddButton(c, "Solo", "PLAY SOLO",   by0 - gap,      bw, bh, menu.PlaySolo,        PixelUI.Cyan);
        AddButton(c, "Settings", "SETTINGS", by0 - gap * 2, bw, bh, menu.OpenSettings,    PixelUI.InkDim);
#if !UNITY_WEBGL
        AddButton(c, "Quit", "QUIT",         by0 - gap * 3, bw, bh, menu.Quit,            PixelUI.Red);
#endif

        // springs in visual order: title, sub, rule, then the buttons (already appended)
        _springs.Insert(0, AddSpring(rule.rectTransform));
        _springs.Insert(0, AddSpring(sub.rectTransform));
        _springs.Insert(0, AddSpring(title.rectTransform));

        // version tag, bottom-left of the SCREEN
        var ver = PixelUI.Label(_root.transform, "Version", "v0.1  pre-alpha", 20, TextAnchor.LowerLeft, PixelUI.InkDim, outline: true);
        var vrt = ver.rectTransform;
        vrt.anchorMin = vrt.anchorMax = new Vector2(0, 0); vrt.pivot = new Vector2(0, 0);
        vrt.anchoredPosition = new Vector2(MarginX, 24); vrt.sizeDelta = new Vector2(360, 26);
    }

    // left-pivoted at column-x 0, vertical position y (from column centre)
    static T Place<T>(T graphic, float y, Vector2 size) where T : Graphic
    {
        var rt = graphic.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = size;
        return graphic;
    }

    void AddButton(Transform col, string name, string label, float y, float w, float h, System.Action onClick, Color accent)
    {
        var btn = PixelUIWidgets.Button(col, name, label, new Vector2(0, 0.5f), new Vector2(0, y), new Vector2(w, h), onClick, accent);
        var lbl = btn.transform.Find("Label") as RectTransform;
        if (lbl != null) { var t = lbl.GetComponent<Text>(); if (t != null) t.alignment = TextAnchor.MiddleLeft; lbl.offsetMin = new Vector2(22, lbl.offsetMin.y); }
        _springs.Add(AddSpring((RectTransform)btn.transform));
    }

    SpringIn AddSpring(RectTransform rt) => rt.gameObject.AddComponent<SpringIn>();

    /// Staggered slide-in from the left (play mode only; called by MenuController when the title is shown).
    public void PlayIntro()
    {
        for (int i = 0; i < _springs.Count; i++)
        {
            var s = _springs[i];
            if (s == null) continue;
            float restX = ((RectTransform)s.transform).anchoredPosition.x;
            s.Play(restX, 800f, i * 0.06f);     // from 800px left, 60ms stagger top→bottom
        }
    }

    public void SetVisible(bool on)
    {
        if (_root == null) return;
        _root.SetActive(on);
        if (on && Application.isPlaying) PlayIntro();
    }
}
