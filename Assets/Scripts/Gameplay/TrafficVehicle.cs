using System.Collections.Generic;
using UnityEngine;

/// One pooled traffic agent (car / rickshaw / bus) on the endless road.
///
/// LOGICAL vs PHYSICAL split:
///   • LOGICAL state — `metresFromBus` (signed position along the road relative to the bus) + `lateral`
///     (offset from centre) + `speed` + `dir` (travel direction). Road-relative, deterministic,
///     floating-origin-invariant. The AI drives THIS.
///   • PHYSICAL — the transform; in L1/L2 it snaps to the logical road point (follows curves). L3 will let
///     it DETACH on a hit for real knockback, then spring back.
///
/// L2 AVOIDANCE (this layer): each vehicle senses the vehicles + the bus ahead of it in its lane and
///   (a) brakes to keep a follow gap (down to a stop if boxed), and
///   (b) steers laterally to squeeze around a slower/blocking obstacle — Dhaka "barely-scratching" weaving.
/// All sensing reads only deterministic logical state, so it stays identical across multiplayer clients.
public class TrafficVehicle : MonoBehaviour
{
    public enum Kind { Rickshaw, Car, Bus }

    /// Optional behaviour override. After the vehicle's normal avoidance decides a desired speed/lateral,
    /// a brain (e.g. a rival bus that wants to camp a stop) may rewrite them — using the SAME road-relative
    /// state, so the vehicle still drives/curves/collides/clamps exactly like ambient traffic. This is how
    /// rivals integrate seamlessly: they ARE traffic vehicles, just with extra intent.
    public interface IVehicleBrain
    {
        void Decide(TrafficVehicle self, float dt, ref float desiredSpeed, ref float desiredLateral);
    }
    public IVehicleBrain Brain;

    /// The road this vehicle rides — exposed so a Brain can sample stops / bands in the same space.
    public TiledRoadStreamer Road => _road;

    [Header("Identity")]
    public Kind kind;
    public int id;                 // stable id for collision/sync events later

    // --- LOGICAL road-relative state (deterministic) ---
    public float metresFromBus;    // signed: + ahead of the bus (toward the road's leading edge), - behind
    public float lateral;          // metres right of centre (- = left / forward-your-side under LHT)
    public float speed;            // CURRENT speed magnitude (m/s) along its travel direction
    public int dir = +1;           // +1 = same direction as the bus, -1 = oncoming/wrong-way

    public bool InUse { get; private set; }
    public bool Knocked => _shoveT > 0f;   // mid-shove → skip the anti-stack separation pass
    public float HalfLen => _halfLen;
    public float HalfWidth => _halfWidth;

    /// Deterministic soft nudge of the logical position (used by TrafficSystem's anti-stack pass). Lateral
    /// is clamped to the drivable band so a nudge never pushes a vehicle off the road.
    public void Nudge(float dMetres, float dLateral)
    {
        metresFromBus += dMetres;
        lateral = ClampToBand(lateral + dLateral);
    }

    // --- per-vehicle tuning, set on Acquire by kind ---
    float _cruise;                 // desired clear-road speed
    float _halfLen;                // for follow-distance (gap measured edge-to-edge-ish)
    float _halfWidth;              // for lateral clearance
    float _accel = 6f;             // m/s^2 speeding up
    float _brake = 12f;            // m/s^2 slowing
    float _steerRate = 4f;         // m/s lateral steer toward desired offset

    float _desiredLateral;         // target lateral the steering eases toward

    [Header("Collision (L3)")]
    [Tooltip("Speed kept by the BUS after hitting this vehicle (0 = dead stop). Big vehicles slow you more.")]
    public float busSpeedAfterHit = 0.6f;
    [Tooltip("Bus health lost when the bus hits this vehicle.")]
    public int busDamageOnHit = 8;
    [Tooltip("Enable this vehicle's collider only when within this many metres of the bus (cheap physics).")]
    public float colliderActiveRange = 35f;
    [Tooltip("How hard the shove pushes the vehicle sideways when the bus hits it (metres of lateral pop).")]
    public float shoveLateral = 2.2f;
    [Tooltip("Small backward shove along the road on a hit (metres).")]
    public float shoveBack = 1.2f;
    [Tooltip("Height of the little hop on a hit (metres).")]
    public float shoveHop = 0.6f;
    [Tooltip("Seconds the shove takes to settle.")]
    public float shoveTime = 0.35f;
    public float hitCooldown = 0.8f;

