using UnityEngine;

/// A passenger. Transform-driven (kinematic — safe on pooled/teleporting road chunks).
/// Lifecycle: Walking the footpath → Waiting at a stop → Gathering at the curb → walk to the door → board
/// (re-parent into the cabin) → ride. While Aboard, an OWED FARE grows in TIERS (10→20→30→50) the longer they
/// stay; the INSIDE conductor must COLLECT it (nothing is auto-collected — uncollected = lost). Each rider
/// also picks a RIDE DURATION; when it elapses they ring the bell (wantsOff) and at the NEXT stop they ALIGHT
/// and walk away on the footpath as a pedestrian (no more auto-disappear).
public class Passenger : MonoBehaviour
{
    public enum State { Hidden, Walking, Waiting, Gathering, Held, Thrown, HeadingToDoor, QueuingAtDoor, EnteringAisle, Aboard, Alighting }
    public State state = State.Hidden;

    // multiplayer identity: PoolIndex is the stable per-client id (same passenger N everywhere, set once by the
    // pool); NetId is assigned by the DRIVER on board so conductors can reference the exact rider.
    public int PoolIndex = -1;
    public ushort NetId;

    public float moveSpeed = 3.5f;
    public float throwDuration = 0.55f;
    public float throwArcHeight = 2.5f;
    Vector3 _throwStart;
    float _throwT;

    // ---- boarding CHAOS: each rider has a slightly different speed + a wandering side-offset so they DON'T
    //      march to the door in a neat line. Set once per acquire from the id (stable, MP-deterministic-ish). ----
    float _speedMul = 1f;          // 0.8..1.25 per-rider speed variation
    float _wanderPhase;            // phase for the lateral wander
    Vector3 _avoidVel;             // smoothed push-away from nearby vehicles (so dodging isn't jittery)
    Vector3 _avoidTarget;          // last computed avoidance push (recomputed ~10Hz, lerped toward each frame)
    float _avoidScan;              // throttle timer for the (expensive) all-traffic avoidance scan
    public float vehicleAvoidRadius = 3.0f;   // start dodging a vehicle within this

    public float cabinWalkSpeed = 2.5f;   // moving to a seat/stand spot when shoved

    // ---- FARE TIERS (taka). Owed fare rises a tier each `tierInterval` seconds aboard, capped at the top tier. ----
    public static readonly int[] FareTiers = { 10, 20, 30, 50 };
    public float tierInterval = 12f;       // seconds per tier step (10→20→30→50)

    // ---- RIDE DURATION → bell → alight at next stop ----
    public float rideMin = 25f, rideMax = 70f;   // how long they ride before wanting off

    BillboardCharacter _view;
    BusPassengers _bus;
    BusPassengers.CabinSpot _spot;
    Vector3 _gatherTarget;
    Vector3 _targetLocal;
    bool _walking;          // walking to _targetLocal inside the cabin (post-shove)

    float _timeAboard;      // seconds since boarding (drives the fare tier)
    int _owedFare;          // current fare they OWE the conductor (a tier value)
    bool _paid;             // the conductor collected their fare
    float _rideTime;        // total seconds they'll ride before wanting off
    bool _wantsOff;         // rang the bell — alight at the next stop

    public BusPassengers.CabinSpot Spot => _spot;
    public bool IsStanding => state == State.Aboard && !_walking && _spot != null && !_spot.isSeat;

    // ---- inside-conductor fare API ----
    /// Can the inside conductor collect from this rider? Aboard, not yet paid, settled (not mid-walk).
    public bool CanCollect => state == State.Aboard && !_paid && !_walking;
    public int OwedFare => _owedFare;
    public bool Paid => _paid;
    public bool WantsOff => _wantsOff;
    public State CurrentState => state;

    /// The inside conductor collects this rider's owed fare. Returns the amount taken (0 if not collectable).
    public int Collect()
    {
        if (!CanCollect) return 0;
        _paid = true;
        int amt = _owedFare;
        Indicate(ColPaid);
        return amt;
    }

    // Conductor 2 shoved us: walk to the new spot, then settle.
    public void PushTo(BusPassengers.CabinSpot target, Vector3 jitter)
    {
        if (target == null) return;
        _spot = target;
        _targetLocal = target.local + jitter;
        _walking = true;
        Indicate(ColHeading);
    }

