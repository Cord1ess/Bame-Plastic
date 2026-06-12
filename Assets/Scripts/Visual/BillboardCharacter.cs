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
    static Material _shadowMat;   // shared sprite material with an alpha-clipped shadow caster pass

    SpriteRenderer _indicator;    // floating state dot ABOVE the head (color-codes state w/o tinting the body)
    SpriteRenderer _outline;      // selection halo BEHIND the body (shown when targeted by the conductor)

    public void SetColor(Color c) { if (sr != null) sr.color = c; }

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

    /// Selection outline — a bright halo behind the sprite, shown when the inside conductor is targeting this
    /// rider (so you can SELECT a passenger to collect from). Replaces the old "marker quad" approach.
    public void SetSelected(bool on, Color? color = null)
    {
        EnsureOutline();
        if (_outline == null) return;
        _outline.gameObject.SetActive(on);
        if (on) _outline.color = color ?? new Color(1f, 0.85f, 0.3f, 0.9f);
    }

    void EnsureIndicator()
    {
        if (_indicator != null) return;
        var go = new GameObject("StateDot");
        go.transform.SetParent(transform, false);
        // sit above the head; the sprite is 100px/unit and the body ~1.28u tall, so place near the top
        go.transform.localPosition = new Vector3(0f, (TexH / 100f) * 1.08f, 0f);
        go.transform.localScale = Vector3.one * 0.35f;
        _indicator = go.AddComponent<SpriteRenderer>();
        _indicator.sprite = DotSprite();
        _indicator.sortingOrder = (sr != null ? sr.sortingOrder : 0) + 2;
        go.AddComponent<Billboard>();     // faces the camera (upright)
        go.SetActive(false);
    }

    void EnsureOutline()
    {
        if (_outline != null) return;
        var go = new GameObject("SelectOutline");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * 1.16f;   // slightly larger than the body → reads as a halo
        _outline = go.AddComponent<SpriteRenderer>();
        _outline.sprite = sr != null ? sr.sprite : GetSprite();
        _outline.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        _outline.sortingOrder = (sr != null ? sr.sortingOrder : 0) - 1;   // BEHIND the body
        go.AddComponent<Billboard>();
        go.SetActive(false);
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

    /// Build a placeholder character at `position`. `height` is in world units (~1.8 = adult).
    public static BillboardCharacter Create(string name, Color color, float height, Vector3 position, Transform parent = null)
    {
        GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "Character" : name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite();
        sr.color = color;
        // cast a person-SHAPED shadow: a custom sprite shader with an alpha-clipped ShadowCaster pass (the default
        // Sprites material casts a solid box). Shared material — the sprite's _MainTex is per-renderer data.
        if (_shadowMat == null)
        {
            Shader sh = Shader.Find("BamePlastic/BillboardShadowSprite");
            if (sh != null) _shadowMat = new Material(sh);
        }
        if (_shadowMat != null) sr.sharedMaterial = _shadowMat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        sr.receiveShadows = false;     // billboards reading world shadows looks odd; just CAST

        go.AddComponent<Billboard>();

        BillboardCharacter bc = go.AddComponent<BillboardCharacter>();
        bc.sr = sr;
        bc.heightMeters = height;
        bc.ApplyHeight();
        return bc;
    }

    /// Re-fit the sprite to `heightMeters` in WORLD units for the CURRENT parent's scale. Chunks/bus
    /// have different scales, so call this after Create AND after any re-parent (chunk -> bus, etc.) so
    /// a person is always ~`heightMeters` tall and never inherits the chunk scale (the 144 m giants).
    public void ApplyHeight()
    {
        float unitHeight = TexH / 100f;                       // sprite is 100 px/unit -> 1.28 units tall
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
