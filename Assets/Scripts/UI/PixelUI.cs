using UnityEngine;
using UnityEngine.UI;

/// The pixel-art UI design system — the "CSS framework" every screen pulls from, so the whole game's UI is
/// consistent and crisp instead of default-grey uGUI. Zero art assets: the bordered/beveled panel and bar
/// sprites are GENERATED in code into point-filtered textures (no blur, hard pixel edges), and text uses the
/// pixel font from Resources/Fonts. Build UI with Panel()/BeveledBar()/Label()/Badge().
///
/// Pixel-crisp rules enforced here: point filtering on every generated sprite, integer borders, a hard
/// drop-shadow on text (no soft AA glow), and a tight cohesive palette.
public static class PixelUI
{
    // ---- Palette: a cohesive "dusk slate" pixel combo (clean retro, easy on the eyes — not in-your-face
    //      white). Panels are DARK slate-navy with a soft cream cut-corner frame; text is warm cream. The
    //      no-background HUD text (timer, leaderboard) is cream so it reads on the bright scene. ----
    public static readonly Color PanelFill = new Color32(0x23, 0x29, 0x3d, 0xf2); // dark slate-navy panel (slightly transparent)
    public static readonly Color PanelAlt  = new Color32(0x1a, 0x1f, 0x2e, 0xf2); // deeper slate (tracks/insets)
    public static readonly Color Frame     = new Color32(0xe8, 0xdc, 0xc0, 0xff); // soft cream cut-corner frame
    public static readonly Color FrameSoft = new Color32(0x8a, 0x83, 0x9c, 0xff); // muted frame line

    public static readonly Color Ink      = new Color32(0xf2, 0xe9, 0xd2, 0xff);  // warm cream text
    public static readonly Color InkDim   = new Color32(0x9a, 0x93, 0xab, 0xff);  // muted lavender-grey label
    public static readonly Color Gold     = new Color32(0xff, 0xc4, 0x4d, 0xff);  // taka / accent (warm gold)
    public static readonly Color Green    = new Color32(0x6c, 0xd5, 0x7e, 0xff);  // health full / go
    public static readonly Color Red      = new Color32(0xff, 0x6b, 0x5e, 0xff);  // danger / low (warm coral)
    public static readonly Color Cyan     = new Color32(0x5c, 0xc9, 0xe8, 0xff);  // rpm / info (soft sky)
    public static readonly Color Shadow   = new Color32(0x12, 0x10, 0x1c, 0x96);  // soft dark halo behind no-bg text

    // back-compat aliases (old names used by other UI)
    public static Color Text => Ink;
    public static Color TextDim => InkDim;
    public static Color EdgeHi => Frame;

    const int Border = 5;          // px frame thickness for the 9-slice panel
    const int Notch  = 5;          // px of each corner cut off (the "broken square" look)

    static Font _font;
    static Sprite _panelSprite, _panelSoftSprite, _flatSprite, _barTrackSprite;

