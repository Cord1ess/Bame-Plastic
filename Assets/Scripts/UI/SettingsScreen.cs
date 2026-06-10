using UnityEngine;
using UnityEngine.UI;

/// Settings: a tabbed pixel panel — Audio / Graphics / Player / Controls. Edits persist to SettingsStore
/// (PlayerPrefs) and apply live. Built into a panel toggled by MenuController.
public class SettingsScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;
    GameObject[] _pages;

    public SettingsScreen(Transform parent, MenuController menu)
    {
        _menu = menu;
        _root = new GameObject("SettingsScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);   // hide FIRST so a build error can never leave a stray panel on screen
        Transform root = _root.transform;

        // LEFT-ALIGNED, vertically-centred, COMPACT panel (matches the menu; bus visible on the right).
        const float MX = 90f, PW = 600f, PH = 720f;
        var panel = PixelUI.Panel(root, "Panel", new Vector2(0, 0.5f), new Vector2(MX, 0f), new Vector2(PW, PH));
        Transform pan = panel.transform;
        float pad = 28f, innerW = PW - pad * 2f;

        var heading = PixelUI.Label(pan, "Heading", "SETTINGS", 40, TextAnchor.UpperLeft, PixelUI.Ink);
        var hrt = heading.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1f); hrt.pivot = new Vector2(0, 1f);
        hrt.anchoredPosition = new Vector2(pad, -22); hrt.sizeDelta = new Vector2(innerW, 48);

        // tabs row (build pages first — Tabs.Init calls Select(0) → SelectPage which needs _pages)
        string[] tabs = { "AUDIO", "GRAPHICS", "PLAYER", "CONTROLS" };
        _pageW = innerW;                                  // every row lays out within this width (no overflow)
        _pages = new GameObject[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            var page = new GameObject("Page" + i, typeof(RectTransform));
            page.transform.SetParent(pan, false);
            var prt = (RectTransform)page.transform;
            prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(0, 1); prt.pivot = new Vector2(0, 1);
            prt.anchoredPosition = new Vector2(pad, -150); prt.sizeDelta = new Vector2(innerW, PH - 230f);
            _pages[i] = page;
        }

        BuildAudio(_pages[0].transform);
        BuildGraphics(_pages[1].transform);
        BuildPlayer(_pages[2].transform);
        BuildControls(_pages[3].transform);

        // tab bar under the heading
        PixelUIWidgets.Tabs(pan, "Tabs", tabs, new Vector2(0, 1f), new Vector2(pad, -84), new Vector2(innerW, 50), SelectPage);

        // back button, bottom-left of the panel
        PixelUIWidgets.Button(pan, "Back", "◀ BACK", new Vector2(0, 0f), new Vector2(pad, 22), new Vector2(220, 56),
                              () => { SettingsStore.Save(); _menu.BackToTitle(); }, PixelUI.InkDim);

        SelectPage(0);
    }

    void SelectPage(int i)
    {
        if (_pages == null) return;                       // guard: Tabs may fire Select during construction
        for (int p = 0; p < _pages.Length; p++) if (_pages[p]) _pages[p].SetActive(p == i);
    }

    // ---- row layout: every page is `_pageW` wide. A row = a left label + a right control, both inside the
    //      page so NOTHING overflows. ctrlW is computed from the page width minus the label column. ----
    float _pageW;
    const float RowH = 60f, LabelW = 190f, ColGap = 14f;
    float CtrlW => _pageW - LabelW - ColGap;          // control fills the rest of the row

    // place a left-aligned label for row i; returns the row's control width
    void RowLabel(Transform p, int i, string label, int size = 22)
    {
        var t = PixelUI.Label(p, "Lbl_" + i, label, size, TextAnchor.MiddleLeft, PixelUI.Ink);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, -(i * RowH)); rt.sizeDelta = new Vector2(LabelW, RowH - 12f);
    }
    // anchor/pos/size for a control on the right half of row i (top-left anchored, so width is explicit)
    Vector2 CtrlAnchor => new Vector2(0, 1);
    Vector2 CtrlPos(int i) => new Vector2(LabelW + ColGap, -(i * RowH) - 6f);

    // ---- pages ----
    void BuildAudio(Transform p)
    {
        RowLabel(p, 0, "MASTER");
        PixelUIWidgets.Slider(p, "Master", CtrlAnchor, CtrlPos(0), new Vector2(CtrlW, 30), SettingsStore.MasterVol,
                              v => { SettingsStore.MasterVol = v; SettingsStore.ApplyAudio(); });
        RowLabel(p, 1, "MUSIC");
        PixelUIWidgets.Slider(p, "Music", CtrlAnchor, CtrlPos(1), new Vector2(CtrlW, 30), SettingsStore.MusicVol,
                              v => { SettingsStore.MusicVol = v; SettingsStore.ApplyAudio(); }, PixelUI.Gold);
        RowLabel(p, 2, "SFX");
        PixelUIWidgets.Slider(p, "Sfx", CtrlAnchor, CtrlPos(2), new Vector2(CtrlW, 30), SettingsStore.SfxVol,
                              v => { SettingsStore.SfxVol = v; SettingsStore.ApplyAudio(); }, PixelUI.Green);
    }

    void BuildGraphics(Transform p)
    {
        // graphics changes are PENDING until APPLY (so resolution etc. all commit together)
        RowLabel(p, 0, "QUALITY");
        PixelUIWidgets.Stepper(p, "Quality", QualitySettings.names, CtrlAnchor, CtrlPos(0), new Vector2(CtrlW, 42),
                               Mathf.Clamp(SettingsStore.QualityIndex, 0, QualitySettings.names.Length - 1),
                               i => SettingsStore.QualityIndex = i);

        RowLabel(p, 1, "FRAMERATE");
        var fpsLabels = System.Array.ConvertAll(SettingsStore.FpsOptions, SettingsStore.FpsLabel);
        int fpsIdx = System.Array.IndexOf(SettingsStore.FpsOptions, SettingsStore.TargetFps); if (fpsIdx < 0) fpsIdx = 1;
        PixelUIWidgets.Stepper(p, "Fps", fpsLabels, CtrlAnchor, CtrlPos(1), new Vector2(CtrlW, 42), fpsIdx,
                               i => SettingsStore.TargetFps = SettingsStore.FpsOptions[i]);

        RowLabel(p, 2, "RESOLUTION");
        var res = Resolutions();
        string[] labels = new string[res.Length];
        int cur = 0;
        for (int i = 0; i < res.Length; i++)
        {
            labels[i] = res[i].x + "x" + res[i].y;
            if (res[i].x == SettingsStore.ResW && res[i].y == SettingsStore.ResH) cur = i;
        }
        PixelUIWidgets.Stepper(p, "Resolution", labels, CtrlAnchor, CtrlPos(2), new Vector2(CtrlW, 42), cur,
                               i => SettingsStore.SetResolution(res[i].x, res[i].y));

        RowLabel(p, 3, "FULLSCREEN");
        PixelUIWidgets.Toggle(p, "Fullscreen", "", CtrlAnchor, CtrlPos(3), new Vector2(42, 42), SettingsStore.Fullscreen,
                              v => SettingsStore.Fullscreen = v);

        // APPLY button — commits all graphics settings at once
        var apply = PixelUIWidgets.Button(p, "Apply", "APPLY", new Vector2(1, 1), new Vector2(0, -(4 * RowH) - 6f),
                                          new Vector2(CtrlW, 48), () => SettingsStore.ApplyGraphics(), PixelUI.Green);
        // note under it
        PixelUI.Label(p, "GfxNote", "click APPLY to commit graphics changes", 16, TextAnchor.UpperLeft, PixelUI.InkDim)
               .rectTransform.anchoredPosition = new Vector2(0, -(5 * RowH) + 4f);
    }

    void BuildPlayer(Transform p)
    {
        RowLabel(p, 0, "NAME");
        PixelUIWidgets.Input(p, "NameField", SettingsStore.PlayerName, "your name", CtrlAnchor, CtrlPos(0),
                             new Vector2(CtrlW, 44), v =>
        {
            SettingsStore.PlayerName = v;
            if (SessionContext.Instance != null) SessionContext.Instance.Net.LocalPlayerName = SettingsStore.PlayerName;
        }, 16);
        // hint spans the FULL page width (was overflowing as a one-liner)
        var hint = PixelUI.Label(p, "NameHint", "Shown to other players in the lobby and on the standings board.",
                                 16, TextAnchor.UpperLeft, PixelUI.InkDim);
        var hr = hint.rectTransform;
        hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(0, 1); hr.pivot = new Vector2(0, 1);
        hr.anchoredPosition = new Vector2(0, -RowH - 4f); hr.sizeDelta = new Vector2(_pageW, 60);
        var ht = hint.GetComponent<Text>(); if (ht != null) ht.horizontalOverflow = HorizontalWrapMode.Wrap;  // wrap, don't overflow
    }

    void BuildControls(Transform p)
    {
        string[,] binds =
        {
            { "STEER", "A / D" },
            { "ACCELERATE", "W" },
            { "BRAKE / REVERSE", "S" },
            { "DRIFT (lean + boost)", "SPACE" },
            { "RESTART SHIFT", "R" },
        };
        for (int i = 0; i < binds.GetLength(0); i++)
        {
            RowLabel(p, i, binds[i, 0]);
            var v = PixelUI.Label(p, "V" + i, binds[i, 1], 22, TextAnchor.MiddleRight, PixelUI.Gold);
            var vr = v.rectTransform;
            vr.anchorMin = new Vector2(0, 1); vr.anchorMax = new Vector2(0, 1); vr.pivot = new Vector2(0, 1);
            vr.anchoredPosition = new Vector2(LabelW + ColGap, -(i * RowH)); vr.sizeDelta = new Vector2(CtrlW, RowH - 12f);
        }
        PixelUI.Label(p, "RebindNote", "rebinding coming soon", 16, TextAnchor.UpperLeft, PixelUI.InkDim)
               .rectTransform.anchoredPosition = new Vector2(0, -(binds.GetLength(0) * RowH) - 4f);
    }

    static Vector2Int[] Resolutions()
    {
        return new[]
        {
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440),
        };
    }

    public void SetVisible(bool on) => _root.SetActive(on);
}
