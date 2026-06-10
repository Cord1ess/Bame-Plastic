using System.Collections.Generic;
using UnityEngine;

/// Smoothly crossfades between cubemap skyboxes across the day. You give it keyframes (time-of-day 0..1 →
/// a skybox cubemap); each frame it finds the two keyframes bracketing the current time, sets them as
/// Cubemap A/B on a Skybox/CubemapBlend material, and crossfades `_Blend` between them. Driven by
/// DayNightController (which passes the same `currentTimeOfDay` that drives the sun/ambient/fog), so the
/// sky, light, and colours all move together. Also rotates the skybox so its baked sun ~aligns with the
/// scene's directional light.
[ExecuteAlways]
public class SkyboxBlender : MonoBehaviour
{
    [System.Serializable]
    public struct Key
    {
        [Range(0f, 1f)] public float time;     // time-of-day this skybox is fully shown (0=midnight,0.25=sunrise,0.5=noon,0.75=sunset)
        public Cubemap cubemap;                // assign the .exr cubemap from the skybox material's _Tex
        public string label;                   // just for inspector clarity
    }

    [Tooltip("Skyboxes around the day, in ascending time order. The cycle wraps (last → first).")]
    public List<Key> keys = new List<Key>();

    [Tooltip("Noon variants (plain / cloudy / overcast). One is picked at random each run for the noon key, " +
             "so the day's sky varies. Leave empty to just use whatever cubemap the noon key already has.")]
    public List<Cubemap> noonVariants = new List<Cubemap>();
    [Tooltip("Which key (by time) is 'noon' and gets the random variant. The key whose time is closest to this.")]
    [Range(0f, 1f)] public float noonTime = 0.5f;

    [Tooltip("Tint applied to the blended sky (matches the stock Skybox/Cubemap default).")]
    public Color tint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    [Range(0f, 8f)] public float exposure = 1.25f;
    [Tooltip("Extra Y rotation (deg) added on top of the sun-azimuth alignment.")]
    public float rotationOffset = 0f;

    Material _mat;
    static readonly int IdTexA = Shader.PropertyToID("_Tex");
    static readonly int IdTexB = Shader.PropertyToID("_Tex2");
    static readonly int IdBlend = Shader.PropertyToID("_Blend");
    static readonly int IdRot = Shader.PropertyToID("_Rotation");
    static readonly int IdTint = Shader.PropertyToID("_Tint");
    static readonly int IdExp = Shader.PropertyToID("_Exposure");

    bool _noonPicked;

    [Header("Smog depth")]
    [Tooltip("Spawn the horizon Fog Ring (fake-volumetric smog) on the camera. Pairs with the linear distance fog.")]
    public bool fogRing = true;

    void OnEnable() { EnsureMaterial(); EnsureFogRing(); }

    void Update() { EnsureFogRing(); }   // re-attach if the camera appears later (road seats it after load)

    void EnsureFogRing()
    {
        if (!fogRing || !Application.isPlaying) return;   // PLAY-ONLY: never add components in edit mode
        var cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<FogRing>() == null) cam.gameObject.AddComponent<FogRing>();
    }

    void Start()
    {
        // pick a random noon variant ONCE per play session (not in edit mode, so the preview stays stable)
        if (Application.isPlaying) PickRandomNoon();
    }

    void PickRandomNoon()
    {
        if (_noonPicked || noonVariants == null || noonVariants.Count == 0 || keys == null || keys.Count == 0) return;
        // find the key closest to noonTime
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < keys.Count; i++) { float d = Mathf.Abs(keys[i].time - noonTime); if (d < bestD) { bestD = d; best = i; } }
        var chosen = noonVariants[Random.Range(0, noonVariants.Count)];
        if (chosen != null) { var k = keys[best]; k.cubemap = chosen; keys[best] = k; }
        _noonPicked = true;
    }

    void EnsureMaterial()
    {
        if (_mat == null)
        {
            Shader sh = Shader.Find("Skybox/CubemapBlend");
            if (sh == null) { Debug.LogWarning("[SkyboxBlender] Skybox/CubemapBlend shader not found."); return; }
            _mat = new Material(sh) { name = "SkyboxBlend (runtime)" };
        }
        RenderSettings.skybox = _mat;
    }

    /// Called by DayNightController each frame. t = currentTimeOfDay (0..1), sunAzimuth = the directional
    /// light's compass Y so the skybox's baked sun lines up with the dynamic sun.
    public void Apply(float t, float sunAzimuth)
    {
        if (keys == null || keys.Count == 0) return;
        EnsureMaterial();
        if (_mat == null) return;
        t = Mathf.Repeat(t, 1f);

        // find the bracketing pair (a before t, b after t) on the wrapped timeline
        int n = keys.Count;
        int ai = 0;
        for (int i = 0; i < n; i++) if (keys[i].time <= t) ai = i; else break;
        // if t is before the first key, the "a" is the last key (wrapped from previous day)
        bool beforeFirst = t < keys[0].time;
        int aIdx = beforeFirst ? n - 1 : ai;
        int bIdx = beforeFirst ? 0 : (ai + 1) % n;

        float aTime = keys[aIdx].time + (beforeFirst ? -1f : 0f);   // unwrap so b>a
        float bTime = keys[bIdx].time + (bIdx <= aIdx && !beforeFirst ? 1f : 0f);
        float span = Mathf.Max(0.0001f, bTime - aTime);
        float blend = Mathf.Clamp01((t - aTime) / span);

        var ca = keys[aIdx].cubemap;
        var cb = keys[bIdx].cubemap;
        if (ca != null) _mat.SetTexture(IdTexA, ca);
        if (cb != null) _mat.SetTexture(IdTexB, cb);
        _mat.SetFloat(IdBlend, blend);
        _mat.SetFloat(IdRot, Mathf.Repeat(sunAzimuth + rotationOffset, 360f));
        _mat.SetColor(IdTint, tint);
        _mat.SetFloat(IdExp, exposure);
    }

    void OnDestroy()
    {
        if (_mat != null) { if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat); }
    }
}
