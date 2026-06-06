using UnityEngine;

/// A placeholder passenger. Transform-driven (kinematic — no physics, so safe on pooled/teleporting
/// chunks). Lifecycle: Waiting at the stop → Gathering at the curb when the bus approaches → walk to
/// the moving door → board (re-parent into the bus cabin, pay) → ride (Aboard) → leave after a dwell
/// and return to the pool. (Conductor 1 will add grab/throw; Conductor 2 the seat-shuffle + haggle.)
public class Passenger : MonoBehaviour
{
    public enum State { Hidden, Waiting, Gathering, Held, Thrown, HeadingToDoor, Aboard }
    public State state = State.Hidden;

    public float moveSpeed = 3.5f;
    public float dwellMin = 18f, dwellMax = 40f;   // how long they ride before getting off
    public float throwDuration = 0.55f;
    public float throwArcHeight = 2.5f;
    Vector3 _throwStart;
    float _throwT;

    public float cabinWalkSpeed = 2.5f;   // moving to a seat/stand spot when shoved

    BillboardCharacter _view;
    BusPassengers _bus;
    int _fare;
    BusPassengers.CabinSpot _spot;
    Vector3 _gatherTarget;
    Vector3 _targetLocal;
    bool _walking;          // walking to _targetLocal inside the cabin (post-shove)
    float _dwell;
    bool _haggled;

    public BusPassengers.CabinSpot Spot => _spot;
    // Only a settled standing rider (not mid-walk) can be shoved.
    public bool IsStanding => state == State.Aboard && !_walking && _spot != null && !_spot.isSeat;

    // Conductor 2 shoved us: walk to the new spot, then settle (seat or back of the aisle).
    public void PushTo(BusPassengers.CabinSpot target, Vector3 jitter)
    {
        if (target == null) return;
        _spot = target;
        _targetLocal = target.local + jitter;
        _walking = true;
        if (_view != null) _view.SetColor(ColHeading);
    }

    // Conductor 2 can squeeze a bonus fare out of each aboard passenger once.
    public bool CanHaggle => state == State.Aboard && !_haggled;
    public void Haggle()
    {
        _haggled = true;
        if (_view != null) _view.SetColor(new Color(0.2f, 0.95f, 0.5f));   // brighter green = squeezed
    }

    static readonly Color ColHeading = new Color(0.95f, 0.85f, 0.2f);   // yellow heading to the door / walking
    static readonly Color ColSeated = new Color(0.35f, 0.8f, 0.45f);    // green once seated (paid)
    static readonly Color ColStanding = new Color(0.55f, 0.75f, 0.95f); // blue-ish while standing in the aisle

    public void Setup(BillboardCharacter view) { _view = view; }

    public void ResetWaiting(int fare, Color baseColor)
    {
        _fare = fare; _bus = null; _spot = null; _haggled = false; _walking = false;
        state = State.Waiting;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (_view != null) { _view.ApplyHeight(); _view.SetColor(baseColor); }
    }

    public void Hide()
    {
        state = State.Hidden; _bus = null; _spot = null; _walking = false;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    // Bus is approaching: move to a spot at the curb and wait there ("crowd up").
    public void BeginGather(Vector3 curbTarget)
    {
        if (state != State.Waiting) return;
        _gatherTarget = curbTarget;
        state = State.Gathering;
    }

    // Picked up by the conductor — he positions us until he throws/drops us.
    public void Grab()
    {
        if (state == State.Waiting || state == State.Gathering) state = State.Held;
    }

    // Conductor 1 throws us at the door — a short arc, then we board on landing.
    public void ThrowTo(BusPassengers bus)
    {
        if (state != State.Held && state != State.Waiting && state != State.Gathering) return;
        _bus = bus;
        _throwStart = transform.position;
        _throwT = 0f;
        state = State.Thrown;
        if (_view != null) _view.SetColor(ColHeading);
    }

    // Bus has pulled up (or the conductor threw us): head to the door and board.
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
                if (_throwT >= 1f) state = State.HeadingToDoor;   // arrived — board via the normal path
                break;

            case State.HeadingToDoor:
                if (_bus == null) { state = State.Waiting; return; }
                Vector3 door = _bus.DoorPosition;
                float dSqr = (transform.position - door).sqrMagnitude;
                if (dSqr > 120f * 120f) { LeaveToPool(); return; }   // bus gone far away — give up
                transform.position = Vector3.MoveTowards(transform.position, door, moveSpeed * Time.deltaTime);
                if (dSqr < 1.2f)
                {
                    BusPassengers.CabinSpot spot = _bus.TakeBoardingSpot();
                    if (spot != null)
                    {
                        _spot = spot;
                        _walking = false;
                        transform.SetParent(_bus.Cabin, false);
                        transform.localPosition = spot.local;
                        if (_view != null) { _view.ApplyHeight(); _view.SetColor(spot.isSeat ? ColSeated : ColStanding); }
                        _bus.CompleteBoard(this, _fare);
                        _dwell = Random.Range(dwellMin, dwellMax);
                        state = State.Aboard;
                    }
                }
                break;

            case State.Aboard:
                if (_walking)
                {
                    transform.localPosition = Vector3.MoveTowards(transform.localPosition, _targetLocal, cabinWalkSpeed * Time.deltaTime);
                    if ((transform.localPosition - _targetLocal).sqrMagnitude < 0.01f)
                    {
                        _walking = false;
                        if (_view != null) { _view.ApplyHeight(); _view.SetColor(_spot != null && _spot.isSeat ? ColSeated : ColStanding); }
                    }
                    break;   // dwell pauses while shuffling
                }
                _dwell -= Time.deltaTime;
                if (_dwell <= 0f) LeaveToPool();
                break;
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
