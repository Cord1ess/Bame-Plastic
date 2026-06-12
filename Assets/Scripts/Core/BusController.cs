using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

/// Arcade sphere-physics controller tuned for the bus (not a kart). A detached, interpolated Rigidbody
/// sphere does the physics; this transform follows its smoothed position (offset down by the sphere
/// radius so the root = the ground-contact point) and yaws with steering.
///
/// Feel: heavy pickup (low accelRate), strong brakes (high brakeRate), steering that only works while
/// moving and eases at top speed, an automatic gearbox (exposed for a speedometer), and a weighty
/// slide-drift that leans the body hard with only a little yaw angle.
public class BusController : MonoBehaviour
{
    public static BusController Instance;

    /// Raised on a hazard hit, with severity 0..1 (how much speed was lost). For camera shake / FX.
    public static System.Action<float> Impacted;

    [Header("Rig (wired by Build Clean Bus Rig)")]
    [FormerlySerializedAs("kartModel")] public Transform busModel;
    [FormerlySerializedAs("kartNormal")] public Transform busNormal;
    public Rigidbody sphere;
    public Transform frontWheels;
    public Transform backWheels;

    [Header("Collision box (full-bus trigger for traffic — separate from the sphere mover)")]
    [Tooltip("Size of the bus collision box (local m). The small sphere drives; THIS box is what traffic " +
             "collides with. Auto-created on the bus root carrying a BusTag. 0 = auto-fit from the model bounds.")]
    public Vector3 collisionBoxSize = Vector3.zero;
    [Tooltip("Local centre of the collision box (relative to the bus root, which sits at ground contact).")]
    public Vector3 collisionBoxCenter = new Vector3(0f, 1.6f, 0f);

    [Header("State")]
    [Tooltip("When false (a conductor is active) the bus ignores input and coasts.")]
    public bool controlEnabled = true;
    public bool drifting;

    /// Shared pause flag (set by PauseManager). When true the driver ignores throttle/steer and coasts to a stop;
    /// MP-safe because it's just suppressed input (no Time.timeScale freeze that would also stall the network).
    public static bool GamePaused;

    [Header("Drive — heavy pickup, strong brakes")]
    public float maxSpeed = 22f;
    public float maxReverseSpeed = 6f;

    [Header("Conductor-1 speed gate")]
    [Tooltip("While the outside conductor is OFF the bus, top speed is capped to this (km/h) so the road can't " +
             "leave him behind. Full speed returns when he boards.")]
    public float boardingSpeedCapKmh = 10f;
    [Tooltip("Is the outside conductor (C1) aboard? Set by RoleController (solo) or GameNet/driver (multiplayer). " +
             "When false the bus crawls at boardingSpeedCapKmh.")]
    public bool conductor1Aboard = true;
    [Tooltip("How fast speed builds. Lower = heavier.")]
    public float accelRate = 16f;
    [Tooltip("Braking deceleration. High = sharp stops for dodging.")]
    public float brakeRate = 32f;
    [Tooltip("Engine-braking when you let off the throttle.")]
    public float coastRate = 6f;

    [Header("Auto gearbox (drives the speedometer)")]
    public int gearCount = 5;
    [Tooltip("Seconds the throttle eases during an upshift (gives the climb-shift-climb feel).")]
    public float shiftTime = 0.18f;

    [Header("Steering — only while moving")]
    [Tooltip("Yaw rate (deg/s) for a NORMAL turn — the gentle rate you get the moment you press A/D.")]
    public float turnSpeed = 24f;
    [Tooltip("Yaw rate (deg/s) for a SHARP turn — reached after holding A/D for sharpHoldTime (the bus " +
             "'commits' to a hard turn the longer you hold).")]
    public float sharpTurnSpeed = 44f;
    [Tooltip("Seconds of HOLDING the same direction before the turn ramps from normal up to sharp.")]
    public float sharpHoldTime = 1.2f;
    [Tooltip("How fast the steering input RAMPS toward the key you're holding (units/s). Lower = heavier, " +
             "smoother wheel — the bus eases into the turn instead of snapping. This is the main 'finer " +
             "control' knob.")]
    public float steerRamp = 4f;
    [Tooltip("How fast the steering input RETURNS to centre when you let go (units/s). Usually a bit quicker " +
             "than the ramp so it straightens up naturally.")]
    public float steerReturn = 5.5f;
    [Tooltip("Speed at which you get full steering authority (below it, steering fades to none).")]
    public float fullSteerSpeed = 7f;
    [Range(0f, 1f)] [Tooltip("Fraction of steering kept at top speed (lower = more stable).")]
    public float highSpeedSteer = 0.55f;

