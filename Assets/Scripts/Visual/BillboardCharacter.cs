using UnityEngine;

/// A placeholder "person" drawn as a tinted billboard sprite — a simple head+body silhouette
/// generated in code (no art assets needed). Stand-in for passengers, conductors, and crowd.
/// It already uses the real 2.5D billboard-sprite pipeline, so swapping in real art later is just
/// changing the sprite. Recolour any instance at runtime with SetColor() (used to show state).
[RequireComponent(typeof(SpriteRenderer))]
public class BillboardCharacter : MonoBehaviour
{
    public SpriteRenderer sr;
    public float heightMeters = 1.8f;

    const int TexW = 64, TexH = 128;
    static Sprite _sharedSprite;
    static Sprite _dotSprite;     // round dot for the overhead state indicator
    static Material _shadowMat;   // the custom person-shadow material (null if its shader was stripped from a build)
    static Material _fallbackMat; // Sprites/Default — always available, never magenta (build-safe fallback)

    /// The material every billboard sprite uses. Prefers the custom shadow shader; falls back to Sprites/Default
    /// when that shader isn't in the build (Shader.Find → null), so a billboard is NEVER the magenta error material.
    static Material ShadowSpriteMaterial()
    {
        if (_shadowMat == null && !_shadowTried)
        {
            _shadowTried = true;
            Shader sh = Shader.Find("BamePlastic/BillboardShadowSprite");
            if (sh != null && sh.isSupported) _shadowMat = new Material(sh);
        }
        if (_shadowMat != null) return _shadowMat;
        if (_fallbackMat == null)
        {
            Shader sd = Shader.Find("Sprites/Default");   // built-in, always included → never null/magenta
            _fallbackMat = sd != null ? new Material(sd) : null;
        }
        return _fallbackMat;
    }
    static bool _shadowTried;

    SpriteRenderer _indicator;    // floating state dot ABOVE the head (color-codes state w/o tinting the body)
    SpriteRenderer _selectArrow;   // bobbing down-arrow above the head when this rider is selected
    float _selectArrowBaseY;
    float _selectPulseSeed;

    // ---- walk animation (real sprite art) ----
    Sprite[] _walkFrames;         // the active animation (null = static sprite, no animation)
    Sprite _idleFrame;            // shown when not moving (frame 0 of the walk, or a dedicated idle pose)
    float _frameTime = 0.09f;     // seconds per walk frame
    float _frameTimer;
    int _frame;
    Vector3 _lastPos;             // to detect movement (auto play/pause the walk + face direction)
    bool _haveLastPos;
    bool _autoAnimate = true;     // drive the walk from frame-to-frame movement
    bool _isRealSprite;           // true once real art is assigned (don't recolour the body then)

    /// Tint — only meaningful for the procedural placeholder. With real art we leave the sprite untinted (white)
    /// so the artwork shows as drawn; state is conveyed by the overhead dot, not the body colour.
    public void SetColor(Color c) { if (sr != null && !_isRealSprite) { _baseColor = c; } }

    // The sprite's intended base colour (white for real art, the tint for placeholders). The on-screen colour is
    // this FADED toward the scene fog by distance + dimmed by night, applied each frame in LateUpdate — so far
    // characters melt into the smog like the road does, instead of staying sharp + fully-lit at any distance.
    Color _baseColor = Color.white;
    DayNightController _dn;
    float _dnRetry;

    /// State indicator floating above the head — color-codes state (heading/paid/owes/etc.) WITHOUT recolouring
    /// the body, so real character art can drop in unchanged. Pass alpha 0 (or use HideIndicator) to hide it.
    public void SetIndicator(Color c)
    {
        EnsureIndicator();
        if (_indicator == null) return;
        _indicator.gameObject.SetActive(c.a > 0.01f);
        _indicator.color = c;
    }
    public void HideIndicator() { if (_indicator != null) _indicator.gameObject.SetActive(false); }