    Transform _model;
    BoxCollider _collider;         // box trigger, active only near the bus
    Rigidbody _body;               // kinematic (never goes dynamic — shove is animated in road-space)
    TiledRoadStreamer _road;

    // --- shove state: a quick jumpy push in the hit direction, animated in ROAD space so it can never
    //     fall through the ground (it's an offset on top of the road point, not a free-falling body) ---
    float _shoveT;                 // 0..1 hop-arc progress (the position displacement is permanent, not this)
    float _lastHitTime = -999f;

    // Tunables shared across the system, set once.
    public float followGap = 6f;       // min clear gap ahead before hard-braking (m, plus half-lengths)
    public float lookahead = 30f;      // how far ahead to sense (m) — bigger = reacts earlier/smarter
    public float scratchClearance = 0.4f;  // extra lateral room needed to commit to a squeeze (small = aggressive)
    float eagerness = 0.5f;            // 0..1 how hard it surges into open road (set by kind on Acquire)

    public void Init(TiledRoadStreamer road)
    {
        _road = road;
        EnsureModel();
        InUse = false;
        gameObject.SetActive(false);
    }

    public void Acquire(Kind k, int vehicleId, float metres, float lat, float spd, int direction, Vector3 size, Color color)
    {
        kind = k; id = vehicleId;
        metresFromBus = metres; lateral = lat; _desiredLateral = lat; dir = direction;
        _cruise = spd; speed = spd;
        Brain = null;                            // pooled reuse starts brainless (rivals re-attach after Acquire)
        _shoveT = 0f; _lastHitTime = -999f;     // reset shove state for the pooled reuse
        _halfLen = size.z * 0.5f;
        _halfWidth = size.x * 0.5f;
        // rickshaws are nimble + pushy (steer quick, surge into gaps); buses ponderous + calmer
        switch (k)
        {
            case Kind.Rickshaw: _accel = 6f;   _brake = 9f;  _steerRate = 5f;   eagerness = 0.8f; break;
            case Kind.Bus:      _accel = 3.5f; _brake = 9f;  _steerRate = 2.2f; eagerness = 0.3f; break;
            default:            _accel = 7f;   _brake = 12f; _steerRate = 3.5f; eagerness = 0.6f; break;
        }
        InUse = true;
        gameObject.SetActive(true);
        StyleModel(size, color);
        SyncToRoad();
    }

    public void Release()
    {
        InUse = false;
        gameObject.SetActive(false);
    }

    /// Tick: L2 avoidance (sense + brake + steer) + L3 collider activation and knockback. busSpeed/
    /// busLateral describe the bus as an obstacle at metresFromBus = 0. Returns false when off the live road.
    public bool Tick(List<TrafficVehicle> all, float busSpeed, float busLateral, float dt)
    {
        // enable the collision box only near the bus (cheap physics — distant traffic has no collider)
        bool near = Mathf.Abs(metresFromBus) < colliderActiveRange;
        if (_collider != null && _collider.enabled != near) _collider.enabled = near;

        // advance the shove animation (decaying pop + hop), applied as an offset in SyncToRoad
        if (_shoveT > 0f) _shoveT = Mathf.Max(0f, _shoveT - dt / Mathf.Max(0.05f, shoveTime));

        // Drive normally even while being shoved — the shove is a quick decaying OFFSET on top, not a
        // separate physics state. (No tumble, no free-fall: it can't clip the ground because it rides the
        // road point.) A hit briefly stuns the speed so it doesn't just plough on.
        Sense(all, busSpeed, busLateral, out float desiredSpeed, out float desiredLat);

        // brain override (rivals): rewrite intent AFTER avoidance, so collision-avoidance is the baseline and
        // the rival's plan (camp / steer to kerb / leave) layers on top. Lateral is still band-clamped below.
        if (Brain != null) Brain.Decide(this, dt, ref desiredSpeed, ref desiredLat);

        if (_shoveT > 0f) desiredSpeed = Mathf.Min(desiredSpeed, speed * 0.4f);   // hit stun

        float rate = desiredSpeed < speed ? _brake : _accel;
        speed = Mathf.MoveTowards(speed, desiredSpeed, rate * dt);

        _desiredLateral = ClampToBand(desiredLat);
        lateral = Mathf.MoveTowards(lateral, _desiredLateral, _steerRate * dt);
        lateral = ClampToBand(lateral);

        float relative = dir * speed - busSpeed;
        metresFromBus += relative * dt;

        return SyncToRoad();
    }