    [Header("Grip / Drift (Spacebar)")]
    [Tooltip("How hard the bus snaps to its heading normally (higher = grippier).")]
    public float grip = 14f;
    [Tooltip("Grip while drifting (lower = slides more).")]
    public float driftGrip = 2.5f;
    [Tooltip("How much A/D still turns the bus WHILE drifting. <1 = turns LESS than normal (1=same, 0.5=half, " +
             "0=heading locked straight). This is what was over-rotating you on Space — keep it well below 1.")]
    [Range(0f, 2f)] [FormerlySerializedAs("driftTurnBoost")]
    public float driftTurnScale = 1.2f;
    [Tooltip("Drift turn scale after holding Space + A/D for driftSharpHoldTime — a sustained drift tightens " +
             "its arc (like the A/D sharp-turn ramp). Should be > driftTurnScale.")]
    [Range(0f, 2f)] public float driftTurnScaleSharp = 1.6f;
    [Tooltip("Seconds of holding the drift (Space + same direction) before the arc ramps from driftTurnScale " +
             "up to driftTurnScaleSharp.")]
    public float driftSharpHoldTime = 2f;
    public float driftMinSpeed = 6f;
    [Tooltip("Visual YAW (Y) lean of the body into the slide (deg). 0 = no body spin on Space — only the roll.")]
    public float driftAngle = 0f;
    [Tooltip("BIG body ROLL (Z tilt) while holding Space — THE effect of Space (the bus heaves onto its springs).")]
    public float driftLean = 22f;
    [Tooltip("How fast the drift lean settles in when you press Space. Higher = the drift reads quicker " +
             "(more responsive), but still eased — not an instant snap. Normal cornering stays slow (~6).")]
    public float driftLeanSpeed = 14f;
    [Tooltip("Body ROLL (Z tilt) into NORMAL A/D corners (deg). THIS is the tilt you want from steering — " +
             "very subtle now — the dramatic lean is reserved for drift.")]
    public float cornerLean = 3f;
    [Tooltip("Visual YAW (Y) lean of the body into a normal corner (deg). Keep ~0 — you want tilt, not body spin.")]
    public float cornerAngle = 0f;
    [Tooltip("Speed multiplier applied to top speed while holding Space (a small surge, so there's a reason " +
             "to use it). 1.12 = +12%.")]
    public float driftBoost = 1.12f;

    [Header("Physics")]
    public float gravity = 20f;
    public LayerMask layerMask;

    [Header("Wheels")]
    [Tooltip("1 = physically correct roll from the auto-measured radius. Negate to flip spin direction.")]
    public float wheelSpinScale = 1f;
    public float frontSteerAngle = 22f;

    // --- Exposed for the speedometer / HUD ---
    public int Gear => _gear + 1;                  // 1-based
    public float Rpm01 { get; private set; }       // 0..1 within the current gear (sawtooth; drops on shift)
    public float SpeedKmh => Mathf.Abs(currentSpeed) * 3.6f;
    public float SpeedMps => currentSpeed;          // signed forward speed in m/s (+ forward, - reverse)
    public float SpeedNormalized => Mathf.Clamp01(Mathf.Abs(currentSpeed) / Mathf.Max(0.01f, maxSpeed));
    public bool Shifting => _shiftTimer > 0f;

