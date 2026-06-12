using UnityEngine;

/// L4 — the competitive RIVAL behaviour, layered on a normal TrafficVehicle via IVehicleBrain. The vehicle does
/// all the driving (road-relative motion, curve-following, collision, band-clamping) exactly like ambient
/// traffic; this brain only adds INTENT:
///   • seek the nearest bus stop ahead with waiting passengers,
///   • QUEUE one-behind-another with the other rivals camping that stop (a convoy lined up at the kerb trying to
///     scoop the crowd), only the FRONT rival grabbing, the rest crawling in line,
///   • grab the waiting passengers (real taka into its ShiftManager standings), leave when loaded → the next
///     advances,
///   • aggressively BLOCK/overtake the player (drift into your lane, sit in front, run a bit faster).
/// Because it reuses the traffic vehicle it can never "go all over the place" — same deterministic road space.
public class RivalBrain : MonoBehaviour, TrafficVehicle.IVehicleBrain
{
    enum State { Cruise, Approach, Camp, Leaving }

    [Header("Identity")]
    public string rivalName = "Sonar Bangla";

    [Header("Compete")]
    [Tooltip("Look this far ahead (m, along the road) for a stop to camp.")]
    public float stopSeekRange = 90f;
    [Tooltip("World distance (m) from the stop the FRONT rival pulls in at to grab passengers.")]
    public float campDistance = 7f;
    [Tooltip("Spacing (m) between queued rivals lined up behind the stop — about a bus length.")]
    public float queueSpacing = 11f;
    [Tooltip("Passengers grabbed per second while camping (front rival only).")]
    public float grabRate = 4f;
    [Tooltip("Leave once it has grabbed at least this many (then drives off, freeing the slot).")]
    public int leaveAfter = 8;
    [Tooltip("Max seconds it will camp before leaving regardless.")]
    public float maxCampTime = 5f;
    [Tooltip("Crawl speed (m/s) while approaching / waiting in the queue.")]
    public float approachSpeed = 4f;

    [Header("Aggression")]
    [Tooltip("If the player is just ahead/behind within this many m, drift into their lane to block + overtake.")]
    public float blockRange = 24f;
    [Tooltip("Cruise SPEED MULTIPLIER vs its base — >1 so rivals keep up / overtake the player (annoying).")]
    public float aggressionSpeedMul = 1.25f;

