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
    [Tooltip("World scale of the ring — sits just inside the smog far distance so it reads as a solid haze " +
             "wall (hides the sky/horizon, incl. through gaps between buildings).")]
    public float ringScale = 175f;
    [Tooltip("Vertical offset of the ring centre relative to the camera (push down so the band sits low).")]
    public float heightOffset = -12f;

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
        }
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
    }

    void OnDestroy()
    {
        if (_ring != null) Destroy(_ring.gameObject);
        _ring = null;
    }
}