    float currentSpeed;
    float _yaw;
    float _steer;                  // SMOOTHED steer input (ramps toward the raw key — the heavy-wheel feel)
    float _steerHold;              // seconds the SAME steer direction has been held (drives the sharp-turn ramp)
    int _steerHoldSign;            // -1/0/+1 — which direction the current hold is in (0 = not steering)
    float _steerAssist;            // gentle steer pull toward the DriverGuide line (set per-frame)
    int _driftDir = 1;
    int _gear;
    float _shiftTimer;
    float _wheelSpin;
    float _vLean, _vAngle;
    float _sphereRadius = 0.85f;
    float _wheelRadius = 0.5f;

    void Start()
    {
        Instance = this;
        sphere.transform.parent = null;

        SphereCollider sc = sphere.GetComponent<SphereCollider>();
        if (sc != null) _sphereRadius = sc.radius * Mathf.Abs(sphere.transform.lossyScale.x);

        _yaw = transform.eulerAngles.y;
        _wheelRadius = MeasureWheelRadius();

        // Use the hand-tuned "CollisionBox" child if it exists (the intended workflow: create + size it in
        // the editor via Bame Plastic ▸ Bus ▸ Add Collision Box, then save the prefab). Only build one as a
        // LAST-RESORT fallback so the game never silently ships without a bus collider.
        if (transform.Find("CollisionBox") == null)
            BuildCollisionBox(transform, busModel, collisionBoxSize, collisionBoxCenter);
    }

