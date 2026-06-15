using UnityEngine;

/// Conductor 1 — the door conductor you control (toggle via RoleController). A billboard you move with
/// WASD (camera-relative). Press Grab to scoop the nearest waiting passenger and carry them; press
/// again (or Throw) to send them at the bus door to board — handy for recruiting the ones who weren't
/// going to board. When you're not controlling him he rides at the bus door (his "home"). No physics.
public class Conductor : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float grabRange = 3f;
    [Tooltip("How high above the conductor's head the carried passenger is held.")]
    public float carryHeight = 2.2f;
    [Tooltip("Feet height above the ground when running off the bus (nudge so the billboard's base sits ON the " +
             "ground, not in it / floating). His on-bus origin is the raised door anchor, so off-bus he's dropped " +
             "to the road surface + this offset.")]
    public float groundOffset = 0f;

    BillboardCharacter _view;
    Transform _home;
    Camera _cam;
    bool _controlled;
    bool _ai;                   // SOLO: this unmanned conductor auto-works (run out, grab, board)
    Passenger _held;
    Billboard _heldBillboard;   // we disable the carried one's billboard so we can hold it HORIZONTAL overhead

    // sprite-clip state: swap walk/run/grab sets only when the state CHANGES (SetWalk resets the frame index)
    enum Clip { None, Walk, Run, Grab }
    Clip _clip = Clip.None;
    void UpdateClipState(Clip want)
    {
        if (want == _clip || _view == null) return;
        _clip = want;
        Sprite[] frames = want switch
        {
            Clip.Run  => CharacterSprites.C1Run,
            Clip.Grab => CharacterSprites.C1Grab,
            _         => CharacterSprites.C1Walk,
        };
        if (frames != null) _view.SetWalk(frames, want == Clip.Run ? 0.06f : 0.09f);
    }

    public void Setup(BillboardCharacter view, Transform home)
    {
        _view = view;
        _home = home;
        _cam = Camera.main;
        ReturnHome();
    }

    /// True when the conductor is ON the bus (parented to the door anchor) — i.e. NOT running around outside.
    /// The bus speed-gate uses this: while C1 is off the bus, the bus is capped to a crawl so it can't leave
    /// him behind on the procedural road.
    public bool OnBus => transform.parent == _home && _home != null;

    public void SetControlled(bool on)
    {
        _controlled = on;
        if (on) _ai = false;                          // a human took over → stop the AI
        if (_cam == null) _cam = Camera.main;
        if (on)
        {
            transform.SetParent(null, true);          // detach from the bus to run around (now OnBus == false)
            DropToGround();                           // his on-bus origin is raised (the door height) — plant him
            if (_view != null) _view.ApplyHeight();
        }
        else ReturnHome();                            // re-parents to _home → OnBus == true
    }

    /// SOLO: enable/disable the auto-conductor brain for this unmanned role. While AI is on he runs out to grab
    /// nearby waiting passengers and feeds them to the door, then boards so the bus isn't speed-capped with no
    /// one to pick up. Switching the human INTO this role (SetControlled(true)) turns the AI off.
    public void SetAI(bool on)
    {
        if (_controlled) { _ai = false; return; }
        _ai = on;
        if (!on) ReturnHome();
    }

    // plant the conductor's feet on the ground (road surface). His ON-BUS origin is the raised door anchor, so
    // when detached he must be dropped to ground level or he floats. Ground ≈ the road surface Y at his position;
    // we use the bus's base Y as the reference (the bus sits on the road), which is flat in this game.
    void DropToGround()
    {
        var bus = BusController.Instance;
        if (bus == null) return;
        float groundY = bus.transform.position.y;     // bus root ≈ road surface; conductor stands on it
        Vector3 p = transform.position;
        p.y = groundY + groundOffset;
        transform.position = p;
    }

    void ReturnHome()
    {
        if (_held != null)
        {
            if (_heldBillboard != null) { _heldBillboard.enabled = true; _heldBillboard = null; }
            _held.transform.rotation = Quaternion.identity;
            _held.BeginBoarding(BusPassengers.Instance); _held = null;
        }
        if (_home != null)
        {
            transform.SetParent(_home, false);
            transform.localPosition = Vector3.zero;
            if (_view != null) _view.ApplyHeight();
        }
    }

    void Update()
    {
        if (BusController.GamePaused) return;          // shared pause freezes the conductor too
        if (_ai && !_controlled) { AiTick(); return; }
        if (!_controlled) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        GameInput gi = GameInput.Instance;
        Vector2 mv = gi.move.ReadValue<Vector2>();
        Vector3 fwd = _cam.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = _cam.transform.right; right.y = 0f; right.Normalize();
        Vector3 moveDir = fwd * mv.y + right * mv.x;
        if (moveDir.sqrMagnitude > 0.01f)
            transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;

        // stay planted on the ground while running (his on-bus origin sits at the raised DOOR height, so without
        // this he'd run around floating at door height).
        DropToGround();

        CarryHeld(fwd);

        // pick the sprite set: GRAB while carrying, RUN when moving (he always moves at run pace on foot), else WALK
        UpdateClipState(_held != null ? Clip.Grab : (moveDir.sqrMagnitude > 0.01f ? Clip.Run : Clip.Walk));

        if (gi.action.WasPressedThisFrame()) { if (_held == null) TryGrab(); else ThrowHeld(); }
        if (gi.altAction.WasPressedThisFrame()) ThrowHeld();

        // SHOUT to call passengers: while controlled + shouting into the mic, periodically recruit a nearby
        // waiting passenger to board the bus (louder = faster). Hands-free crowd-pulling, on top of manual Grab.
        ShoutToCall();
    }

    [Header("Shout-to-call (mic)")]
    [Tooltip("Range (m) within which a shout can call a waiting passenger to board.")]
    public float callRange = 14f;
    [Tooltip("At full mic loudness, seconds between auto-recruits (lower = more passengers).")]
    public float callIntervalAtFullShout = 0.8f;
    float _callTimer;

    void ShoutToCall()
    {
        var mic = MicInput.Instance;
        float loud = mic != null ? mic.Loudness : 0f;
        if (loud < 0.35f) { _callTimer = 0f; return; }      // not shouting → no calling

        _callTimer -= Time.deltaTime;
        if (_callTimer > 0f) return;
        // louder shout → shorter interval (more boarders); scale 0.35..1 loudness → slow..fast
        _callTimer = callIntervalAtFullShout / Mathf.Lerp(0.5f, 2.5f, Mathf.InverseLerp(0.35f, 1f, loud));

        // find the nearest waiting passenger in range and send them to board
        Passenger best = NearestWaiting(transform.position, callRange);
        if (best != null && BusPassengers.Instance != null) best.BeginBoarding(BusPassengers.Instance);
    }

    /// Nearest WAITING/GATHERING passenger within `range` of `from`. Iterates the PASSENGER POOL (cheap, no
    /// allocation) instead of a per-frame FindObjectsByType scene scan. Returns null if none / pool absent.
    static Passenger NearestWaiting(Vector3 from, float range)
    {
        var pool = PassengerPool.Instance;
        if (pool == null) return null;
        var all = pool.All;
        Passenger best = null; float bestSqr = range * range;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null) continue;
            if (p.state != Passenger.State.Waiting && p.state != Passenger.State.Gathering) continue;
            float d = (p.transform.position - from).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = p; }
        }
        return best;
    }

    void CarryHeld(Vector3 fwd)
    {
        // carry HORIZONTALLY overhead: above the head, sprite laid flat (lengthwise across the conductor).
        if (_held == null) return;
        _held.transform.position = transform.position + Vector3.up * carryHeight;
        _held.transform.rotation = Quaternion.LookRotation(Vector3.up, fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward);
    }

    // ---------- SOLO auto-conductor ----------
    [Header("Auto-conductor (solo)")]
    [Tooltip("Max passengers the AI grabs+throws per off-bus excursion before boarding again.")]
    public int aiMaxPickups = 5;
    [Tooltip("He only hops off when a bus stop is within this range AND the bus is below the speed cap.")]
    public float aiStopRange = 12f;
    [Tooltip("He only hops off when the bus speed is below this (km/h).")]
    public float aiHopOffSpeedKmh = 10f;

    Passenger _aiTarget;
    float _aiRescan;
    int _aiPickups;        // count of throws this excursion (reset when he boards)

    void AiTick()
    {
        var bus = BusController.Instance;
        if (bus == null) return;

        // animate the AI conductor too: grab while carrying, run while off-bus chasing, walk otherwise
        UpdateClipState(_held != null ? Clip.Grab : (!OnBus && _aiTarget != null ? Clip.Run : Clip.Walk));

        // carrying someone → THROW immediately from where he stands (the passenger arcs to the door + boards).
        // No walking to the door.
        if (_held != null)
        {
            ThrowHeld();
            _aiPickups++;
            return;
        }

        // hit the per-trip cap → board and let the bus run at full speed.
        if (_aiPickups >= Mathf.Max(1, aiMaxPickups))
        {
            if (!OnBus) ReturnHome();
            return;
        }

        // ON the bus: only LEAVE when it's safe — near a stop AND slow (≤ cap). Otherwise ride along.
        if (OnBus)
        {
            if (!CanHopOff(bus)) return;                 // stay aboard until we reach a slow stop
            // a fresh excursion begins
            _aiPickups = 0;
            _aiTarget = FindWaiting();
            if (_aiTarget == null) return;               // nothing to grab here → keep riding
            transform.SetParent(null, true);             // detach to run for them
            DropToGround();
        }

        // OFF the bus: chase the target and grab when close.
        _aiRescan -= Time.deltaTime;
        if ((_aiTarget == null ||
             (_aiTarget.state != Passenger.State.Waiting && _aiTarget.state != Passenger.State.Gathering))
            && _aiRescan <= 0f)
        { _aiTarget = FindWaiting(); _aiRescan = 0.4f; }

        if (_aiTarget != null)
        {
            MoveToward(_aiTarget.transform.position);
            if (PlanarDist(transform.position, _aiTarget.transform.position) < grabRange) TryGrab();
            return;
        }

        // ran out of reachable passengers → board (also lifts the speed gate).
        if (!OnBus) ReturnHome();
    }

    // safe to leave the bus: a stop is nearby AND the bus is below the hop-off speed (so he isn't left behind).
    bool CanHopOff(BusController bus)
    {
        if (bus.SpeedKmh > aiHopOffSpeedKmh) return false;
        var stops = SplineStopSpawner.Instance;
        if (stops == null) return false;
        return stops.IsNearStop(bus.transform.position, aiStopRange);
    }

    Passenger FindWaiting()
    {
        var bus = BusController.Instance; if (bus == null) return null;
        return NearestWaiting(bus.transform.position, 18f);   // pooled scan; reasonably close to the bus path
    }

    void MoveToward(Vector3 target)
    {
        Vector3 dir = target - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f) transform.position += dir.normalized * moveSpeed * Time.deltaTime;
        DropToGround();
    }

    static float PlanarDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

    void TryGrab()
    {
        Passenger best = NearestWaiting(transform.position, grabRange);
        if (best != null)
        {
            best.Grab();
            _held = best;
            // stop its billboard so we can hold it horizontal (the Billboard re-uprights it every frame otherwise)
            _heldBillboard = best.GetComponent<Billboard>();
            if (_heldBillboard != null) _heldBillboard.enabled = false;
        }
    }

    void ThrowHeld()
    {
        if (_held == null) return;
        if (_heldBillboard != null) { _heldBillboard.enabled = true; _heldBillboard = null; }   // re-upright on release
        _held.transform.rotation = Quaternion.identity;
        _held.ThrowTo(BusPassengers.Instance);   // arc to the door, then board on landing
        _held = null;
    }
}
