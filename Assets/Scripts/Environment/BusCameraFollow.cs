using UnityEngine;

/// Smooth chase camera for the player bus: sits behind + above at a fixed downward pitch and
/// follows the bus's position and heading with damping (good feel on turns/drifts).
///
/// FloatingOrigin-safe: this camera also carries FloatingOrigin. When the world recenters,
/// the camera AND the bus are shifted by the same offset, so the framing is preserved. The
/// position damping tracks a delta (current -> desired), which a uniform world shift doesn't
/// disturb, so there's no jump at recenter. Keep FloatingOrigin (+ its layoutGenerator) on this
/// same GameObject.
[ExecuteAlways]
public class BusCameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                 // the Bus root

    [Header("Framing")]
    [Tooltip("Distance behind the bus.")]
    public float distance = 12.5f;
    [Tooltip("Height above the bus.")]
    public float height = 8f;
    [Tooltip("Fixed downward look pitch in degrees.")]
    public float pitch = 26f;

    [Header("Smoothing")]
    [Tooltip("Position follow smooth time in seconds (higher = laggier/floatier).")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Heading (yaw) follow speed (higher = snappier behind-the-bus on turns).")]
    public float yawLerpSpeed = 3.5f;

    [Header("Juice")]
    [Tooltip("Extra FOV added at top speed (sense of speed).")]
    public float fovKick = 12f;
    public float fovLerp = 4f;
    [Tooltip("Shake kick on a full-speed impact.")]
    public float impactShake = 0.6f;
    public float shakeDecay = 3f;

    Vector3 _vel;
    float _yaw;
    bool _snapped;   // first frame snaps instantly so the camera never starts far from the bus
    Transform _yawSource;  // if set, take heading from this instead of the target (e.g. the bus while following the billboard conductor)
    Camera _cam;
    float _baseFov;
    float _shake;

    void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam != null) _baseFov = _cam.fieldOfView;
    }

    void OnEnable() { BusController.Impacted += OnImpact; }
    void OnDisable() { BusController.Impacted -= OnImpact; }
    void OnImpact(float severity) { _shake += severity * impactShake; }

    /// Swap who the camera follows (and optionally where its heading comes from). Used by RoleController
    /// to switch between the bus (driving) and the conductor — a billboard with no meaningful heading.
    public void Retarget(Transform newTarget, Transform yawSource, float newDistance, float newHeight, float newPitch)
    {
        target = newTarget;
        _yawSource = yawSource;
        distance = newDistance;
        height = newHeight;
        pitch = newPitch;
        _snapped = false;   // re-snap to the new target next frame
    }

    void LateUpdate()
    {
        if (!target)
        {
            // In edit mode there's no RoleController to assign the target, so auto-find the bus so the
            // Scene/Game view frames it before Play (no more empty-space-until-Play).
            if (!Application.isPlaying)
            {
                var bc = BusController.Instance != null ? BusController.Instance : FindAnyObjectByType<BusController>();
                if (bc != null) target = bc.transform;
            }
            if (!target) return;
        }

        float targetYaw = (_yawSource != null ? _yawSource : target).eulerAngles.y;

        // EDIT MODE: just snap to frame the bus every update (no damping/juice — those need play timing).
        if (!Application.isPlaying)
        {
            _yaw = targetYaw;
            transform.position = DesiredPosition();
            transform.rotation = Quaternion.Euler(pitch, _yaw, 0f);
            return;
        }

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

        ApplyJuice();
    }

    // FOV widens with speed; camera shakes on impacts and (subtly) while drifting.
    void ApplyJuice()
    {
        BusController bus = BusController.Instance;
        float speedN = bus != null ? bus.SpeedNormalized : 0f;

        if (_cam != null)
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _baseFov + speedN * fovKick, fovLerp * Time.deltaTime);

        // Shake only on impacts now (no drift shake).
        _shake = Mathf.MoveTowards(_shake, 0f, shakeDecay * Time.deltaTime);
        if (_shake > 0.0001f)
        {
            Vector3 off = Random.insideUnitSphere * _shake;
            transform.position += off;
            transform.rotation *= Quaternion.Euler(off.y * 8f, off.x * 8f, 0f);
        }
    }

    Vector3 DesiredPosition()
    {
        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        return target.position - yawRot * Vector3.forward * distance + Vector3.up * height;
    }

    /// Where the chase camera wants to be RIGHT NOW (used by MenuMode to ease into the chase on play start).
    public void GetChasePose(out Vector3 pos, out Quaternion rot)
    {
        if (target != null) _yaw = (_yawSource != null ? _yawSource : target).eulerAngles.y;
        pos = target != null ? DesiredPosition() : transform.position;
        rot = Quaternion.Euler(pitch, _yaw, 0f);
    }
}