    /// Create the full-bus BOX TRIGGER child ("CollisionBox") on `root` so traffic collides with the whole
    /// bus, not the little physics sphere. Carries a BusTag (traffic detects the bus by it) + a kinematic
    /// Rigidbody (so trigger events fire). Public + static so the editor tool can build it as a PERSISTENT,
    /// hand-editable scene object. Auto-fits from the model bounds when size is zero. Returns the box GO.
    public static GameObject BuildCollisionBox(Transform root, Transform model, Vector3 size, Vector3 center)
    {
        Transform existing = root.Find("CollisionBox");
        if (existing != null) return existing.gameObject;

        if (size == Vector3.zero && model != null)
        {
            Bounds b = LocalModelBounds(root, model);
            if (b.size != Vector3.zero) { size = b.size; center = b.center; }
        }
        if (size == Vector3.zero) size = new Vector3(2.4f, 3.0f, 9.0f);   // sensible bus default

        var go = new GameObject("CollisionBox");
        go.transform.SetParent(root, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.identity;
        go.AddComponent<BusTag>();
        var bc = go.AddComponent<BoxCollider>();
        bc.size = size;
        bc.isTrigger = true;                       // detection only — the sphere does the physics
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;                     // needed so trigger events fire; doesn't move on its own
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;
        return go;
    }

    static Bounds LocalModelBounds(Transform root, Transform model)
    {
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds w = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) w.Encapsulate(rends[i].bounds);
        // convert world bounds to root's local space (axis-aligned approximation — fine for a box)
        Vector3 c = root.InverseTransformPoint(w.center);
        Vector3 s = root.InverseTransformVector(w.size);
        return new Bounds(c, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
    }

    /// Place the bus (and its detached physics sphere) at a pose and kill momentum. Safe to call after
    /// Start() — used by the road generator to drop the bus onto the spawn point on the left lane.
    public void TeleportTo(Vector3 groundPos, Quaternion rotation)
    {
        _yaw = rotation.eulerAngles.y;
        currentSpeed = 0f;
        transform.SetPositionAndRotation(groundPos, Quaternion.Euler(0f, _yaw, 0f));
        if (sphere != null)
        {
            sphere.transform.position = groundPos + Vector3.up * _sphereRadius;
            sphere.linearVelocity = Vector3.zero;
            sphere.angularVelocity = Vector3.zero;
        }
    }


    // ---- multiplayer PROXY mode: on conductor clients the bus is NOT simulated locally; it's driven from the
    //      driver's interpolated BusState. In proxy mode Update() skips all physics/driving and just holds the
    //      pose GameNet sets each frame via ProxySetPose. ----
    public bool ProxyMode { get; private set; }
    public void SetProxyMode(bool on)
    {
        ProxyMode = on;
        controlEnabled = controlEnabled && !on;
        if (on && sphere != null) { sphere.linearVelocity = Vector3.zero; sphere.angularVelocity = Vector3.zero; }
    }
    /// Apply a remote pose to the proxy bus (position is the ground point; yaw degrees). No physics.
    public void ProxySetPose(Vector3 groundPos, float yawDeg, float speedMps)
    {
        _yaw = yawDeg;
        currentSpeed = speedMps;
        transform.SetPositionAndRotation(groundPos, Quaternion.Euler(0f, yawDeg, 0f));
        if (sphere != null) sphere.transform.position = groundPos + Vector3.up * _sphereRadius;
    }

    void Update()
    {
        if (ProxyMode) return;   // proxy bus: pose comes from GameNet (driver's interpolated state), not physics

        // Follow the sphere's INTERPOLATED position (smooth — no per-frame jitter).
        transform.position = sphere.transform.position - Vector3.up * _sphereRadius;

        GameInput gi = GameInput.Instance;
        bool canDrive = controlEnabled && !GamePaused;   // paused → no input, bus brakes to a halt below
        float rawSteer = canDrive ? gi.steer.ReadValue<float>() : 0f;
        // DriverGuide adds a gentle pull toward the guide line (set each frame, consumed here).
        float steerTarget = Mathf.Clamp(rawSteer + _steerAssist, -1f, 1f);
        _steerAssist = 0f;

        // Smooth the input into _steer for a HEAVY, fine wheel — the bus eases into/out of the turn instead
        // of snapping. Returns to centre a touch quicker than it ramps so it straightens up naturally.
        float toward = Mathf.Abs(steerTarget) < Mathf.Abs(_steer) ? steerReturn : steerRamp;
        _steer = Mathf.MoveTowards(_steer, steerTarget, toward * Time.deltaTime);
        float steerVal = _steer;

        // Track how long the SAME direction is held: a sustained hold means the player WANTS a sharp turn,
        // so the yaw rate ramps from turnSpeed up to sharpTurnSpeed over sharpHoldTime. A short flick stays
        // gentle. The hold resets the moment the key is released or the direction flips.
        int steerSign = Mathf.Abs(rawSteer) > 0.1f ? (int)Mathf.Sign(rawSteer) : 0;
        if (steerSign != 0 && steerSign == _steerHoldSign) _steerHold += Time.deltaTime;
        else _steerHold = 0f;
        _steerHoldSign = steerSign;

        bool accel = canDrive && gi.accelerate.IsPressed();
        bool brake = (canDrive && gi.brake.IsPressed()) || GamePaused;   // paused → hold the brake (coast to stop)
        bool driftDown = canDrive && gi.drift.WasPressedThisFrame();
        bool driftUp = canDrive && gi.drift.WasReleasedThisFrame();

        float dt = Time.deltaTime;
        float topSpeed = maxSpeed * HealthFactor() * (drifting ? driftBoost : 1f);   // Space gives a small surge
        // SPEED GATE: while the outside conductor (C1) is OFF the bus, crawl at <= boardingSpeedCapKmh so the
        // procedural road can't leave him behind. Lifts the moment he boards. (conductor1Aboard is set by
        // RoleController in solo, or by GameNet/driver-authority in multiplayer.)
        if (!conductor1Aboard) topSpeed = Mathf.Min(topSpeed, boardingSpeedCapKmh / 3.6f);

        // --- Gearbox (also feeds the speedometer). PROGRESSIVE ratios like a real box: 1st covers a small
        //     speed band and revs out fast; each higher gear is taller (wider band) and pulls softer. The
        //     gear's speed band = [GearStart(g) .. GearStart(g+1)]; Rpm01 climbs across it then a shift drops
        //     revs to the bottom of the next band → the natural climb-shift-climb cadence. ---
        float absSpeed = Mathf.Abs(currentSpeed);
        int newGear = GearAtSpeed(absSpeed);
        if (accel && newGear > _gear) _shiftTimer = shiftTime;   // upshift → brief throttle cut (revs drop)
        _gear = newGear;
        float gLo = GearStartSpeed(_gear);
        float gHi = GearStartSpeed(_gear + 1);
        Rpm01 = Mathf.Clamp01((absSpeed - gLo) / Mathf.Max(0.01f, gHi - gLo));

        // --- Throttle: torque is STRONG just after a shift (low revs) and TAPERS to redline, and each higher
        //     gear pulls softer (taller ratio). Shift = a brief cut so you feel the gap, then it re-pulls. ---
        if (accel)
        {
            if (_shiftTimer > 0f) { _shiftTimer -= dt; }         // mid-shift: throttle cut, coast briefly
            else
            {
                float inGearTorque = Mathf.Lerp(1.3f, 0.45f, Rpm01);          // hard low-rev pull → soft redline
                float gearTall = Mathf.Lerp(1.25f, 0.55f, _gear / Mathf.Max(1f, gearCount - 1f)); // taller = softer
                currentSpeed = Mathf.MoveTowards(currentSpeed, topSpeed, accelRate * inGearTorque * gearTall * dt);
            }
        }
        else if (brake)
        {
            if (currentSpeed > 0.2f) currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, brakeRate * dt);
            else currentSpeed = Mathf.MoveTowards(currentSpeed, -maxReverseSpeed, accelRate * 0.7f * dt);
        }
        else currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, coastRate * dt);

