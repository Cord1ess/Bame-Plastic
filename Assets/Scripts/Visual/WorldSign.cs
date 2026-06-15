using UnityEngine;

/// A camera-billboarded WORLD-SPACE callout: a bold ICON badge with a SMALL TEXT label above it. Used for the
/// bus's "passengers want off" roof sign and the police "slow down" warning. The icon reads instantly at a glance;
/// the small text clarifies it.
///
/// Build-safe by construction: the icon is a generated sprite on a SpriteRenderer (Sprites/Default material, always
/// included) and the label is a TextMesh (built-in font shader, always included). NO `Shader.Find("URP/Unlit")` —
/// that returns null in a built player and renders magenta ("turns purple in the build").
public class WorldSign : MonoBehaviour
{
    public enum Icon { Alight, Police }

    SpriteRenderer _icon;
    SpriteRenderer _badge;     // round backing behind the icon glyph
    TextMesh _text;
    Camera _cam;

    /// Build a sign under `parent`. `worldHeight` ≈ the icon badge size in metres.
    public static WorldSign Create(Transform parent, string name, Icon icon, float worldHeight = 1.4f)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var s = go.AddComponent<WorldSign>();
        s.Build(icon, worldHeight);
        return s;
    }

    void Build(Icon icon, float worldHeight)
    {
        // round badge behind the glyph
        var badgeGo = new GameObject("Badge");
        badgeGo.transform.SetParent(transform, false);
        _badge = badgeGo.AddComponent<SpriteRenderer>();
        _badge.sprite = BadgeSprite();
        _badge.color = new Color(0.06f, 0.05f, 0.09f, 0.9f);
        _badge.sortingOrder = 5000;
        badgeGo.transform.localScale = Vector3.one * worldHeight;

        // the icon glyph (alight arrow / police shield-ish exclamation)
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(transform, false);
        iconGo.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        _icon = iconGo.AddComponent<SpriteRenderer>();
        _icon.sprite = IconSprite(icon);
        _icon.sortingOrder = 5001;
        iconGo.transform.localScale = Vector3.one * worldHeight * 0.78f;

        // small text label ABOVE the badge
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, worldHeight * 0.72f, -0.02f);
        _text = textGo.AddComponent<TextMesh>();
        _text.anchor = TextAnchor.LowerCenter;
        _text.alignment = TextAlignment.Center;
        _text.fontSize = 64;
        _text.characterSize = worldHeight * 0.045f;     // SMALL relative to the icon
        _text.color = Color.white;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) { _text.font = font; var mrf = textGo.GetComponent<MeshRenderer>(); if (mrf != null) mrf.sharedMaterial = font.material; }
        var mr = textGo.GetComponent<MeshRenderer>();
        mr.sortingOrder = 5001;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
    }

    public void SetText(string s) { if (_text != null) _text.text = s; }
    /// Tint the ICON (the badge stays dark). Used to pulse / show go/stop state.
    public void SetColor(Color c) { if (_icon != null) _icon.color = c; }

    /// Billboard toward the camera (yaw only, stays upright). Call from LateUpdate.
    public void FaceCamera()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        Vector3 to = transform.position - _cam.transform.position; to.y = 0f;
        if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
    }

    // ---- generated sprites (one each, cached) ----
    static Sprite s_badge, s_alight, s_police;

    static Sprite BadgeSprite()
    {
        if (s_badge != null) return s_badge;
        const int N = 64; var t = NewTex(N);
        var px = new Color[N * N]; Vector2 c = new Vector2((N - 1) * 0.5f, (N - 1) * 0.5f);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float r = Vector2.Distance(new Vector2(x, y), c) / (N * 0.5f);
            float a = Mathf.Clamp01(1f - Mathf.Max(0f, (r - 0.9f) / 0.1f));
            px[y * N + x] = new Color(1f, 1f, 1f, a);
        }
        t.SetPixels(px); t.Apply();
        s_badge = ToSprite(t); return s_badge;
    }

    static Sprite IconSprite(Icon icon)
    {
        if (icon == Icon.Alight) { if (s_alight != null) return s_alight; s_alight = ToSprite(AlightTex()); return s_alight; }
        if (s_police != null) return s_police; s_police = ToSprite(PoliceTex()); return s_police;
    }

    // a downward ▼ "get off here" arrow on a transparent field
    static Texture2D AlightTex()
    {
        const int N = 64; var t = NewTex(N); var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float nx = Mathf.Abs(x - (N - 1) * 0.5f) / N;     // 0 centre .. ~0.5 edge
            float ny = 1f - y / (float)(N - 1);               // 0 top .. 1 bottom
            // triangle pointing DOWN: width shrinks toward the bottom tip
            bool tri = ny > 0.15f && ny < 0.78f && nx < Mathf.Lerp(0.34f, 0.02f, Mathf.InverseLerp(0.15f, 0.78f, ny));
            // a stubby stem above the arrowhead
            bool stem = ny <= 0.15f && nx < 0.12f;
            float a = (tri || stem) ? 1f : 0f;
            px[y * N + x] = new Color(1f, 1f, 1f, a);          // white → tinted by SetColor
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    // a bold "!" exclamation (police warning) on a transparent field
    static Texture2D PoliceTex()
    {
        const int N = 64; var t = NewTex(N); var px = new Color[N * N];
        float cx = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float nx = Mathf.Abs(x - cx);
            float fy = y / (float)(N - 1);                     // 0 bottom .. 1 top
            bool stem = nx < N * 0.08f && fy > 0.32f && fy < 0.86f;
            bool dot  = nx < N * 0.09f && fy > 0.12f && fy < 0.24f;
            float a = (stem || dot) ? 1f : 0f;
            px[y * N + x] = new Color(1f, 1f, 1f, a);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D NewTex(int n) => new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
    static Sprite ToSprite(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width, 0, SpriteMeshType.FullRect);
}
