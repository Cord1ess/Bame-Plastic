using UnityEngine;

/// L4 — the competitive RIVAL behaviour, layered on a normal TrafficVehicle via IVehicleBrain. The vehicle
/// itself does all the driving (road-relative motion, curve-following, collision, band-clamping) exactly
/// like ambient traffic — this brain only adds INTENT: seek a bus stop ahead, camp it to STEAL the waiting
/// passengers (real taka into its ShiftManager standings entry), leave when loaded, and drift to block the
/// player. Because it reuses the traffic vehicle, it can never "go all over the place" — it lives in the
/// same deterministic road space as every other vehicle.
public class RivalBrain : MonoBehaviour, TrafficVehicle.IVehicleBrain
{
    enum State { Cruise, SeekStop, Camp, Leaving }

    [Header("Identity")]
    public string rivalName = "Sonar Bangla";

    [Header("Compete")]
    [Tooltip("Look this far ahead (m, along the road) for a stop to camp.")]
    public float stopSeekRange = 70f;
    [Tooltip("World distance (m) to the stop at which it pulls in and starts grabbing passengers.")]
    public float campDistance = 7f;
    [Tooltip("Passengers grabbed per second while camping.")]
    public float grabRate = 4f;
    [Tooltip("Leave once it has grabbed at least this many (then drives off, freeing the stop).")]
    public int leaveAfter = 8;
    [Tooltip("Max seconds it will camp before leaving regardless.")]
    public float maxCampTime = 5f;
    [Tooltip("Crawl speed (m/s) while approaching/leaving a stop.")]
    public float approachSpeed = 4f;

    [Header("Block the player")]
    [Tooltip("If the player is just behind and close (m), drift toward the player's lane to block.")]
    public float blockRange = 14f;

    RivalBus _standing;
    State _state = State.Cruise;
    Vector3 _stopPos;
    bool _haveStop;
    int _grabbed;
    float _campTimer, _grabAccum;

    public void SetName(string n) { rivalName = n; }

    void OnEnable() { LinkStanding(); }

    void LinkStanding()
    {
        var sm = ShiftManager.Instance;
        if (sm == null) return;
        _standing = sm.rivals.Find(r => r.name == rivalName);
        if (_standing == null) { _standing = new RivalBus { name = rivalName, earnRate = 0f }; sm.rivals.Add(_standing); }
        _standing.drivenByAgent = true;
    }

    // Called by TrafficVehicle.Tick AFTER its own avoidance — we rewrite intent on top.
    public void Decide(TrafficVehicle self, float dt, ref float desiredSpeed, ref float desiredLat)
    {
        var road = self.Road;
        if (road == null) return;
        if (_standing == null) LinkStanding();

        switch (_state)
        {
            case State.Cruise:
                MaybeBlockPlayer(self, road, ref desiredLat);
                // hunt for a stop ahead with waiting passengers (only forward-running rivals camp)
                if (self.dir > 0 && SplineStopSpawner.Instance != null &&
                    SplineStopSpawner.Instance.TryGetStopAhead(self.transform.position, self.transform.forward,
                                                               stopSeekRange, out _stopPos, out int waiting) && waiting > 0)
                { _haveStop = true; _state = State.SeekStop; }
                break;

            case State.SeekStop:
            {
                if (!RefreshStop(self)) { _state = State.Cruise; break; }
                desiredLat = KerbLateral(road);                            // ease toward the footpath lane
                float d = Vector3.Distance(self.transform.position, _stopPos);
                desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Lerp(approachSpeed, desiredSpeed, Mathf.InverseLerp(campDistance, stopSeekRange, d)));
                if (d <= campDistance) { _state = State.Camp; _campTimer = 0f; _grabbed = 0; _grabAccum = 0f; }
                break;
            }

            case State.Camp:
            {
                desiredSpeed = 0f;                                          // pulled up at the kerb
                desiredLat = KerbLateral(road);
                _campTimer += dt;
                _grabAccum += grabRate * dt;
                int grabNow = Mathf.FloorToInt(_grabAccum);
                if (grabNow > 0 && SplineStopSpawner.Instance != null)
                {
                    _grabAccum -= grabNow;
                    int fare = SplineStopSpawner.Instance.ClaimWaitingPassengers(_stopPos, grabNow);
                    if (fare > 0 && _standing != null) _standing.AddEarnings(fare);
                    _grabbed += grabNow;
                }
                if (_grabbed >= leaveAfter || _campTimer >= maxCampTime) _state = State.Leaving;
                break;
            }

            case State.Leaving:
                // accelerate back to traffic flow; once moving, hand control back to normal cruising
                desiredSpeed = Mathf.Max(desiredSpeed, approachSpeed + 2f);
                if (self.speed > approachSpeed) { _haveStop = false; _state = State.Cruise; }
                break;
        }
    }

    // Re-query so a recenter never leaves us chasing a stale world position. Keeps the nearest stop ahead.
    bool RefreshStop(TrafficVehicle self)
    {
        if (SplineStopSpawner.Instance == null) return false;
        if (SplineStopSpawner.Instance.TryGetStopAhead(self.transform.position, self.transform.forward,
                                                       stopSeekRange + 10f, out Vector3 p, out int waiting) && waiting > 0)
        { _stopPos = p; return true; }
        // no waiting passengers left here (we or the player cleared it) → give up, go cruise
        return _haveStop && Vector3.Distance(self.transform.position, _stopPos) < campDistance + 3f;
    }

    // Outermost forward lane (toward the footpath where the stop is), via the vehicle's own band clamp later.
    float KerbLateral(TiledRoadStreamer road)
    {
        road.SampleBand(true, out float lo, out float hi);
        return hi;
    }

    void MaybeBlockPlayer(TrafficVehicle self, TiledRoadStreamer road, ref float desiredLat)
    {
        // player sits at metresFromBus == 0; we block when we're just ahead of them and close
        if (self.metresFromBus > 1f && self.metresFromBus < blockRange)
            desiredLat = road.BusLateral;       // slide in front of the player's lane
    }
}
