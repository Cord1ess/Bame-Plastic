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
    [Tooltip("Distance behind the bus (LIVE value — driven between idle/fast by speed; also set by Retarget for the conductor view).")]
    public float distance = 11f;
    [Tooltip("Height above the bus (LIVE value).")]
    public float height = 6f;
    [Tooltip("Fixed downward look pitch in degrees (LIVE value).")]
    public float pitch = 20f;

    [Header("Driver speed framing (eases between these by BUS speed)")]
    [Tooltip("Idle framing — held while stopped/slow (and what the start animates INTO).")]
    public float idleDistance = 11f;
    public float idleHeight = 6f;
    public float idlePitch = 20f;
    [Tooltip("Top-speed framing — eased to as the bus speeds up (pulls back + up, slightly flatter).")]
    public float fastDistance = 16f;
    public float fastHeight = 9f;
    public float fastPitch = 18f;

    [Header("Conductor-1 speed framing (eases by C1 RUN speed)")]
    [Tooltip("C1 stopped/slow framing (while OFF the bus, standing).")]
    public Vector3 c1IdleCam = new Vector3(3.6f, 3.6f, 23f);   // (distance, height, pitch)
    [Tooltip("C1 running framing (pulls back + up, slightly flatter).")]
    public Vector3 c1FastCam = new Vector3(5f, 4.5f, 21f);
    [Tooltip("C1 framing while ON the bus door — a cleaner angle that doesn't cut into the bus (he's a real " +
             "character here). Eased to whenever C1 is aboard.")]
    public Vector3 c1OnBusCam = new Vector3(5.5f, 4f, 18f);

    [Tooltip("How quickly the framing eases between idle and fast (higher = snappier). Quick but smooth.")]
    public float framingLerpSpeed = 6f;

    [Header("Smoothing")]
    [Tooltip("Position follow smooth time in seconds (higher = laggier/floatier).")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Heading (yaw) follow speed (higher = snappier behind-the-bus on turns).")]
    public float yawLerpSpeed = 3.5f;

    [Header("Juice")]
    [Tooltip("Extra FOV added at top speed (sense of speed).")]
    public float fovKick = 12f;
    public float fovLerp = 4f;
    [Tooltip("Shake kick on a full-speed impact (TINY — a non-stacking nudge, not a screen-wrecker).")]
    public float impactShake = 0.09f;
    [Tooltip("Hard cap on shake so repeated/continuous hits can NEVER stack into a nasty wobble.")]
    public float maxShake = 0.12f;
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
    // NON-STACKING: take the MAX (not +=) and hard-cap, so continuous rival clashes / rapid hits can't pile up
    // into a nasty escalating wobble — it stays a small, consistent nudge that decays away.
    void OnImpact(float severity) { _shake = Mathf.Min(maxShake, Mathf.Max(_shake, Mathf.Clamp01(severity) * impactShake)); }

    /// Swap who the camera follows (and optionally where its heading comes from). Used by RoleController
    /// to switch between the bus (driving) and the conductor — a billboard with no meaningful heading.
    public void Retarget(Transform newTarget, Transform yawSource, float newDistance, float newHeight, float newPitch)
    {
        target = newTarget;
        _yawSource = yawSource;
        distance = newDistance;
        height = newHeight;
        pitch = newPitch;
        _framing = Framing.Fixed;   // a Retarget (e.g. conductor-2 view) holds the given fixed framing
        _snapped = false;           // re-snap to the new target next frame
    }

    enum Framing { Fixed, Driver, Conductor1 }
    Framing _framing = Framing.Driver;   // default driving view is speed-framed
    Conductor _c1;                       // C1 ref for its run-speed (set by UseConductor1Framing)

    /// DRIVER speed framing: eases idle↔fast by BUS speed. RoleController calls this for the driver view.
    public void UseSpeedFraming() { _framing = Framing.Driver; _snapped = false; }

    /// CONDUCTOR-1 speed framing: eases c1Idle↔c1Fast by C1's RUN speed. RoleController calls this for the C1 view.
    public void UseConductor1Framing(Conductor c1) { _framing = Framing.Conductor1; _c1 = c1; _snapped = false; }

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

        // SPEED FRAMING: ease distance/height/pitch between an idle (stopped/slow) and fast (moving) framing by
        // the relevant speed — quick but smooth. DRIVER eases by bus speed; CONDUCTOR-1 by its own run speed.
        // Fixed framing (conductor-2 / Retarget) is left untouched.
        if (_framing != Framing.Fixed)
        {
            Vector3 idle, fast; float sp;
            if (_framing == Framing.Conductor1)
            {
                // ON the bus door → use the clean on-bus angle (he's a real character there, don't cut into the
                // bus). OFF the bus (running around) → ease idle↔fast by his run speed.
                if (_c1 != null && _c1.OnBus) { idle = c1OnBusCam; fast = c1OnBusCam; sp = 0f; }
                else { idle = c1IdleCam; fast = c1FastCam; sp = MeasureTargetSpeed01(_c1 != null ? _c1.moveSpeed : 5f); }
            }
            else // Driver
            {
                idle = new Vector3(idleDistance, idleHeight, idlePitch);
                fast = new Vector3(fastDistance, fastHeight, fastPitch);
                BusController b = BusController.Instance;
                sp = b != null ? Mathf.Clamp01(b.SpeedNormalized) : 0f;
            }
            float t = framingLerpSpeed * Time.deltaTime;
            distance = Mathf.Lerp(distance, Mathf.Lerp(idle.x, fast.x, sp), t);
            height   = Mathf.Lerp(height,   Mathf.Lerp(idle.y, fast.y, sp), t);
            pitch    = Mathf.Lerp(pitch,    Mathf.Lerp(idle.z, fast.z, sp), t);
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
        float shakeScale = SettingsStore.CameraShake;   // player setting (0 = off) scales all shake live
        if (_shake * shakeScale > 0.0001f)
        {
            Vector3 off = Random.insideUnitSphere * (_shake * shakeScale);
            transform.position += off;
            transform.rotation *= Quaternion.Euler(off.y * 3f, off.x * 3f, 0f);   // gentle rot (was 8 → nauseating)
        }
    }

    // C1 has no speed accessor (it just moves its transform), so measure the followed target's planar speed from
    // its frame-to-frame displacement, normalized 0..1 against a reference (its moveSpeed). Smoothed so the
    // framing doesn't twitch on per-frame jitter.
    Vector3 _lastTargetPos; bool _haveLastPos; float _spdSmooth;
    float MeasureTargetSpeed01(float refSpeed)
    {
        if (target == null) return 0f;
        Vector3 p = target.position; p.y = 0f;
        float raw = 0f;
        if (_haveLastPos && Time.deltaTime > 1e-5f)
        {
            Vector3 d = p - _lastTargetPos; d.y = 0f;
            raw = Mathf.Clamp01(d.magnitude / Time.deltaTime / Mathf.Max(0.1f, refSpeed));
        }
        _lastTargetPos = p; _haveLastPos = true;
        _spdSmooth = Mathf.Lerp(_spdSmooth, raw, 8f * Time.deltaTime);
        return _spdSmooth;
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
