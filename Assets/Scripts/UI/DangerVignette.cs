using UnityEngine;
using UnityEngine.UI;

/// Directional danger glow on the screen edges. When the bus gets close to another vehicle (traffic or a rival),
/// the screen edge NEAREST that threat breathes a red glow — left/right/top(ahead)/bottom(behind) — brighter the
/// closer it is. Nudges the driver to slow or steer past. Local-only HUD, play-only, auto-spawned in gameplay.
public class DangerVignette : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<DangerVignette>() != null) return;
        var go = new GameObject("DangerVignette");
        go.AddComponent<DangerVignette>();
    }

    [Tooltip("Start glowing when a vehicle is within this distance (m).")]
    public float dangerRange = 9f;
    [Tooltip("Full-intensity glow at this distance (m) or closer.")]
    public float criticalRange = 3.2f;
    [Tooltip("Only consider vehicles roughly in front / beside (within this many m to the side) — ignore far-lane.")]
    public float sideLimit = 7f;
    public float breatheSpeed = 4f;
    [Tooltip("Peak opacity of the glow at full danger (lower = subtler).")]
    [Range(0f, 1f)] public float maxAlpha = 0.45f;
    public Color glowColor = new Color(1f, 0.15f, 0.1f, 1f);

    Image _left, _right, _top, _bottom;
    // smoothed per-edge intensity so the glow eases in/out instead of flickering frame-to-frame
    float _il, _ir, _it, _ib;

    void Start()
    {
        if (!Application.isPlaying) return;
        BuildUI();
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("DangerCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;                 // under the menu (50) but over gameplay HUD (10)
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.referenceResolution = new Vector2(1920, 1080);
        SceneHierarchy.Parent(canvasGo, SceneHierarchy.Category.UI);

        _left   = EdgeBar(canvas.transform, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f), GradTex(true),  0f);
        _right  = EdgeBar(canvas.transform, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), GradTex(true),  180f);
        _bottom = EdgeBar(canvas.transform, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), GradTex(false), 0f);
        _top    = EdgeBar(canvas.transform, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), GradTex(false), 180f);
    }

    // an edge-anchored strip with a gradient sprite (opaque at the screen edge → transparent inward)
    Image EdgeBar(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Sprite grad, float rotZ)
    {
        var go = new GameObject("Glow_" + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = grad; img.raycastTarget = false; img.color = new Color(1, 1, 1, 0);
        var rt = img.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        bool vertical = Mathf.Approximately(aMin.x, aMax.x);   // left/right strips
        if (vertical) { rt.sizeDelta = new Vector2(200, 0); rt.pivot = new Vector2(aMin.x, 0.5f); rt.anchoredPosition = Vector2.zero; }
        else          { rt.sizeDelta = new Vector2(0, 200); rt.pivot = new Vector2(0.5f, aMin.y); rt.anchoredPosition = Vector2.zero; }
        rt.localRotation = Quaternion.Euler(0, 0, rotZ);
        return img;
    }

    void Update()
    {
        if (_left == null) return;
        float tl = 0f, tr = 0f, tt = 0f, tb = 0f;

        var bus = BusController.Instance;
        var traffic = TrafficSystem.Instance;
        if (bus != null && traffic != null && !BusController.GamePaused)
        {
            Transform bt = bus.transform;
            var live = traffic.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var v = live[i];
                if (v == null || !v.InUse) continue;
                Vector3 d = v.transform.position - bt.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist > dangerRange || dist < 0.01f) continue;

                Vector3 local = bt.InverseTransformDirection(d);   // +z ahead, +x right
                if (Mathf.Abs(local.x) > sideLimit && Mathf.Abs(local.z) < 1f) continue;  // far to the side, not ahead

                float intensity = Mathf.Clamp01((dangerRange - dist) / Mathf.Max(0.1f, dangerRange - criticalRange));
                // pick the dominant edge from the bus-local direction
                if (Mathf.Abs(local.x) > Mathf.Abs(local.z))
                {
                    if (local.x > 0) tr = Mathf.Max(tr, intensity); else tl = Mathf.Max(tl, intensity);
                }
                else
                {
                    if (local.z > 0) tt = Mathf.Max(tt, intensity); else tb = Mathf.Max(tb, intensity);
                }
            }
        }

        // ease toward the targets (attack fast, release slower) for a smooth breathing glow
        _il = Ease(_il, tl); _ir = Ease(_ir, tr); _it = Ease(_it, tt); _ib = Ease(_ib, tb);
        // gentle breathe (small amplitude) and cap the overall opacity so it nudges rather than blinds.
        float breathe = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * breatheSpeed);
        float k = maxAlpha * breathe;
        Apply(_left, _il * k); Apply(_right, _ir * k); Apply(_top, _it * k); Apply(_bottom, _ib * k);
    }

    float Ease(float cur, float target)
    {
        float rate = target > cur ? 12f : 5f;     // light up fast, fade out gentler
        return Mathf.MoveTowards(cur, target, rate * Time.unscaledDeltaTime);
    }

    void Apply(Image img, float a)
    {
        if (img == null) return;
        var c = glowColor; c.a = Mathf.Clamp01(a);
        img.color = c;
        if (img.gameObject.activeSelf != (a > 0.01f)) img.gameObject.SetActive(a > 0.01f);
    }

    // a one-axis gradient sprite: opaque at one end → transparent at the other (the screen-edge falloff).
    static Sprite s_vert, s_horiz;
    static Sprite GradTex(bool vertical)
    {
        if (vertical && s_vert != null) return s_vert;
        if (!vertical && s_horiz != null) return s_horiz;
        int W = vertical ? 64 : 4, H = vertical ? 4 : 64;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = vertical ? (x / (float)(W - 1)) : (y / (float)(H - 1));  // 0 at edge → 1 inward
                px[y * W + x] = new Color(1, 1, 1, 1f - u);
            }
        t.SetPixels(px); t.Apply();
        var s = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
        if (vertical) s_vert = s; else s_horiz = s;
        return s;
    }
}