    // STATE is shown by a floating DOT above the head (SetIndicator), NOT by recolouring the body — so real
    // character art drops in unchanged. The body keeps a neutral colour; these are the DOT colours per state.
    static readonly Color ColHeading  = new Color(0.95f, 0.85f, 0.2f);   // yellow: heading / walking
    static readonly Color ColSeated   = new Color(0.9f, 0.35f, 0.3f);    // red dot: unpaid (owes fare)
    static readonly Color ColStanding = new Color(0.95f, 0.45f, 0.35f);  // orange-red dot: unpaid standing
    static readonly Color ColPaid     = new Color(0.35f, 0.85f, 0.5f);   // GREEN dot: paid
    static readonly Color ColWalking  = new Color(0f, 0f, 0f, 0f);       // no dot: a plain pedestrian (not a fare)
    static readonly Color BodyNeutral = new Color(0.82f, 0.8f, 0.86f);   // the body's fixed colour (placeholder art)

    // drive the overhead dot + keep the body neutral. Central so every state goes through one path.
    void Indicate(Color dot)
    {
        if (_view == null) return;
        _view.SetColor(BodyNeutral);
        if (dot.a <= 0.01f) _view.HideIndicator(); else _view.SetIndicator(dot);
    }

    Sprite[] _frontWalk;   // on-foot walk cycle
    Sprite _backSprite;    // seated-on-bus back view (facing away)

    public void Setup(BillboardCharacter view) { _view = view; }
    public void Setup(BillboardCharacter view, Sprite[] frontWalk, Sprite back)
    { _view = view; _frontWalk = frontWalk; _backSprite = back; }

    /// seated on the bus → show the BACK sprite (we see their back). On foot → resume the front walk cycle.
    void ShowBack()  { if (_view != null && _backSprite != null) _view.SetSprite(_backSprite); }
    void ShowFront() { if (_view != null && _frontWalk != null) _view.SetWalk(_frontWalk, 0.1f); }

    /// Conductor selection outline (a halo on the billboard) — replaces the old separate marker quad.
    public void SetSelected(bool on) { if (_view != null) _view.SetSelected(on); }

    // legacy read kept for rivals/HUD: the fare they'd represent if stolen at a stop = the first tier.
    public int Fare => FareTiers[0];

    public void ResetWaiting(int fare, Color baseColor)
    {
        ResetCommon();
        state = State.Waiting;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        ShowFront();            // on foot → front walk cycle
        if (_view != null) _view.ApplyHeight();
        Indicate(ColHeading);   // waiting at a stop = a fare to grab (yellow dot)
    }

    void ResetCommon()
    {
        _bus = null; _spot = null; _walking = false;
        _timeAboard = 0f; _owedFare = 0; _paid = false; _wantsOff = false; _rideTime = 0f;
        // per-rider boarding chaos (varies speed + wander so the crowd isn't a marching line)
        _speedMul = Random.Range(0.8f, 1.25f);
        _wanderPhase = Random.Range(0f, 6.28f);
        _avoidVel = Vector3.zero;
    }

    // Move toward `target` in WORLD space with CHAOS: per-rider speed, a sideways wander, and AVOIDANCE of the
    // bus + traffic vehicles (so boarders dodge cars like footpath walkers do, instead of walking through them).
    void MoveToDoorChaotic(Vector3 target)
    {
        Vector3 pos = transform.position;
        Vector3 toTarget = target - pos; toTarget.y = 0f;
        float dist = toTarget.magnitude;
        Vector3 dir = dist > 0.01f ? toTarget / dist : Vector3.zero;

        // wander: a small sideways oscillation perpendicular to the travel dir (eases off as they near the door)
        Vector3 sideAxis = new Vector3(-dir.z, 0f, dir.x);
        float wander = Mathf.Sin(Time.time * 2.3f + _wanderPhase) * 0.6f * Mathf.Clamp01(dist / 4f);

        // avoidance: push away from any vehicle (incl. the bus) within range. The scan over ALL traffic is the
        // expensive bit, so RECOMPUTE it only ~10×/sec (staggered per-passenger) and keep lerping _avoidVel
        // toward the cached push every frame → identical look, a fraction of the cost at high crowd density.
        _avoidScan -= Time.deltaTime;
        if (_avoidScan <= 0f)
        {
            _avoidScan = 0.1f + (_wanderPhase * 0.01f);   // ~10Hz, staggered so they don't all scan the same frame
            Vector3 push = AvoidFrom(BusController.Instance != null ? BusController.Instance.transform.position : pos, pos, 3.2f);
            var ts = TrafficSystem.Instance;
            if (ts != null)
            {
                var live = ts.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    var v = live[i]; if (v == null || !v.isActiveAndEnabled) continue;
                    push += AvoidFrom(v.transform.position, pos, vehicleAvoidRadius);
                }
            }
            _avoidTarget = push;
        }
        _avoidVel = Vector3.Lerp(_avoidVel, _avoidTarget, 6f * Time.deltaTime);   // smooth so dodging reads natural

