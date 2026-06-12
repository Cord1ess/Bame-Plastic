using UnityEngine;

/// Spawns the "Fog Ring" — a large horizon-haze mesh that wraps the camera, fading vertically and coloured by
/// the scene fog (its shadergraph reads unity_FogColor). It rises off the ground and blends into the skybox,
/// giving fake-volumetric SMOG depth on the horizon. Combined with the linear distance fog (DayNightController),
/// distant objects sink into a thick, deep haze. The ring is parented to the camera so it always centres on
/// the view; only its yaw follows (kept upright) so the haze band stays on the horizon.
///
/// Put this on the main camera (or anything); it loads + spawns the ring. PLAY-ONLY (no [ExecuteAlways]) so
/// it never creates objects in edit mode. The fog COLOUR comes from RenderSettings.fogColor (DayNightController).
public class FogRing : MonoBehaviour
{
    [Tooltip("World scale of the ring — it reads as a solid haze SKIRT along the horizon (covers the void where " +
             "the road/buildings end + the building pop-in line). The FogRing mesh is already large, so ~1.2 fits.")]
    public float ringScale = 1.2f;
    [Tooltip("Vertical offset of the ring centre relative to the camera (push DOWN so the skirt sits low and " +
             "covers below the horizon).")]
    public float heightOffset = -20f;
    [Header("Haze skirt (solid below the horizon, feathers up, fog-coloured)")]
    [Tooltip("World Y at/below which the haze is FULLY opaque — set above the road + ~building base so the " +
             "horizon void is completely covered.")]
    public float solidTopY = 18f;
    [Tooltip("Feathers from solid up to clear over this many metres above solidTopY (soft top edge of the skirt).")]
    public float topFade = 50f;
    [Tooltip("Fully clear closer than this (keeps the near street readable).")]
    public float nearClear = 35f;
    [Tooltip("Fully opaque beyond this — set ≈ the smog reach (~200) so haze + fog merge into one wall.")]
    public float farOpaque = 200f;

    GameObject _prefab;
    Transform _ring;

    void OnEnable() { EnsureRing(); }

    void EnsureRing()
    {
        if (_prefab == null) _prefab = Resources.Load<GameObject>("FogRing/FogRing");
        if (_prefab == null) return;
        if (_ring == null)
        {
            var go = Instantiate(_prefab);
            go.name = "FogRing (runtime)";
            go.hideFlags = HideFlags.DontSave;          // not serialized; rebuilt each load
            _ring = go.transform;
            ApplyHazeShader(go);
        }
    }

    Material _mat;   // runtime material instance; its _Color is driven to the live fog colour each frame

    // Swap the ring's material to the cheap haze-skirt shader, set from our knobs. Done at runtime so we don't
    // rewire the prefab/material assets; reversible (it's a runtime instance).
    void ApplyHazeShader(GameObject go)
    {
        Shader sh = Shader.Find("BamePlastic/FogRingHaze");
        if (sh == null) return;                          // shader missing → keep the prefab's material
        _mat = new Material(sh);
        _mat.SetFloat("_SolidTopY", solidTopY);
        _mat.SetFloat("_TopFade", topFade);
        _mat.SetFloat("_NearClear", nearClear);
        _mat.SetFloat("_FarOpaque", farOpaque);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = _mat;
    }

    void LateUpdate()
    {
        if (_ring == null) { EnsureRing(); if (_ring == null) return; }
        // centre on the camera, upright (no pitch/roll) so the haze band stays level on the horizon; only
        // follow the camera's yaw so it rotates with the view.
        Vector3 pos = transform.position + Vector3.up * heightOffset;
        _ring.position = pos;
        _ring.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        _ring.localScale = Vector3.one * ringScale;

        // push the skirt params + live fog colour to the material EVERY frame, so the Inspector knobs
        // (solidTopY/topFade/nearClear/farOpaque) tune LIVE in Play, and the colour tracks the atmosphere
        // (DayNightController drives RenderSettings.fogColor via fogSkyMatch: sunrise→noon→dusk→night).
        if (_mat != null)
        {
            _mat.SetColor("_Color", RenderSettings.fogColor);
            _mat.SetFloat("_SolidTopY", solidTopY);
            _mat.SetFloat("_TopFade", topFade);
            _mat.SetFloat("_NearClear", nearClear);
            _mat.SetFloat("_FarOpaque", farOpaque);
        }
    }

    void OnDestroy()
    {
        if (_ring != null) Destroy(_ring.gameObject);
        if (_mat != null) Destroy(_mat);
        _ring = null;
    }
}