    /// Selection marker — a big, bright, BOBBING DOWN-ARROW floating above the head, pointing at the rider the
    /// inside conductor is targeting. Always drawn in front (high sort order) so it's unmistakable, regardless of
    /// the body sprite's shape (the old silhouette-rim hid behind the opaque body and didn't read).
    public void SetSelected(bool on, Color? color = null)
    {
        EnsureSelectArrow();
        if (_selectArrow == null) return;
        _selectArrow.gameObject.SetActive(on);
        if (on) _selectArrow.color = color ?? new Color(1f, 0.95f, 0.2f, 1f);
    }

    void EnsureIndicator()
    {
        if (_indicator != null) return;
        var go = new GameObject("StateDot");
        go.transform.SetParent(transform, false);
        // sit just ABOVE the head — use the ACTUAL sprite's local top (real art ≈4.8u, placeholder 1.28u).
        float top = (sr != null && sr.sprite != null) ? sr.sprite.bounds.max.y : (TexH / 100f);
        go.transform.localPosition = new Vector3(0f, top * 1.06f, 0f);
        // scale the dot relative to the sprite so it's a consistent on-screen size on any sprite height
        go.transform.localScale = Vector3.one * Mathf.Max(0.35f, top * 0.22f);
        _indicator = go.AddComponent<SpriteRenderer>();
        _indicator.sprite = DotSprite();
        _indicator.sortingOrder = (sr != null ? sr.sortingOrder : 0) + 2;
        go.AddComponent<Billboard>();     // faces the camera (upright)
        go.SetActive(false);
    }

    void EnsureSelectArrow()
    {
        if (_selectArrow != null) return;
        var go = new GameObject("SelectArrow");
        go.transform.SetParent(transform, false);
        // float just ABOVE the head — small, so it points without covering the rider.
        float top = (sr != null && sr.sprite != null) ? sr.sprite.bounds.max.y : (TexH / 100f);
        _selectArrowBaseY = top * 1.18f;
        go.transform.localPosition = new Vector3(0f, _selectArrowBaseY, 0f);
        go.transform.localScale = Vector3.one * Mathf.Max(0.18f, top * 0.2f);
        _selectArrow = go.AddComponent<SpriteRenderer>();
        _selectArrow.sprite = ArrowSprite();
        _selectArrow.color = new Color(1f, 0.95f, 0.2f, 1f);
        _selectArrow.sortingOrder = (sr != null ? sr.sortingOrder : 0) + 50;   // ALWAYS in front
        go.AddComponent<Billboard>();
        _selectPulseSeed = Random.value * 10f;
        go.SetActive(false);
    }