    // Decide desired speed + desired lateral from what's ahead in my lane.
    void Sense(List<TrafficVehicle> all, float busSpeed, float busLateral, out float desiredSpeed, out float desiredLat)
    {
        desiredSpeed = _cruise;
        desiredLat = lateral;          // hold lane by default

        // find the NEAREST obstacle ahead of me, in my lateral band, going my way (or stationary like the bus
        // relative to my lane). "ahead" along my travel direction: (other.metres - my.metres) * dir > 0.
        float bestGap = float.MaxValue;
        float bestOtherSpeedAlongMe = 0f;
        bool blocked = false;

        // other vehicles
        for (int i = 0; i < all.Count; i++)
        {
            TrafficVehicle o = all[i];
            if (o == this || !o.InUse) continue;
            if (!ConsiderObstacle(o.metresFromBus, o.lateral, out float gap)) continue;
            if (gap < bestGap)
            {
                bestGap = gap;
                // o's speed projected onto MY travel direction (same dir = +o.speed; opposite = -o.speed)
                bestOtherSpeedAlongMe = (o.dir == dir) ? o.speed : -o.speed;
                blocked = true;
            }
        }

        // the bus, as an obstacle at metres = 0, lateral = busLateral, moving the bus's way (dir +1)
        if (ConsiderObstacle(0f, busLateral, out float busGap) && busGap < bestGap)
        {
            bestGap = busGap;
            bestOtherSpeedAlongMe = busSpeed;   // bus travels dir+1; project onto mine below via dir compare
            if (dir != +1) bestOtherSpeedAlongMe = -busSpeed;
            blocked = true;
        }

        // CLEAR ROAD → push a bit ABOVE cruise (gun it / claim open road), more when very open. Dhaka
        // traffic surges into space rather than coasting at a polite constant.
        if (!blocked) { desiredSpeed = _cruise * (1f + 0.35f * eagerness); return; }

        // --- longitudinal: brake to keep the follow gap; match the leader's along-speed as we close ---
        float stopGap = followGap;                              // edge gap we refuse to go below
        if (bestGap <= stopGap)
            desiredSpeed = Mathf.Max(0f, bestOtherSpeedAlongMe); // ride the leader's speed (don't reverse)
        else
        {
            // blend cruise -> leader speed as the gap closes from lookahead down to stopGap
            float tt = Mathf.InverseLerp(lookahead, stopGap, bestGap);   // 0 far .. 1 at stopGap
            desiredSpeed = Mathf.Lerp(_cruise, Mathf.Max(0f, bestOtherSpeedAlongMe), tt);
        }

        // --- lateral: steer around the blocker toward the side with MORE open road ahead. Only start
        // committing once we're actually closing on it (gap below ~2/3 lookahead) so vehicles don't weave
        // pointlessly far out. If neither side is clear, hold lane and brake (boxed in).
        if (bestGap > lookahead * 0.66f) { desiredLat = lateral; return; }

        float step = _halfWidth + scratchClearance + 0.3f;
        float left = lateral - step;     // toward -X
        float rightO = lateral + step;   // toward +X
        bool leftClear = InBand(left) && LateralClear(all, left, busLateral);
        bool rightClear = InBand(rightO) && LateralClear(all, rightO, busLateral);

        float leftOpen = leftClear ? SideOpenness(all, left, busLateral) : -1f;
        float rightOpen = rightClear ? SideOpenness(all, rightO, busLateral) : -1f;

        if (leftOpen >= 0f || rightOpen >= 0f)
        {
            desiredLat = (leftOpen >= rightOpen) ? left : rightO;
            // COMMIT to the gap: gun it back toward cruise (claim the opening) instead of crawling behind
            // the blocker. Scaled by how open the chosen side is, so a real gap → real acceleration.
            float open = Mathf.Max(leftOpen, rightOpen);
            float openFrac = Mathf.Clamp01(open / lookahead);
            desiredSpeed = Mathf.Max(desiredSpeed, _cruise * (0.6f + 0.4f * openFrac) * (1f + 0.35f * eagerness));
        }
        // else: boxed in → hold lane, brake (handled above)
    }

