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

    public void SetColor(Color c) { if (sr != null) sr.color = c; }

    /// Build a placeholder character at `position`. `height` is in world units (~1.8 = adult).
    public static BillboardCharacter Create(string name, Color color, float height, Vector3 position, Transform parent = null)
    {
        GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "Character" : name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite();
        sr.color = color;

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