        // --- Drift (hold Space + a direction) — leans hard onto Z + small surge. REQUIRES A/D: pressing
        //     Space alone does nothing. Uses RAW steer so it triggers instantly. Ends if you let go of the
        //     steering (or Space, or stop). While drifting the heading turns LESS, not more (driftTurnScale).
        if (driftDown && Mathf.Abs(currentSpeed) > driftMinSpeed && Mathf.Abs(rawSteer) > 0.1f)
        {
            drifting = true;
            _driftDir = rawSteer > 0f ? 1 : -1;
        }
        if (driftUp || Mathf.Abs(currentSpeed) < 1f || Mathf.Abs(rawSteer) < 0.1f) drifting = false;

        // --- Steering: none at a standstill, full once moving, eased at top speed, inverted in reverse ---
        float authority = Mathf.InverseLerp(0.3f, fullSteerSpeed, Mathf.Abs(currentSpeed));
        float stability = Mathf.Lerp(1f, highSpeedSteer, Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed));
        float reverseSign = currentSpeed < -0.2f ? -1f : 1f;
        // progressive yaw: gentle at first, ramps to sharp the longer the SAME direction is held.
        float holdT = sharpHoldTime > 0.01f ? Mathf.Clamp01(_steerHold / sharpHoldTime) : 1f;
        float yawRate = Mathf.Lerp(turnSpeed, sharpTurnSpeed, holdT);
        // While drifting use the BASE turnSpeed (not the A/D sharp ramp) so the drift's tightening comes
        // purely from its own hold ramp below — the two ramps don't compound into an over-spin.
        float effRate = drifting ? turnSpeed : yawRate;
        float turn = steerVal * authority * stability * effRate * reverseSign;
        if (drifting)
        {
            // drift turns LESS than a normal turn, but a SUSTAINED drift (Space + same dir held) tightens its
            // arc from driftTurnScale up to driftTurnScaleSharp — same idea as the A/D sharp-turn ramp.
            float driftHoldT = driftSharpHoldTime > 0.01f ? Mathf.Clamp01(_steerHold / driftSharpHoldTime) : 1f;
            turn *= Mathf.Lerp(driftTurnScale, driftTurnScaleSharp, driftHoldT);
        }
        _yaw += turn * dt;
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        // --- Visual TILT: the body leans (rolls) into turns. Space = a BIG lean; normal A/D = a SUBTLE lean.
        //     The drift lean settles FASTER (driftLeanSpeed) so the drift READS the instant you hit Space —
        //     responsive but still eased (not an instant snap). Normal cornering stays slow/weighty.
        float speedRatio = SpeedNormalized;
        float targetAngle = drifting ? _driftDir * driftAngle : steerVal * speedRatio * cornerAngle;
        float targetLean  = drifting ? -_driftDir * driftLean  : -steerVal * speedRatio * cornerLean;
        float leanSpeed = drifting ? driftLeanSpeed : 6f;
        _vAngle = Mathf.Lerp(_vAngle, targetAngle, dt * leanSpeed);
        _vLean  = Mathf.Lerp(_vLean,  targetLean,  dt * leanSpeed);
        if (busModel != null) busModel.localRotation = Quaternion.Euler(0f, _vAngle, _vLean);

        // --- Wheels: roll at the physically correct rate from the bus's ACTUAL forward speed + the
        // auto-measured wheel radius (no tuning); fronts also steer with the input. ---
        float rollSpeed = Vector3.Dot(sphere.linearVelocity, transform.forward);
        _wheelSpin += rollSpeed / _wheelRadius * Mathf.Rad2Deg * wheelSpinScale * dt;
        if (frontWheels != null) frontWheels.localRotation = Quaternion.Euler(_wheelSpin, steerVal * frontSteerAngle, 0f);
        if (backWheels != null) backWheels.localRotation = Quaternion.Euler(_wheelSpin, 0f, 0f);
    }

    void FixedUpdate()
    {
        Vector3 v = sphere.linearVelocity;

        // Ground handling via VELOCITY (no per-frame position teleport → no jitter). Gravity only airborne.
        if (FindGroundY(out float gy))
        {
            float targetY = gy + _sphereRadius;
            float dy = targetY - sphere.position.y;
            if (dy > _sphereRadius)                       // severe penetration backstop (rare)
            {
                Vector3 p = sphere.position; p.y = targetY; sphere.position = p;
                v.y = 0f;
            }
            else
            {
                v.y = Mathf.Clamp(dy / Time.fixedDeltaTime, -8f, 12f);   // hold on the surface smoothly
            }
        }
        else
        {
            v.y -= gravity * Time.fixedDeltaTime;
        }

        // Drive + grip: forward velocity tracks currentSpeed; sideways velocity bleeds off (less drifting).
        Vector3 fwd = transform.forward;
        Vector3 flat = new Vector3(v.x, 0f, v.z);
        Vector3 lateral = flat - fwd * Vector3.Dot(flat, fwd);
        float g = drifting ? driftGrip : grip;
        lateral = Vector3.Lerp(lateral, Vector3.zero, Mathf.Clamp01(g * Time.fixedDeltaTime));
        Vector3 result = fwd * currentSpeed + lateral;
        sphere.linearVelocity = new Vector3(result.x, v.y, result.z);

        // Slope tilt for the visual, then re-apply heading.
        if (busNormal != null)
        {
            Vector3 targetUp = Vector3.up;
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, layerMask))
                targetUp = hit.normal;
            busNormal.up = Vector3.Lerp(busNormal.up, targetUp, Time.deltaTime * 8f);
            busNormal.Rotate(0f, _yaw, 0f);
        }
    }

    /// DriverGuide calls this each frame with a signed [-1,1] steer bias toward the guide line. It's added
    /// to the player's steering this frame (a gentle assist), then auto-cleared — call every frame to hold.
    public void ApplySteerAssist(float bias) => _steerAssist = Mathf.Clamp(bias, -1f, 1f);

    /// Hazards/obstacles call this to shed speed on contact. 1 = no effect, 0 = dead stop.
    public void ApplyImpact(float speedMultiplier)
    {
        float severity = 1f - Mathf.Clamp01(speedMultiplier);
        currentSpeed *= Mathf.Clamp01(speedMultiplier);
        drifting = false;
        Impacted?.Invoke(severity);
    }

    /// Physically KNOCK the bus back — used when it rams a SOLID obstacle (a rival bus) so it bounces off
    /// instead of passing through. Pushes the physics sphere in `worldDir` (away from the obstacle). `force` is
    /// the bounce-back speed (m/s). `hard` = a real impact (kills drive speed + fires the camera shake once);
    /// the continuous frame-by-frame PRESS while leaning on a rival passes hard=false → it keeps shoving the bus
    /// without zeroing its speed every frame (feels like leaning on a real bus, not glued to a wall) and DOESN'T
    /// spam the camera shake.
    public void Knockback(Vector3 worldDir, float force) => Knockback(worldDir, force, true);
    public void Knockback(Vector3 worldDir, float force, bool hard)
    {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 1e-5f) return;
        worldDir.Normalize();
        if (sphere != null)
        {
            Vector3 v = sphere.linearVelocity;
            float into = Vector3.Dot(v, -worldDir);        // how fast we're driving INTO the obstacle
            if (into > 0f) v += worldDir * into;           // cancel that component
            v += worldDir * force;                          // then bounce away
            sphere.linearVelocity = new Vector3(v.x, sphere.linearVelocity.y, v.z);
        }
        currentSpeed *= hard ? 0.2f : 0.86f;               // hard hit kills drive speed; a press just bleeds it
        drifting = false;
        if (hard) Impacted?.Invoke(0.8f);                  // shake ONCE on a real impact, not every press frame
    }

    // PROGRESSIVE gear ratios: speed band widths grow geometrically across the gears (1st is short and revs
    // out quickly; top gear is tall and spans the most speed) — like a real gearbox, not even slices. The
    // cumulative band edge for gear g (0-based) as a fraction of maxSpeed, scaled so the last edge = maxSpeed.
    float GearStartSpeed(int g)
    {
        int n = Mathf.Max(1, gearCount);
        const float ratio = 1.35f;                 // each gear ~35% taller than the previous
        float cum = 0f, span = 1f, total = 0f;
        for (int i = 0; i < n; i++) { total += span; span *= ratio; }   // sum of all band widths
        span = 1f;
        for (int i = 0; i < Mathf.Clamp(g, 0, n); i++) { cum += span; span *= ratio; }
        return maxSpeed * (cum / total);
    }

    int GearAtSpeed(float absSpeed)
    {
        int n = Mathf.Max(1, gearCount);
        for (int g = 0; g < n; g++)
            if (absSpeed < GearStartSpeed(g + 1)) return g;
        return n - 1;
    }

    // A damaged bus is sluggish (lower top speed): 1.0 at full health down to 0.4 at zero.
    float HealthFactor()
    {
        ShiftManager sm = ShiftManager.Instance;
        if (sm == null || sm.maxHealth <= 0f) return 1f;
        return Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(sm.Health / sm.maxHealth));
    }

    // Auto-measure the wheel radius from the rigged wheel meshes so the roll rate matches the bus's real
    // speed with no tuning. (Diameter ≈ the wheel's vertical/forward extent.)
    float MeasureWheelRadius()
    {
        Transform w = (frontWheels != null && frontWheels.childCount > 0) ? frontWheels
                    : (backWheels != null && backWheels.childCount > 0) ? backWheels : null;
        if (w == null) return 0.5f;
        Renderer[] rs = w.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 0.5f;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return Mathf.Max(0.1f, Mathf.Max(b.size.y, b.size.z) * 0.5f);
    }

    static readonly RaycastHit[] _groundHits = new RaycastHit[8];

    // Highest solid surface below the sphere that isn't the bus. Returns true when the sphere is on/near
    // it (grounded); false when airborne. Layer-independent + ignores triggers, so a thin road collider
    // is fine and the bus can never sink through.
    bool FindGroundY(out float groundY)
    {
        groundY = 0f;
        Vector3 origin = sphere.position + Vector3.up * (_sphereRadius + 1.5f);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, _sphereRadius + 8f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float topY = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = _groundHits[i].collider;
            if (c == null) continue;
            if (_groundHits[i].rigidbody == sphere) continue;
            if (c.GetComponentInParent<BusController>() != null) continue;
            if (_groundHits[i].point.y > topY) { topY = _groundHits[i].point.y; found = true; }
        }
        if (!found) return false;
        groundY = topY;
        return sphere.position.y <= topY + _sphereRadius + 0.3f;
    }
}