    // a fat downward-pointing arrow/chevron sprite (generated once) for the selection marker.
    static Sprite _arrowSprite;
    static Sprite ArrowSprite()
    {
        if (_arrowSprite != null) return _arrowSprite;
        int N = 48;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float nx = Mathf.Abs(x - (N - 1) * 0.5f) / N;   // 0 centre .. ~0.5 edge
                float ny = 1f - y / (float)(N - 1);             // 0 top .. 1 bottom (tip)
                // downward triangle: full width at top, narrowing to a point at the bottom
                bool tri = ny > 0.18f && nx < Mathf.Lerp(0.42f, 0.0f, Mathf.InverseLerp(0.18f, 0.95f, ny));
                px[y * N + x] = new Color(1f, 1f, 1f, tri ? 1f : 0f);
            }
        tex.SetPixels(px); tex.Apply();
        _arrowSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
        return _arrowSprite;
    }

    // a soft round dot sprite (generated once) for the overhead indicator
    static Sprite DotSprite()
    {
        if (_dotSprite != null) return _dotSprite;
        int s = 32; var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        float cx = (s - 1) * 0.5f, r = s * 0.46f;
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
            px[y * s + x] = new Color(1, 1, 1, d <= r ? 1f : 0f);
        }
        tex.SetPixels(px); tex.Apply();
        _dotSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return _dotSprite;
    }

    /// ABOARD: lean with the bus (tilt with the cabin) vs. stand upright (footpath/world). Toggled by Passenger
    /// on board/alight so riders tilt on turns instead of poking out the side.
    public void SetTiltWithParent(bool on)
    {
        var b = GetComponent<Billboard>();
        if (b != null) b.tiltWithParent = on;
    }

    // ===================== REAL SPRITE ART =====================

    /// Show a single static real sprite (poses / idle). Stops any walk animation.
    public void SetSprite(Sprite s)
    {
        if (s == null || sr == null) return;
        _walkFrames = null; _idleFrame = s; _isRealSprite = true; _baseColor = Color.white;
        sr.sprite = s; sr.color = Color.white;
        ApplyHeight();
    }

    /// Assign a WALK CYCLE (frames). It auto-plays while the character MOVES and rests on frame 0 when still
    /// (set autoAnimate=false to always play). `secondsPerFrame` tunes speed. Front-facing art → flips by dir.
    public void SetWalk(Sprite[] frames, float secondsPerFrame = 0.09f, bool autoAnimate = true)
    {
        if (frames == null || frames.Length == 0 || sr == null) return;
        _walkFrames = frames; _idleFrame = frames[0]; _frameTime = secondsPerFrame; _autoAnimate = autoAnimate;
        _isRealSprite = true; _frame = 0; _frameTimer = 0f; _baseColor = Color.white;
        sr.sprite = frames[0]; sr.color = Color.white;
        ApplyHeight();
    }

    /// Face left/right by flipping X. Pass the world travel direction; +X / camera-right → not flipped.
    public void FaceDirection(Vector3 worldDir)
    {
        if (sr == null) return;
        if (Mathf.Abs(worldDir.x) < 0.01f && Mathf.Abs(worldDir.z) < 0.01f) return;
        // use the screen-right test against the camera so "left/right" matches what the player sees
        var cam = Camera.main;
        float side = cam != null ? Vector3.Dot(worldDir, cam.transform.right) : worldDir.x;
        if (Mathf.Abs(side) > 0.05f) sr.flipX = side < 0f;
    }

    // Apply scene fog + night dimming to the sprite each frame so far characters fade into the smog and darken at
    // night — instead of staying sharp + fully-bright at any distance. Runs for ALL billboards (walking or static).
    void LateUpdate()
    {
        if (sr == null) return;
        Color c = _baseColor;
        // distance fade toward the fog colour (linear, matching RenderSettings used by the road)
        if (RenderSettings.fog)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, transform.position);
                float fs = RenderSettings.fogStartDistance, fe = RenderSettings.fogEndDistance;
                float f = fe > fs ? Mathf.Clamp01((dist - fs) / (fe - fs)) : 0f;
                c = Color.Lerp(c, RenderSettings.fogColor, f);
            }
        }
        // night dimming (cache the controller; retry occasionally if not yet present)
        if (_dn == null && Time.time >= _dnRetry) { _dn = FindAnyObjectByType<DayNightController>(); _dnRetry = Time.time + 2f; }
        if (_dn != null)
        {
            float dim = 1f - 0.55f * _dn.Darkness;   // never fully black — keep silhouettes readable at night
            c.r *= dim; c.g *= dim; c.b *= dim;
        }
        sr.color = c;

        // bob + pulse the selection arrow so it grabs the eye.
        if (_selectArrow != null && _selectArrow.gameObject.activeSelf)
        {
            float ph = (Time.unscaledTime + _selectPulseSeed);
            float bob = Mathf.Sin(ph * 4f) * (_selectArrowBaseY * 0.06f);
            var lp = _selectArrow.transform.localPosition; lp.y = _selectArrowBaseY + bob; _selectArrow.transform.localPosition = lp;
            float pulse = 0.75f + 0.25f * Mathf.Sin(ph * 8f);
            var col = _selectArrow.color; col.a = pulse; _selectArrow.color = col;
        }
    }

    void Update()
    {
        if (_walkFrames == null || sr == null) return;

        // detect movement (world-space) to auto play/pause + face direction
        Vector3 p = transform.position;
        bool moving = true;
        if (_autoAnimate)
        {
            if (_haveLastPos)
            {
                Vector3 d = p - _lastPos; d.y = 0f;
                moving = d.sqrMagnitude > 1e-5f;
                if (moving) FaceDirection(d);
            }
            _lastPos = p; _haveLastPos = true;
        }

        if (!moving) { sr.sprite = _idleFrame; return; }   // rest on frame 0 when standing still

        _frameTimer += Time.deltaTime;
        if (_frameTimer >= _frameTime)
        {
            _frameTimer -= _frameTime;
            _frame = (_frame + 1) % _walkFrames.Length;
            sr.sprite = _walkFrames[_frame];
        }
    }

    /// Build a placeholder character at `position`. `height` is in world units (~1.8 = adult).
    public static BillboardCharacter Create(string name, Color color, float height, Vector3 position, Transform parent = null)
    {
        GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "Character" : name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite();
        sr.color = color;
        // (note: bc._baseColor is seeded from `color` below, after the component is added)
        // Material: prefer the custom person-shaped-shadow shader; BUT it can be STRIPPED FROM A BUILD (Shader.Find
        // returns null → the SpriteRenderer would show the magenta error material = the "purple around sprites"
        // bug). So fall back to the ALWAYS-INCLUDED Sprites/Default material when the custom shader isn't present,
        // and only enable shadow casting when we actually have the shadow shader.
        sr.sharedMaterial = ShadowSpriteMaterial();
        bool hasShadowShader = _shadowMat != null;
        sr.shadowCastingMode = hasShadowShader ? UnityEngine.Rendering.ShadowCastingMode.On
                                               : UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;     // billboards reading world shadows looks odd; just CAST

        go.AddComponent<Billboard>();

        BillboardCharacter bc = go.AddComponent<BillboardCharacter>();
        bc.sr = sr;
        bc._baseColor = color;
        bc.heightMeters = height;
        bc.ApplyHeight();
        return bc;
    }

    /// Re-fit the sprite to `heightMeters` in WORLD units for the CURRENT parent's scale. Chunks/bus
    /// have different scales, so call this after Create AND after any re-parent (chunk -> bus, etc.) so
    /// a person is always ~`heightMeters` tall and never inherits the chunk scale (the 144 m giants).
    public void ApplyHeight()
    {
        // use the ACTUAL sprite's unit height (real art is 240×480 @100ppu ≈ 4.8u; the placeholder is 1.28u).
        float unitHeight = (sr != null && sr.sprite != null) ? sr.sprite.bounds.size.y : (TexH / 100f);
        if (unitHeight < 1e-4f) unitHeight = TexH / 100f;
        float worldScale = heightMeters / unitHeight;
        Vector3 p = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(worldScale / NonZero(p.x), worldScale / NonZero(p.y), worldScale / NonZero(p.z));
    }

    static float NonZero(float v) => Mathf.Abs(v) < 1e-6f ? 1f : v;

    // One shared silhouette texture for every character (generated once).
    static Sprite GetSprite()
    {
        if (_sharedSprite != null) return _sharedSprite;

        Texture2D tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] px = new Color[TexW * TexH];
        Color clear = new Color(1f, 1f, 1f, 0f);
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        // Body: a slightly tapered rectangle (wider at the shoulders, narrower at the feet).
        for (int y = 0; y < 88; y++)
        {
            float tt = y / 88f;
            int halfW = Mathf.RoundToInt(Mathf.Lerp(13f, 18f, tt));
            for (int x = 32 - halfW; x < 32 + halfW; x++) px[y * TexW + x] = Color.white;
        }
        // Head: filled circle above the body.
        int cx = 32, cy = 104, r = 17;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= TexW || y < 0 || y >= TexH) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r * r) px[y * TexW + x] = Color.white;
            }

        tex.SetPixels(px);
        tex.Apply();

        _sharedSprite = Sprite.Create(tex, new Rect(0, 0, TexW, TexH), new Vector2(0.5f, 0f), 100f);
        _sharedSprite.name = "PlaceholderCharacter";
        return _sharedSprite;
    }
}