    // How much clear road is ahead at lateral offset `lat` (bigger = more open → prefer steering there).
    float SideOpenness(List<TrafficVehicle> all, float lat, float busLateral)
    {
        float nearest = lookahead;
        for (int i = 0; i < all.Count; i++)
        {
            TrafficVehicle o = all[i];
            if (o == this || !o.InUse || o.dir != dir) continue;
            if (Mathf.Abs(o.lateral - lat) > (_halfWidth + o._halfWidth)) continue;
            float along = (o.metresFromBus - metresFromBus) * dir;
            if (along > 0f && along < nearest) nearest = along;
        }
        // the bus too
        if (Mathf.Abs(busLateral - lat) <= (_halfWidth + 1.2f))
        {
            float along = (0f - metresFromBus) * dir;
            if (along > 0f && along < nearest) nearest = along;
        }
        return nearest;
    }

    // Is an obstacle at (m, lat) ahead of me, in my lateral band, within lookahead? Outputs the longitudinal
    // gap (edge-to-edge-ish). Ignores oncoming traffic in ITS OWN lane (only blocks if it strayed into mine).
    bool ConsiderObstacle(float m, float lat, out float gap)
    {
        gap = 0f;
        float along = (m - metresFromBus) * dir;            // >0 = ahead of me along my travel
        if (along <= 0f) return false;                       // behind or beside-but-not-ahead
        if (Mathf.Abs(lat - lateral) > (_halfWidth + 1.0f)) return false;   // not in my lateral band
        gap = along - _halfLen;                              // rough edge gap
        return gap < lookahead;
    }

    // Is lateral offset `lat` clear of other vehicles near my longitudinal position (so I can steer there)?
    bool LateralClear(List<TrafficVehicle> all, float lat, float busLateral)
    {
        float need = _halfWidth + scratchClearance;
        // bus
        if (Mathf.Abs(busLateral - lat) < (need + 1.2f) && Mathf.Abs(0f - metresFromBus) < (_halfLen + 4f))
            return false;
        for (int i = 0; i < all.Count; i++)
        {
            TrafficVehicle o = all[i];
            if (o == this || !o.InUse) continue;
            if (Mathf.Abs(o.metresFromBus - metresFromBus) > (_halfLen + o._halfLen + 3f)) continue;  // not alongside
            if (Mathf.Abs(o.lateral - lat) < (need + o._halfWidth)) return false;                     // would clip
        }
        return true;
    }

    // Drivable lateral band for MY direction (don't steer into the median or onto the footpath).
    bool InBand(float lat)
    {
        RoadZone z = _road != null ? _road.Zone : null;
        if (z == null) return true;
        bool forward = dir > 0;
        float sideSign = (forward == z.leftHandTraffic) ? -1f : 1f;   // forward+LHT → -X
        float inner = z.MedianHalf + _halfWidth;
        float outer = z.DriveHalf - _halfWidth;
        float mag = lat * sideSign;                  // distance into my side (should be inner..outer)
        return mag >= inner - 0.01f && mag <= outer + 0.01f;
    }

    float ClampToBand(float lat)
    {
        RoadZone z = _road != null ? _road.Zone : null;
        if (z == null) return lat;
        bool forward = dir > 0;
        float sideSign = (forward == z.leftHandTraffic) ? -1f : 1f;
        float inner = z.MedianHalf + _halfWidth;
        float outer = Mathf.Max(inner, z.DriveHalf - _halfWidth);
        float mag = Mathf.Clamp(lat * sideSign, inner, outer);
        return mag * sideSign;
    }