    // --- queue state exposed so other rivals can line up behind me ---
    public bool Targeting { get; private set; }
    public Vector3 TargetStopPos { get; private set; }
    public float DistToStop { get; private set; }

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
        if (_standing == null) { _standing = new RivalBus { name = rivalName, fareInterval = 4f }; sm.rivals.Add(_standing); }
        _standing.drivenByAgent = true;
    }

    // Called by TrafficVehicle.Tick AFTER its own avoidance — we rewrite intent on top.
    public void Decide(TrafficVehicle self, float dt, ref float desiredSpeed, ref float desiredLat)
    {
        var road = self.Road;
        if (road == null) return;
        if (_standing == null) LinkStanding();

        Targeting = _state != State.Cruise;
        TargetStopPos = _stopPos;
        DistToStop = _haveStop ? Vector3.Distance(self.transform.position, _stopPos) : 9999f;

        switch (_state)
        {
            case State.Cruise:
                desiredSpeed = Mathf.Max(desiredSpeed, self.Cruise * aggressionSpeedMul);   // run hot — overtake
                MaybeBlockPlayer(self, road, ref desiredSpeed, ref desiredLat);
                if (self.dir > 0 && SplineStopSpawner.Instance != null &&
                    SplineStopSpawner.Instance.TryGetStopAhead(self.transform.position, self.transform.forward,
                                                               stopSeekRange, out _stopPos, out int waiting) && waiting > 0)
                { _haveStop = true; _state = State.Approach; }
                break;

            case State.Approach:
            {
                if (!RefreshStop(self)) { _state = State.Cruise; _haveStop = false; break; }
                desiredLat = KerbLateral(road);                                  // ease toward the footpath lane

                int slot = QueueIndex(self);                                     // 0 = front (camps); 1,2.. = behind
                float slotDist = campDistance + slot * queueSpacing;            // pull up THIS far back → lined up
                float d = Vector3.Distance(self.transform.position, _stopPos);

                // crawl as we close on our slot; gun it if we're still far back in the queue
                float t = Mathf.InverseLerp(slotDist, stopSeekRange, d);
                desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Lerp(approachSpeed, self.Cruise, t));

                if (d <= slotDist + 1.5f)
                {
                    if (slot == 0) { _state = State.Camp; _campTimer = 0f; _grabbed = 0; _grabAccum = 0f; }
                    else desiredSpeed = 0f;                                      // hold our place in line (crawl/stop)
                }
                break;
            }

            case State.Camp:
            {
                desiredSpeed = 0f;                                              // pulled up at the kerb
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
                desiredSpeed = Mathf.Max(desiredSpeed, approachSpeed + 3f);     // pull away, free the slot
                if (self.speed > approachSpeed) { _haveStop = false; _state = State.Cruise; }
                break;
        }
    }

    // my position in the queue for this stop: how many OTHER rivals target the SAME stop and are CLOSER to it.
    int QueueIndex(TrafficVehicle self)
    {
        var ts = TrafficSystem.Instance;
        if (ts == null) return 0;
        int idx = 0;
        var rivals = ts.Rivals;
        for (int i = 0; i < rivals.Count; i++)
        {
            var v = rivals[i];
            if (v == null || v == self) continue;
            var b = v.GetComponent<RivalBrain>();
            if (b == null || b == this || !b.Targeting) continue;
            if ((b.TargetStopPos - _stopPos).sqrMagnitude > 64f) continue;      // a different stop (>8m apart)
            if (b.DistToStop < DistToStop - 0.5f) idx++;                        // they're ahead of me in line
        }
        return idx;
    }

    // Re-query so a recenter never leaves us chasing a stale world position. Keeps the nearest stop ahead.
    bool RefreshStop(TrafficVehicle self)
    {
        if (SplineStopSpawner.Instance == null) return false;
        if (SplineStopSpawner.Instance.TryGetStopAhead(self.transform.position, self.transform.forward,
                                                       stopSeekRange + 15f, out Vector3 p, out int waiting) && waiting > 0)
        { _stopPos = p; return true; }
        return _haveStop && Vector3.Distance(self.transform.position, _stopPos) < campDistance + 4f;
    }

    float KerbLateral(TiledRoadStreamer road)
    {
        road.SampleBand(true, out float lo, out float hi);
        return hi;
    }

    // AGGRESSIVE blocking: when the player is just ahead or behind and close, slide into their lane and (if just
    // ahead) ease off the throttle to sit in front of them — annoying, taking your line.
    float _nextHornAt;

    void MaybeBlockPlayer(TrafficVehicle self, TiledRoadStreamer road, ref float desiredSpeed, ref float desiredLat)
    {
        float m = self.metresFromBus;                       // player sits at metresFromBus == 0
        bool blocking = false;
        if (m > 0.5f && m < blockRange)
        {
            desiredLat = road.BusLateral;                   // slide in front of the player's lane
            if (m < blockRange * 0.5f) desiredSpeed = Mathf.Min(desiredSpeed, BusController.Instance != null
                ? Mathf.Max(approachSpeed, BusController.Instance.SpeedMps * 0.92f) : desiredSpeed);  // sit just ahead
            blocking = true;
        }
        else if (m < 0f && m > -blockRange * 0.6f)
        {
            desiredLat = road.BusLateral;                   // player overtaking → drift back across to re-block
            blocking = true;
        }

        // annoying RIVAL HORN while it's bullying the player — honks OFTEN (this is the harassment).
        if (blocking && Time.time >= _nextHornAt && Mathf.Abs(m) < blockRange)
        {
            _nextHornAt = Time.time + Random.Range(1.0f, 2.2f);
            Sfx.PlayAt("horn_rival", self.transform.position, 0.6f, 0.02f);
        }
    }
}
