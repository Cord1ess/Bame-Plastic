using UnityEngine;

/// Smooth chase camera for the player bus: sits behind + above at a fixed downward pitch and
/// follows the bus's position and heading with damping (good feel on turns/drifts).
///
/// FloatingOrigin-safe: this camera also carries FloatingOrigin. When the world recenters,
/// the camera AND the bus are shifted by the same offset, so the framing is preserved. The
/// position damping tracks a delta (current -> desired), which a uniform world shift doesn't
/// disturb, so there's no jump at recenter. Keep FloatingOrigin (+ its layoutGenerator) on this
/// same GameObject.
public class BusCameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                 // the Bus root

    [Header("Framing")]
    [Tooltip("Distance behind the bus.")]
    public float distance = 8f;
    [Tooltip("Height above the bus.")]
    public float height = 5f;
    [Tooltip("Fixed downward look pitch in degrees (~30 per the design).")]
    public float pitch = 30f;

    [Header("Smoothing")]
    [Tooltip("Position follow smooth time in seconds (higher = laggier/floatier).")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Heading (yaw) follow speed (higher = snappier behind-the-bus on turns).")]
    public float yawLerpSpeed = 3.5f;

    Vector3 _vel;
    float _yaw;
    bool _snapped;   // first frame snaps instantly so the camera never starts far from the bus

    void LateUpdate()
    {
        if (!target) return;

        float targetYaw = target.eulerAngles.y;

        // First frame: snap directly behind the bus with no damping. This keeps the camera near
        // the bus from frame one, so FloatingOrigin doesn't see a far-away camera at startup and
        // recenter the whole world (which would fling the bus off the road and drop it into the void).
        if (!_snapped)
        {
            _yaw = targetYaw;
            transform.position = DesiredPosition();
            transform.rotation = Quaternion.Euler(pitch, _yaw, 0f);
            _vel = Vector3.zero;
            _snapped = true;
            return;
        }

        // Smoothly chase the bus's heading so the camera swings behind it through turns/drifts.
        _yaw = Mathf.LerpAngle(_yaw, targetYaw, yawLerpSpeed * Time.deltaTime);
        transform.position = Vector3.SmoothDamp(transform.position, DesiredPosition(), ref _vel, positionSmoothTime);
        transform.rotation = Quaternion.Euler(pitch, _yaw, 0f);  // fixed ~30 deg look-down
    }

    Vector3 DesiredPosition()
    {
        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        return target.position - yawRot * Vector3.forward * distance + Vector3.up * height;
    }
}