        Vector3 step = (dir + sideAxis * wander) * (moveSpeed * _speedMul) + _avoidVel;
        Vector3 next = pos + step * Time.deltaTime;
        next.y = pos.y;
        transform.position = next;
    }

    static Vector3 AvoidFrom(Vector3 vehiclePos, Vector3 myPos, float radius)
    {
        Vector3 away = myPos - vehiclePos; away.y = 0f;
        float d = away.magnitude;
        if (d >= radius || d < 0.001f) return Vector3.zero;
        return (away / d) * (radius - d) * 2.2f;   // stronger the closer the vehicle
    }

    public void Hide()
    {
        state = State.Hidden; _bus = null; _spot = null; _walking = false;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// L5: become a pedestrian strolling the footpath (position driven by FootpathPedestrians).
    public void BeginWalking(int fare, Color baseColor)
    {
        ResetCommon();
        state = State.Walking;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        ShowFront();            // back on foot → front walk cycle
        if (_view != null) { _view.ApplyHeight(); _view.SetTiltWithParent(false); }  // stand upright on foot
        Indicate(ColWalking);   // plain pedestrian → no dot
    }

    /// A walker that reached a stop turns into a waiting fare.
    public void ConvertToWaiting(Color baseColor)
    {
        state = State.Waiting;
        Indicate(ColHeading);   // a waiting fare
    }

    public void BeginGather(Vector3 curbTarget)
    {
        if (state != State.Waiting) return;
        _gatherTarget = curbTarget;
        state = State.Gathering;
    }

    public void Grab()
    {
        if (state == State.Waiting || state == State.Gathering) state = State.Held;
    }

    public void ThrowTo(BusPassengers bus)
    {
        if (state != State.Held && state != State.Waiting && state != State.Gathering) return;
        _bus = bus;
        _throwStart = transform.position;
        _throwT = 0f;
        state = State.Thrown;
        Indicate(ColHeading);
    }

    public void BeginBoarding(BusPassengers bus)
    {
        if (state != State.Waiting && state != State.Gathering && state != State.Held) return;
        _bus = bus;
        state = State.HeadingToDoor;
        Indicate(ColHeading);
    }

    void Update()
    {
        switch (state)
        {
            case State.Gathering:
                MoveToDoorChaotic(_gatherTarget);
                break;

            case State.Thrown:
                if (_bus == null) { state = State.HeadingToDoor; break; }
                _throwT += Time.deltaTime / Mathf.Max(0.1f, throwDuration);
                Vector3 tp = Vector3.Lerp(_throwStart, _bus.DoorPosition, Mathf.Clamp01(_throwT));
                tp.y += throwArcHeight * Mathf.Sin(Mathf.Clamp01(_throwT) * Mathf.PI);
                transform.position = tp;
                if (_throwT >= 1f) state = State.HeadingToDoor;
                break;

            case State.HeadingToDoor:
                if (_bus == null) { state = State.Waiting; return; }
                Vector3 door = _bus.DoorPosition;
                float dSqr = (transform.position - door).sqrMagnitude;
                if (dSqr > 120f * 120f) { LeaveToPool(); return; }
                MoveToDoorChaotic(door);                 // chaotic approach + dodges vehicles
                if (dSqr < 1.6f) { state = State.QueuingAtDoor; _bus.JoinDoorQueue(this); }
                break;

            case State.QueuingAtDoor:
                // wait at the door until it's our turn AND a spot is free; meanwhile hold near the door so a
                // crowd visibly queues. The bus grants the next boarder via TryGrantBoard().
                if (_bus == null) { state = State.Waiting; return; }
                MoveToDoorChaotic(_bus.DoorPosition);    // jostle at the door instead of a neat line
                break;

            case State.EnteringAisle:
                // walk DOOR → aisle-entry → our spot, INSIDE the cabin (local space), so they thread the aisle
                // instead of snapping. _aisleStage: 0 = to aisle entry, 1 = to the spot.
                if (_spot == null) { state = State.Aboard; break; }
                Vector3 goal = _aisleStage == 0 ? _aisleEntryLocal : _spot.local;
                transform.localPosition = Vector3.MoveTowards(transform.localPosition, goal, cabinWalkSpeed * Time.deltaTime);
                if ((transform.localPosition - goal).sqrMagnitude < 0.04f)
                {
                    if (_aisleStage == 0) _aisleStage = 1;     // reached aisle entry → head to the seat/stand
                    else SettleAboard();                       // reached the spot → settled
                }
                break;

            case State.Aboard:
                if (_walking)
                {
                    transform.localPosition = Vector3.MoveTowards(transform.localPosition, _targetLocal, cabinWalkSpeed * Time.deltaTime);
                    if ((transform.localPosition - _targetLocal).sqrMagnitude < 0.01f)
                    {
                        _walking = false;
                        if (_view != null) _view.ApplyHeight();
                        Indicate(SettledColor());
                    }
                    break;   // fare/ride timers pause while shuffling
                }
                TickAboard();
                break;

            case State.Alighting:
                // MIRROR of boarding, in reverse: thread the aisle OUT to the DOOR in cabin-local space (stay
                // parented to the cabin so they follow the bus), THEN detach + step to the footpath. _aisleStage:
                // 0 = seat → aisle-mouth, 1 = aisle-mouth → door, 2 = stepped out → walk to footpath (world).
                if (_bus == null) { LeaveToPool(); return; }
                if (_aisleStage < 2)
                {
                    Vector3 exitGoal = _aisleStage == 0 ? _aisleEntryLocal : _bus.DoorLocal;
                    transform.localPosition = Vector3.MoveTowards(transform.localPosition, exitGoal, cabinWalkSpeed * Time.deltaTime);
                    if ((transform.localPosition - exitGoal).sqrMagnitude < 0.04f)
                    {
                        if (_aisleStage == 0) _aisleStage = 1;          // at the aisle mouth → head to the door
                        else { _aisleStage = 2; transform.SetParent(null, true); }   // at the door → step out to world
                    }
                }
                else
                {
                    // stepped out at the door → walk the last bit OUT onto the footpath (a point beside the door,
                    // off the road), and only THEN become a pedestrian — so the step-off is actually visible
                    // instead of teleporting to the footpath system the instant they detach.
                    if (!_haveAlightTarget) { _alightTarget = ComputeFootpathDropoff(); _haveAlightTarget = true; }
                    transform.position = Vector3.MoveTowards(transform.position, _alightTarget, moveSpeed * Time.deltaTime);
                    // face the way they're walking off
                    Vector3 step = _alightTarget - transform.position; step.y = 0f;
                    if (step.sqrMagnitude > 1e-4f && _view != null) _view.FaceDirection(step);
                    if ((transform.position - _alightTarget).sqrMagnitude < 0.09f)
                    {
                        _haveAlightTarget = false;
                        StepOffToFootpath();   // reached the footpath → hand to FootpathPedestrians (walks them away)
                    }
                }
                break;
        }
    }

    Vector3 _aisleEntryLocal;   // cabin-local point just inside the door where boarders enter the aisle
    int _aisleStage;            // 0 = walking to aisle entry, 1 = walking to the spot
    Vector3 _alightTarget;      // world point on the footpath the alighter walks to before becoming a pedestrian
    bool _haveAlightTarget;

    // A point on the footpath just OUTSIDE the door (door world pos pushed sideways toward the kerb + a touch
    // back), so an alighting rider visibly steps off the bus and clears the doorway before becoming a pedestrian.
    Vector3 ComputeFootpathDropoff()
    {
        if (_bus == null) return transform.position;
        Vector3 door = _bus.DoorPosition;
        Transform bt = _bus.transform;
        // door is on the bus's LEFT under LHT → push further left (away from the road) + slightly back.
        Vector3 outward = -bt.right;          // toward the kerb/footpath
        Vector3 p = door + outward * 2.2f - bt.forward * 0.6f;
        p.y = door.y;
        return p;
    }

    /// The bus grants THIS queued boarder a reserved spot — start threading the aisle to it. Called by
    /// BusPassengers.TryGrantBoard() (driver-authoritative in MP). Re-parents into the cabin at the door.
    public void BeginAisleEntry(BusPassengers.CabinSpot spot, Vector3 aisleEntryLocal)
    {
        if (spot == null) return;
        _spot = spot;
        _aisleEntryLocal = aisleEntryLocal;
        _aisleStage = 0;
        _walking = false;
        transform.SetParent(_bus.Cabin, false);
        // enter at the door's cabin-local position so the walk starts AT the door, not snapped to the spot
        transform.localPosition = aisleEntryLocal;
        if (_view != null) _view.ApplyHeight();
        Indicate(ColHeading);
        state = State.EnteringAisle;
    }

    void SettleAboard()
    {
        _walking = false;
        _timeAboard = 0f; _owedFare = FareTiers[0]; _paid = false; _wantsOff = false;
        _rideTime = Random.Range(rideMin, rideMax);
        ShowBack();                                                  // seated → we see their back
        if (_view != null) { _view.ApplyHeight(); _view.SetTiltWithParent(true); }  // lean with the bus
        Indicate(SettledColor());
        _bus.CompleteBoard(this);     // registers them aboard — no money
        state = State.Aboard;
    }

    void TickAboard()
    {
        _timeAboard += Time.deltaTime;

        // owed fare rises a tier with time (only while UNPAID — once paid, fare is settled)
        if (!_paid)
        {
            int tier = Mathf.Clamp(Mathf.FloorToInt(_timeAboard / Mathf.Max(1f, tierInterval)), 0, FareTiers.Length - 1);
            _owedFare = FareTiers[tier];
        }

        // ride duration → ring the bell (wants off). Then alight at the next stop the bus pulls up to.
        if (!_wantsOff && _timeAboard >= _rideTime)
        {
            _wantsOff = true;
            // a subtle cue could go here (bell sound / indicator); colour stays as-is.
        }
    }

    Color SettledColor()
    {
        if (_paid) return ColPaid;
        return (_spot != null && _spot.isSeat) ? ColSeated : ColStanding;
    }

    /// Called by BusPassengers when the bus is stopped at a stop: any rider who wants off begins alighting.
    public void TryAlightAtStop()
    {
        if (state == State.Aboard && _wantsOff && !_walking)
        {
            _aisleStage = 0;                                  // thread the aisle OUT from the seat (stages 0→1→2)
            _haveAlightTarget = false;                        // fresh footpath drop-off computed at stage 2
            if (_bus != null) _aisleEntryLocal = _bus.AisleEntryLocal();   // aisle mouth at the door (fresh)
            ShowFront();                                      // stand up → front WALK cycle (animates as they exit)
            if (_view != null) _view.SetTiltWithParent(true);   // still in the cabin → still tilts
            Indicate(ColHeading);
            state = State.Alighting;
        }
    }

    // ---- multiplayer mirror (conductor clients): place/remove a rider to match the driver, WITHOUT running
    //      the local board/ride state machine (the driver is authoritative for fares/alighting). ----
    public void MirrorTakeSpot(BusPassengers bus, BusPassengers.CabinSpot spot)
    {
        _bus = bus; _spot = spot; _walking = false;
        transform.SetParent(bus.Cabin, false);
        transform.localPosition = spot.local;
        state = State.Aboard;
        ShowBack();                                                  // seated → back view
        if (_view != null) { _view.ApplyHeight(); _view.SetTiltWithParent(true); }
        Indicate(SettledColor());
    }
    public void MirrorLeave()
    {
        _spot = null;
        var peds = FootpathPedestrians.Instance;
        if (peds == null || !peds.AdoptAsWalker(this))
        {
            if (PassengerPool.Instance != null) PassengerPool.Instance.Return(this); else Hide();
        }
        _bus = null;
    }
    /// the conductor sets a rider's paid state when the driver broadcasts FareCollected.
    public void MirrorPaid() { _paid = true; Indicate(ColPaid); }

    void StepOffToFootpath()
    {
        // DRIVER: broadcast the alight so conductors remove the same rider.
        var gn = BamePlastic.Net.GameNet.Instance;
        if (gn != null && gn.Active && gn.IsDriver) gn.DriverPassengerAlighted(this);
        // COLLECT-OR-LOSE-IT: this rider is leaving. If the conductor never collected, that fare walks free —
        // signal the loss so the HUD can ping it (driver/solo only; conductor clients use MirrorLeave, not this).
        if (!_paid && _owedFare > 0) ShiftManager.ReportFareLost(_owedFare);
        // free the cabin spot
        if (_bus != null && _spot != null) _bus.LeaveCabin(this, _spot);
        _spot = null;
        // hand the SAME passenger to the footpath system as a walking pedestrian (no pop-out)
        var peds = FootpathPedestrians.Instance;
        if (peds != null && peds.AdoptAsWalker(this))
        {
            // FootpathPedestrians now owns it (drives it as a Walking pedestrian).
            _bus = null;
        }
        else
        {
            LeaveToPool();   // fallback if the footpath system isn't available
        }
    }

    void LeaveToPool()
    {
        if (_bus != null && _spot != null) _bus.LeaveCabin(this, _spot);
        _spot = null;
        if (PassengerPool.Instance != null) PassengerPool.Instance.Return(this);
        else Hide();
    }
}