    bool SyncToRoad()
    {
        if (_road == null) return false;
        // the displacement from a hit is already baked into lateral/metres (permanent — no spring-back).
        if (_road.SampleRoad(metresFromBus, lateral, out Vector3 pos, out Vector3 fwd, out Vector3 right))
        {
            if (_model != null && !_model.gameObject.activeSelf) _model.gameObject.SetActive(true);
            transform.position = pos;
            Vector3 travel = fwd * dir;
            if (travel.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(travel, Vector3.up);

            // hop: a one-shot arc on the model's local Y over the shove, then it lands and stays (sin → 0 at
            // both ends). Pure local-Y on the model child, so it lifts ABOVE the road — never falls through.
            if (_model != null)
            {
                float baseY = _model.localScale.y * 0.5f;
                float hop = _shoveT > 0f ? shoveHop * Mathf.Sin(Mathf.Clamp01(_shoveT) * Mathf.PI) : 0f;
                _model.localPosition = new Vector3(0f, baseY + hop, 0f);
            }
            return true;
        }
        if (_model != null && _model.gameObject.activeSelf) _model.gameObject.SetActive(false);
        return false;
    }

    void EnsureModel()
    {
        if (_model != null) return;
        // visual cube (no collider on the model — the collision box lives on the root)
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Body";
        var modelCol = box.GetComponent<Collider>();
        if (modelCol != null) Destroy(modelCol);
        _model = box.transform;
        _model.SetParent(transform, false);

        // collision box TRIGGER on the root + kinematic Rigidbody so trigger events fire. Layer set so
        // traffic doesn't self-collide (configured by TrafficSystem); starts disabled (enabled near the bus).
        _collider = gameObject.AddComponent<BoxCollider>();
        _collider.isTrigger = true;
        _collider.enabled = false;
        _body = gameObject.AddComponent<Rigidbody>();
        _body.isKinematic = true;
        _body.useGravity = false;
        _body.interpolation = RigidbodyInterpolation.None;
    }

    void StyleModel(Vector3 size, Color color)
    {
        if (_model == null) EnsureModel();
        _model.localScale = size;
        _model.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        if (_collider != null) { _collider.size = size; _collider.center = new Vector3(0f, size.y * 0.5f, 0f); }
        var r = _model.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
            if (r.sharedMaterial == null || r.sharedMaterial.shader != sh) r.material = new Material(sh);
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", color);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", color);
        }
    }

    // --- collision with the BUS (only the bus carries a BusTag trigger; traffic↔traffic is handled in the
    //     deterministic logical layer by TrafficSystem, NOT physics) ---
    void OnTriggerEnter(Collider other) => TryBusHit(other);
    void OnTriggerStay(Collider other) => TryBusHit(other);

    void TryBusHit(Collider other)
    {
        if (Time.time - _lastHitTime < hitCooldown) return;
        BusController bus = BusController.Instance;
        if (bus == null) return;
        bool isBus = other.GetComponentInParent<BusTag>() != null
                  || (other.attachedRigidbody != null && other.attachedRigidbody == bus.sphere);
        if (!isBus) return;

        _lastHitTime = Time.time;
        bus.ApplyImpact(busSpeedAfterHit);
        if (ShiftManager.Instance != null) ShiftManager.Instance.Damage(busDamageOnHit);

        // SHOVE: commit a PERMANENT displacement in the hit direction (the pushed spot becomes the vehicle's
        // new position — no spring-back) + a one-shot HOP that arcs and lands. Sideways = away from the bus
        // side; plus a backward pop. Scaled a bit by bus speed.
        float busLat = _road != null ? _road.BusLateral : (lateral - 1f);
        float side = Mathf.Sign(lateral - busLat); if (side == 0f) side = 1f;
        float hitBoost = 1f + Mathf.Clamp01(Mathf.Abs(bus.SpeedMps) / 20f);   // 1..2

        // permanent: move the logical position now (clamped to the drivable band so it stays on the road)
        lateral = ClampToBand(lateral + side * shoveLateral * hitBoost);
        _desiredLateral = lateral;                     // don't immediately steer back to the old lane
        metresFromBus += -dir * shoveBack * hitBoost;  // bumped back along its travel direction
        speed = Mathf.Max(0f, speed * 0.3f);           // jolted — lose most speed momentarily

        _shoveT = 1f;                                  // ONLY drives the hop arc now (lands, doesn't reset pos)
    }
}