    /// The pixel font (VCR OSD Mono), loaded once from Resources/Fonts. Falls back to the legacy font.
    public static Font PixelFont
    {
        get
        {
            if (_font == null)
            {
                _font = Resources.Load<Font>("Fonts/PixelFont");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }
    }

    // ---------- Procedural 9-slice sprites (generated once, point-filtered) ----------

    /// White panel with a dark "broken square" frame — corners are CUT OFF (notched) and a thin dark outline
    /// runs the edges. 9-sliced so the cut corners stay crisp at any size.
    public static Sprite PanelSprite => _panelSprite ??= MakeCutPanel(PanelFill);
    public static Sprite PanelSoftSprite => _panelSoftSprite ??= MakeCutPanel(PanelAlt);
    /// A flat 1px white sprite (tint it) for bar fills / solid blocks — point-filtered so edges stay hard.
    public static Sprite FlatSprite => _flatSprite ??= MakeFlat();
    /// Bar track: same cut-corner frame, dark-ish fill so a bright fill reads against it.
    public static Sprite BarTrackSprite => _barTrackSprite ??= MakeCutPanel(PanelAlt);

    // Draw a panel whose four corners are CHAMFERED (a diagonal of `Notch` px removed), with a dark frame
    // outline and a flat fill. The corner cut lives inside the 9-slice border region so it never stretches.
    static Sprite MakeCutPanel(Color fill)
    {
        int s = Border * 2 + 2;                       // border + 1px center + border
        var tex = NewTex(s, s);
        Color clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            // distance into each corner; if x+y under the notch diagonal → cut (transparent)
            int dl = x, dr = s - 1 - x, db = y, dt = s - 1 - y;
            bool cut = (dl + db < Notch) || (dr + db < Notch) || (dl + dt < Notch) || (dr + dt < Notch);
            if (cut) { tex.SetPixel(x, y, clear); continue; }

            // the outline = first ring of non-cut pixels (touching a cut or the texture edge)
            bool edge = x == 0 || y == 0 || x == s - 1 || y == s - 1
                     || (dl + db == Notch) || (dr + db == Notch) || (dl + dt == Notch) || (dr + dt == Notch);
            tex.SetPixel(x, y, edge ? Frame : fill);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
    }

    static Sprite MakeFlat()
    {
        var tex = NewTex(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
    }

    static Texture2D NewTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,        // <- the crisp-pixel rule
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelUITex"
        };
        return tex;
    }

    // ---------- Builders ----------

    /// A screen-space-overlay canvas that scales with a 1920x1080 reference. Pixel-perfect-ish at common sizes.
    public static Canvas Canvas(Transform parent, string name = "PixelCanvas", int sortOrder = 0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortOrder;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    /// A beveled pixel panel. anchor/pivot via min==max; position is anchoredPosition; size in px.
    public static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, bool soft = false)
    {
        var img = MakeImage(parent, name, soft ? PanelSoftSprite : PanelSprite);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return img;
    }

    /// A solid tinted block (flat sprite). Use for bar fills / dividers.
    public static Image Block(Transform parent, string name, Color color)
    {
        var img = MakeImage(parent, name, FlatSprite);
        img.color = color;
        return img;
    }

    /// A pixel label. `outline=true` wraps it in a crisp light halo (Outline) so dark text stays readable on
    /// the bright white scene where it has NO panel behind it (timer, leaderboard). `outline=false` uses a
    /// subtle drop shadow for text that sits on a white panel. Returns the Text for live updates.
    public static Text Label(Transform parent, string name, string content, int size, TextAnchor align, Color color, bool outline = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = PixelFont;
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        if (outline)
        {
            var ol = go.AddComponent<Outline>();      // subtle dark halo so cream text reads on any background
            ol.effectColor = Shadow;
            ol.effectDistance = new Vector2(1.5f, -1.5f);
        }
        else
        {
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.18f);
            sh.effectDistance = new Vector2(2f, -2f);
        }
        return t;
    }

    /// A bordered progress bar: sunken track + tinted fill that grows from the left. Returns the FILL rect so
    /// callers set its width each frame (set width = innerWidth * value). `innerWidth` is the usable fill span.
    public static RectTransform BeveledBar(Transform parent, string name, Vector2 anchor, Vector2 pos,
                                           Vector2 size, Color fillColor, out Image fillImg, out float innerWidth)
    {
        var track = MakeImage(parent, name, BarTrackSprite);
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 1f;
        var trt = track.rectTransform;
        trt.anchorMin = trt.anchorMax = anchor; trt.pivot = anchor;
        trt.anchoredPosition = pos; trt.sizeDelta = size;

        const float pad = Border + 1f;
        innerWidth = size.x - pad * 2f;
        fillImg = Block(track.transform, name + "Fill", fillColor);
        var frt = fillImg.rectTransform;
        frt.anchorMin = new Vector2(0, 0.5f); frt.anchorMax = new Vector2(0, 0.5f); frt.pivot = new Vector2(0, 0.5f);
        frt.anchoredPosition = new Vector2(pad, 0f);
        frt.sizeDelta = new Vector2(innerWidth, size.y - pad * 2f);
        return frt;
    }

    static Image MakeImage(Transform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = Color.white;
        return img;
    }
}
