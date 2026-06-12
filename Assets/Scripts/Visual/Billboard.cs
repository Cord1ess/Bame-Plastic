using UnityEngine;

/// Rotates this object to face the camera each frame — the 2.5D billboard trick. Default keeps the
/// sprite UPRIGHT (yaw only), like a standing person; FaceCamera fully aligns it to the camera plane.
/// Put this on any sprite that should always face the player (passengers, conductors, crowd, props).
/// [ExecuteAlways] so menu-preview crew also face the camera in the editor (not just in play).
[ExecuteAlways]
public class Billboard : MonoBehaviour
{
    public enum Mode { UprightYAxis, FaceCamera }
    public Mode mode = Mode.UprightYAxis;

    [Tooltip("When true, the sprite TILTS with its parent (pitch/roll) and only yaw-faces the camera — used for " +
             "riders ABOARD the bus so they lean with it on turns/drifts instead of staying bolt-upright.")]
    public bool tiltWithParent = false;

    Camera _cam;

    void LateUpdate()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_cam == null) return;
        }

        if (mode == Mode.FaceCamera)
        {
            transform.forward = _cam.transform.forward;
            return;
        }

        Vector3 f = _cam.transform.forward;
        f.y = 0f;                                      // yaw toward the camera (no camera pitch/roll)
        if (f.sqrMagnitude <= 0.0001f) return;
        Quaternion yaw = Quaternion.LookRotation(f.normalized, Vector3.up);

        if (tiltWithParent && transform.parent != null)
        {
            // keep the camera-facing YAW but inherit the parent's TILT (pitch/roll) → leans with the bus.
            Vector3 up = transform.parent.up;          // the tilted "up" of the cabin/bus model
            transform.rotation = Quaternion.LookRotation(yaw * Vector3.forward, up);
        }
        else
        {
            transform.rotation = yaw;                  // standing upright (footpath/world)
        }
    }
}
