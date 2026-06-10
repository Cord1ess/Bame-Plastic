using UnityEngine;

/// A passenger. Transform-driven (kinematic — safe on pooled/teleporting road chunks).
/// Lifecycle: Walking the footpath → Waiting at a stop → Gathering at the curb → walk to the door → board
/// (re-parent into the cabin) → ride. While Aboard, an OWED FARE grows in TIERS (10→20→30→50) the longer they
/// stay; the INSIDE conductor must COLLECT it (nothing is auto-collected — uncollected = lost). Each rider
/// also picks a RIDE DURATION; when it elapses they ring the bell (wantsOff) and at the NEXT stop they ALIGHT
/// and walk away on the footpath as a pedestrian (no more auto-disappear).
public class Passenger : MonoBehaviour
{
    public enum State { Hidden, Walking, Waiting, Gathering, Held, Thrown, HeadingToDoor, Aboard, Alighting }
    public State state = State.Hidden;

    public float moveSpeed = 3.5f;
    public float throwDuration = 0.55f;
    public float throwArcHeight = 2.5f;
    Vector3 _throwStart;
    float _throwT;

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
        if (_view != null) _view.SetColor(ColPaid);
        return amt;
    }

    // Conductor 2 shoved us: walk to the new spot, then settle.
    public void PushTo(BusPassengers.CabinSpot target, Vector3 jitter)
    {
        if (target == null) return;
        _spot = target;
        _targetLocal = target.local + jitter;
        _walking = true;
        if (_view != null) _view.SetColor(ColHeading);
    }

    static readonly Color ColHeading  = new Color(0.95f, 0.85f, 0.2f);   // yellow: heading / walking
    static readonly Color ColSeated   = new Color(0.85f, 0.45f, 0.4f);   // unpaid seated (reddish = owes fare)
    static readonly Color ColStanding = new Color(0.9f, 0.55f, 0.45f);   // unpaid standing (owes fare)
    static readonly Color ColPaid     = new Color(0.35f, 0.85f, 0.5f);   // GREEN once they've paid
    static readonly Color ColWalking  = new Color(0.7f, 0.72f, 0.78f);   // grey: a pedestrian, not a fare yet

    public void Setup(BillboardCharacter view) { _view = view; }

    // legacy read kept for rivals/HUD: the fare they'd represent if stolen at a stop = the first tier.
    public int Fare => FareTiers[0];

    public void ResetWaiting(int fare, Color baseColor)
    {
        ResetCommon();
        state = State.Waiting;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (_view != null) { _view.ApplyHeight(); _view.SetColor(baseColor); }
    }

    void ResetCommon()
    {
        _bus = null; _spot = null; _walking = false;
        _timeAboard = 0f; _owedFare = 0; _paid = false; _wantsOff = false; _rideTime = 0f;
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
        if (_view != null) { _view.ApplyHeight(); _view.SetColor(ColWalking); }
    }

    /// A walker that reached a stop turns into a waiting fare.
    public void ConvertToWaiting(Color baseColor)
    {
        state = State.Waiting;
        if (_view != null) _view.SetColor(baseColor);
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
        if (_view != null) _view.SetColor(ColHeading);
    }

    public void BeginBoarding(BusPassengers bus)
    {
        if (state != State.Waiting && state != State.Gathering && state != State.Held) return;
        _bus = bus;
        state = State.HeadingToDoor;
        if (_view != null) _view.SetColor(ColHeading);
    }

    void Update()
    {
        switch (state)
        {
            case State.Gathering:
                transform.position = Vector3.MoveTowards(transform.position, _gatherTarget, moveSpeed * Time.deltaTime);
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
                transform.position = Vector3.MoveTowards(transform.position, door, moveSpeed * Time.deltaTime);
                if (dSqr < 1.2f) TryBoard();
                break;

            case State.Aboard:
                if (_walking)
                {
                    transform.localPosition = Vector3.MoveTowards(transform.localPosition, _targetLocal, cabinWalkSpeed * Time.deltaTime);
                    if ((transform.localPosition - _targetLocal).sqrMagnitude < 0.01f)
                    {
                        _walking = false;
                        if (_view != null) { _view.ApplyHeight(); _view.SetColor(SettledColor()); }
                    }
                    break;   // fare/ride timers pause while shuffling
                }
                TickAboard();
                break;

            case State.Alighting:
                // walk to the door, then hand off to FootpathPedestrians as a walker on the kerb.
                if (_bus == null) { LeaveToPool(); return; }
                Vector3 d2 = _bus.DoorPosition;
                transform.SetParent(null, true);
                transform.position = Vector3.MoveTowards(transform.position, d2, moveSpeed * Time.deltaTime);
                if ((transform.position - d2).sqrMagnitude < 1.2f) StepOffToFootpath();
                break;
        }
    }

    void TryBoard()
    {
        BusPassengers.CabinSpot spot = _bus.TakeBoardingSpot();
        if (spot == null) return;
        _spot = spot;
        _walking = false;
        transform.SetParent(_bus.Cabin, false);
        transform.localPosition = spot.local;
        // boarding sets up the FARE + RIDE timers; NO auto-fare (the conductor collects).
        _timeAboard = 0f; _owedFare = FareTiers[0]; _paid = false; _wantsOff = false;
        _rideTime = Random.Range(rideMin, rideMax);
        if (_view != null) { _view.ApplyHeight(); _view.SetColor(SettledColor()); }
        _bus.CompleteBoard(this);     // just registers them aboard — no money
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
        if (state == State.Aboard && _wantsOff && !_walking) state = State.Alighting;
    }

    void StepOffToFootpath()
    {
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
