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
    public enum Kind { Rickshaw, Car, Bus, Truck }

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
    public bool Knocked => _shoveT > 0f;   // mid-shove (normal traffic skips the anti-stack pass while tumbling)
    public bool Solid => _solid;           // rival bus — a solid obstacle; STILL separates from other vehicles
    public float HalfLen => _halfLen;
    public float HalfWidth => _halfWidth;
    public float Cruise => _cruise;        // clear-road desired speed (a brain reads it to set aggression)

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
    [Tooltip("Beyond this distance (m) ahead/behind the bus, the vehicle's VISUAL is hidden (logic still runs).")]
    public float lodVisibleRange = 230f;   // ~the smog edge, so a culled vehicle is never actually seen
    bool _modelVisible = true;
    Renderer[] _modelRenderers;     // cached on model build → LOD toggle without per-frame GetComponentsInChildren
    [Tooltip("How far the bus PUSHES the vehicle sideways on a hit (metres it slides away). Bigger = harder shove.")]
    public float shoveLateral = 2.6f;
    [Tooltip("How far back along the road the vehicle is pushed on a hit (metres).")]
    public float shoveBack = 1.4f;
    [Tooltip("Seconds the push takes to slide out and settle.")]
    public float shoveTime = 0.4f;
    [Tooltip("How hard the bus BOUNCES BACK when it rams a SOLID rival bus (m/s). Bigger = the bus is thrown " +
             "back more, so it can't drive through/over the rival.")]
    public float rivalBounce = 9f;
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
    public float followGap = 7f;       // min clear gap ahead before hard-braking (m, plus half-lengths)
    public float lookahead = 42f;      // how far ahead to sense (m) — bigger = reacts earlier/smarter
    public float scratchClearance = 0.5f;  // extra lateral room needed to commit to a squeeze (small = aggressive)
    float eagerness = 0.5f;            // 0..1 how hard it surges into open road (set by kind on Acquire)
    float _massFactor = 0.65f;         // collision mass feel: <1 light (flung far, barely slows bus), >1 heavy

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
        _offRoadFrames = 0;                      // fresh grace counter (anti-flicker)
        _pushLatVel = 0f; _pushBackVel = 0f;
        _crossing = false;                       // not mid-cross on a fresh acquire
        _modelVisible = true;                    // LOD re-evaluates on the new vehicle (renderers default on)
        SetSolid(false);                         // pooled vehicles start as normal (trigger) traffic
        _halfLen = size.z * 0.5f;
        _halfWidth = size.x * 0.5f;
        // rickshaws are nimble + pushy (steer quick, surge into gaps); buses ponderous + calmer.
        // _massFactor drives the collision FEEL: light things (rickshaw) get flung far + barely slow the bus;
        // heavy things (bus) barely move + slow the bus more. Makes the push read as real mass.
        switch (k)
        {
            case Kind.Rickshaw: _accel = 6f;   _brake = 9f;  _steerRate = 6.5f; eagerness = 0.8f; _massFactor = 0.30f; break;
            case Kind.Bus:      _accel = 3.5f; _brake = 9f;  _steerRate = 3f;   eagerness = 0.3f; _massFactor = 1.10f; break;
            case Kind.Truck:    _accel = 4f;   _brake = 10f; _steerRate = 3.5f; eagerness = 0.35f; _massFactor = 0.95f; break;  // normal heavy-ish, not a wall
            default:            _accel = 7f;   _brake = 12f; _steerRate = 5f;   eagerness = 0.6f; _massFactor = 0.65f; break;
        }
        InUse = true;
        StyleModel(size, color);
        // POSITION FIRST (while still INACTIVE), THEN reveal — activating before placing left the vehicle
        // rendering one frame at its stale pooled spot (the faint "ghost"/flash on spawn). SyncToRoad writes the
        // transform while inactive; spawn metres is always inside the loaded road so it samples fine. Then show.
        SyncToRoad();
        gameObject.SetActive(true);
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
        // enable the collision box only near the bus (cheap physics — distant traffic has no collider).
        // SOLID rivals keep their collider on always (it's a real physical wall the bus mustn't pass through).
        if (!_solid)
        {
            bool near = Mathf.Abs(metresFromBus) < colliderActiveRange;
            if (_collider != null && _collider.enabled != near) _collider.enabled = near;
        }

        // LOD: hide the VISUAL (not the logic) when far ahead/behind — keeps the sim running so traffic is in
        // place when it comes into view, but skips drawing the dozens of distant vehicles (FPS at high density).
        // Renderers are CACHED (set when the model is built) → no per-frame GetComponentsInChildren allocation.
        bool visible = Mathf.Abs(metresFromBus) < lodVisibleRange;
        if (_modelVisible != visible && _modelRenderers != null)
        {
            _modelVisible = visible;
            for (int i = 0; i < _modelRenderers.Length; i++)
                if (_modelRenderers[i] != null) _modelRenderers[i].enabled = visible;
        }

        // advance the hit PUSH: slide the vehicle along the ground in the shove direction, decaying to 0 over
        // shoveTime. Applied to the logical position so it rides the road point (never clips/jumps).
        if (_shoveT > 0f)
        {
            _shoveT = Mathf.Max(0f, _shoveT - dt / Mathf.Max(0.05f, shoveTime));
            lateral = ClampToBand(lateral + _pushLatVel * _shoveT * dt);
            metresFromBus += _pushBackVel * _shoveT * dt;
        }

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

        if (_crossing)
        {
            // CROSSING the road from the oncoming lane to OUR side: ease lateral across the median (NO band clamp,
            // so it can traverse), keep a steady crawl. When it reaches the target side, MERGE: flip dir so it
            // joins our flow — a car/rickshaw that cut across. (Dhaka chaos; a hazard the player must dodge.)
            lateral = Mathf.MoveTowards(lateral, _crossTarget, (_steerRate * 0.7f) * dt);
            speed = Mathf.MoveTowards(speed, _cruise * 0.7f, _accel * dt);
            if (Mathf.Abs(lateral - _crossTarget) < 0.3f) { dir = _crossNewDir; _crossing = false; lateral = ClampToBand(lateral); }
        }
        else
        {
            _desiredLateral = ClampToBand(desiredLat);
            lateral = Mathf.MoveTowards(lateral, _desiredLateral, _steerRate * dt);
            lateral = ClampToBand(lateral);
        }

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
        if (bestGap > lookahead * 0.8f) { desiredLat = lateral; return; }   // start weaving earlier (smoother)

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

    int _offRoadFrames;        // consecutive frames SampleRoad failed
    Vector3 _lastPos, _lastFwd; // last good pose, for smooth EXTRAPOLATION on a momentary miss (no freeze)

    bool SyncToRoad()
    {
        if (_road == null) return false;
        // the displacement from a hit is already baked into lateral/metres (permanent — no spring-back).
        if (_road.SampleRoad(metresFromBus, lateral, out Vector3 pos, out Vector3 fwd, out Vector3 right))
        {
            _offRoadFrames = 0;
            _lastPos = pos; _lastFwd = fwd;
            if (_model != null && !_model.gameObject.activeSelf) _model.gameObject.SetActive(true);
            transform.position = pos;
            Vector3 travel = fwd * dir;
            if (travel.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(travel, Vector3.up);
            // cube fallback is centre-pivoted → lift to sit on the ground; real models are already base-seated.
            if (_model != null && _modelIsCube) _model.localPosition = new Vector3(0f, _model.localScale.y * 0.5f, 0f);
            return true;
        }

        // SampleRoad missed THIS frame (a genuine 1-frame dip while tiles stream). HOLD the last good pose for a
        // tiny grace — do NOT extrapolate-move (that phantom slide off the road was the "ghost"). After the grace
        // it's genuinely off-road → recycle. With the bus-tracking fixed, mid-road misses are rare/transient.
        _offRoadFrames++;
        if (_offRoadFrames >= 2) return false;            // really gone — let TrafficSystem recycle it
        transform.position = _lastPos;                    // hold still (no phantom motion → no ghost)
        return true;
    }

    void EnsureModel()
    {
        if (_collider != null) return;   // collider/body set up once; the VISUAL is (re)built per-kind in StyleModel
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

    // a procedural cube fallback when no real model is available for this kind (keeps the game working).
    Transform MakeCube()
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Body";
        var modelCol = box.GetComponent<Collider>();
        if (modelCol != null) Destroy(modelCol);
        box.transform.SetParent(transform, false);
        _modelIsCube = true;
        return box.transform;
    }

    bool _modelIsCube;
    TrafficVehicle.Kind _modelKind = (TrafficVehicle.Kind)(-1);   // which kind the current _model was built for
    bool _modelIsRival;
    Vector3 _lastSize = new Vector3(1.8f, 1.5f, 4.2f);            // last styled box size (for rival re-style)

    bool _solid;   // rivals: act as a SOLID obstacle — the bus physically BOUNCES BACK off them (manual
                   // knockback in code, reliable with the sphere physics) instead of passing through.

    /// Mark this vehicle as a solid obstacle (rival buses). Detection still uses the trigger (the bus's
    /// CollisionBox overlaps it); the physical bounce is applied manually in TryBusHit. The collider stays a
    /// trigger but ALWAYS enabled for a rival (so the overlap is detected wherever the player meets it).
    public void SetSolid(bool solid)
    {
        bool changed = _solid != solid;
        _solid = solid;
        if (solid && _collider != null) _collider.enabled = true;
        // a rival may want a distinct bus model — re-style if the solid/rival state changed and we have a model
        if (changed && _model != null && InUse) RefreshModelForRival();
    }

    void StyleModel(Vector3 size, Color color)
    {
        EnsureModel();
        _lastSize = size;

        // the collision box is ALWAYS the gameplay source of truth (visual never affects handling)
        if (_collider != null) { _collider.size = size; _collider.center = new Vector3(0f, size.y * 0.5f, 0f); }

        // (Re)build the VISUAL only when the kind/rival changed (pooled vehicles reuse across kinds). A real
        // model from the library if available, else a coloured cube fallback.
        bool wantRival = _solid;   // rivals are flagged solid before/after Acquire; refresh on the rival path too
        if (_model == null || _modelKind != kind || _modelIsRival != wantRival)
        {
            if (_model != null) Destroy(_model.gameObject);
            _model = null; _modelIsCube = false;

            var lib = VehicleModelLibrary.Instance;
            if (lib != null) _model = lib.CreateModel(kind, id, size, transform, wantRival);
            if (_model == null) _model = MakeCube();

            // cache the renderers ONCE (the model only changes here) → LOD toggles with no per-frame alloc
            _modelRenderers = _model != null ? _model.GetComponentsInChildren<Renderer>(true) : null;
            _modelVisible = true;   // freshly built models start visible; LOD re-evaluates next Tick

            _modelKind = kind; _modelIsRival = wantRival;
        }

        if (_modelIsCube)
        {
            _model.localScale = size;
            _model.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            var r = _model.GetComponent<Renderer>();
            if (r != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Standard");
                if (r.sharedMaterial == null || r.sharedMaterial.shader != sh) r.material = new Material(sh);
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", color);
                if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", color);
            }
        }
        // real models are already fitted to `size` + base-seated by the library; nothing more to do.
    }

    /// Rivals call this after SetSolid — re-style so the visual uses the rival-bus look if one is set.
    public void RefreshModelForRival() => StyleModel(_lastSize, Color.white);

    // --- collision with the BUS (the bus's CollisionBox trigger overlaps us) ---
    // Normal traffic → shoved aside. Rival buses (_solid) → the bus is KNOCKED BACK every overlapping frame
    // (BusController.Knockback) so it can't drive through/over them, and the rival is firmly shoved too.
    void OnTriggerEnter(Collider other) => TryBusHit(other);
    void OnTriggerStay(Collider other) => TryBusHit(other);

    void TryBusHit(Collider other)
    {
        BusController bus = BusController.Instance;
        if (bus == null) return;
        bool isBus = other.GetComponentInParent<BusTag>() != null
                  || (other.attachedRigidbody != null && other.attachedRigidbody == bus.sphere);
        if (!isBus) return;
        // MULTIPLAYER: on a CONDUCTOR client the bus is a PROXY (interpolated from the driver's broadcast). The
        // driver is authoritative for the bus's motion/health, so traffic here must NOT knock or damage it — just
        // let the local vehicle shove off it cosmetically (no bus effect). The driver's client does the real hit.
        bool busAuthoritative = !bus.ProxyMode;

        float busLat = _road != null ? _road.BusLateral : (lateral - 1f);
        float side = Mathf.Sign(lateral - busLat); if (side == 0f) side = 1f;
        float hitBoost = 1f + Mathf.Clamp01(Mathf.Abs(bus.SpeedMps) / 18f);   // 1..2

        if (_solid)
        {
            // RIVAL BUS = a HEAVY but PUSHABLE clash (mutual). The harder you drive INTO it, the more YOU get
            // shoved back AND the more the rival gets shoved FORWARD — so you can bully it down the road, it just
            // resists and shoves you too (heavier, so it wins a standoff). Continuous press (hard=false: no
            // speed-zero / no shake spam); a real IMPACT (damage + shake) fires once per cooldown.
            Vector3 awayFromRival = bus.transform.position - transform.position; awayFromRival.y = 0f;
            bool impact = Time.time - _lastHitTime >= hitCooldown;
            // how fast is the bus driving INTO the rival right now? scales the mutual push.
            float intoSpeed = bus.sphere != null ? Mathf.Max(0f, Vector3.Dot(bus.sphere.linearVelocity, -awayFromRival.normalized)) : Mathf.Abs(bus.SpeedMps);
            // bounce the bus back only PARTIALLY (so it can still make headway and push the rival), scaled by how
            // hard it's pressing — a gentle lean barely bounces, a full ram bounces more but never fully stops you.
            if (busAuthoritative) bus.Knockback(awayFromRival, Mathf.Min(rivalBounce, 2f + intoSpeed * 0.45f), impact);
            // the rival is SHOVED FORWARD proportional to how hard you ram it → you can move it down the road.
            float ramForward = Mathf.Clamp(intoSpeed, 0f, 12f) * 0.18f;     // m/s the rival is pushed along the road
            _pushLatVel  = side * shoveLateral * 0.6f * hitBoost / Mathf.Max(0.1f, shoveTime);
            _pushBackVel = (dir * ramForward + (-dir * shoveBack * 0.4f)) / Mathf.Max(0.1f, shoveTime);  // forward shove
            _desiredLateral = ClampToBand(lateral + side * shoveLateral * 0.6f * hitBoost);
            speed = Mathf.Max(speed * 0.7f, ramForward);    // keep some of the rammed momentum
            _shoveT = 1f;
            if (impact)
            { _lastHitTime = Time.time; if (busAuthoritative && ShiftManager.Instance != null) ShiftManager.Instance.Damage(busDamageOnHit); }
            return;
        }

        if (Time.time - _lastHitTime < hitCooldown) return;
        _lastHitTime = Time.time;

        // NORMAL traffic PUSH — mass-based feel. Light vehicles (rickshaw) get FLUNG and barely slow the bus;
        // heavier ones (a traffic bus) move little and slow the bus more. invMass scales how far it's shoved;
        // the bus keeps MORE speed against light things (busSpeedAfterHit raised by 1/mass).
        float invMass = Mathf.Clamp(0.65f / Mathf.Max(0.15f, _massFactor), 0.4f, 2.2f);
        if (busAuthoritative)
        {
            if (ShiftManager.Instance != null) ShiftManager.Instance.Damage(Mathf.RoundToInt(busDamageOnHit * _massFactor));
            bus.ApplyImpact(Mathf.Clamp01(busSpeedAfterHit + 0.25f * (invMass - 1f)));   // light → keep more speed
        }

        _pushLatVel  = side * shoveLateral * hitBoost * invMass / Mathf.Max(0.1f, shoveTime);   // light → flung far
        _pushBackVel = -dir * shoveBack * hitBoost * invMass / Mathf.Max(0.1f, shoveTime);
        _desiredLateral = ClampToBand(lateral + side * shoveLateral * hitBoost * invMass);
        speed = Mathf.Max(0f, speed * 0.3f);
        _shoveT = 1f;
    }

    float _pushLatVel, _pushBackVel;   // hit-push velocities (m/s), decay to 0 over the shove

    // --- lane CROSSING (oncoming → our side, then merge). Started by TrafficSystem (deterministic RNG). ---
    bool _crossing;
    float _crossTarget;     // target lateral (our side's drive band centre)
    int _crossNewDir;       // dir to flip to once across (merges into our flow)
    public bool Crossing => _crossing;

    /// Begin crossing the road to the OPPOSITE side's drive lane, then merge into that direction. Only sensible
    /// for an oncoming (dir -1) small vehicle; TrafficSystem gates who/when.
    public void BeginCross()
    {
        RoadZone z = _road != null ? _road.Zone : null;
        if (z == null || _crossing) return;
        _crossNewDir = -dir;                                   // flip to the other flow when across
        bool newForward = _crossNewDir > 0;
        float sideSign = (newForward == z.leftHandTraffic) ? -1f : 1f;
        _crossTarget = sideSign * Mathf.Lerp(z.MedianHalf + _halfWidth, z.DriveHalf - _halfWidth, 0.5f);  // mid of our band
        _crossing = true;
    }
}
