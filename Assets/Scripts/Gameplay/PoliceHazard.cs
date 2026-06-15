using System.Collections.Generic;
using UnityEngine;

/// TRAFFIC POLICE checkpoint hazard. Every ~60 s a police checkpoint appears on the road (placed road-relative
/// like the bus stops, so it's floating-origin-safe + deterministic). A BIG overhead warning marker tells the
/// driver to slow to <= speedLimitKmh (45). If the bus passes the checkpoint ABOVE the limit, the bus HARD-STOPS
/// instantly and a fine (default 500 Tk) is deducted (earnings may go negative).
///
/// PLAY-ONLY (no [ExecuteAlways]; builds in Start, tears down in OnDestroy). Auto-added by
/// "Bame Plastic > Create Tiled Road (Fast)". Container name "PoliceHazards" is registered with
/// TransientUICleaner so a stray one can never persist into the scene.
///
/// MULTIPLAYER: the checkpoint POSITION is road-relative + seed-deterministic, so it's identical on every client
/// with no sync. The SPEED GATE (hard-stop + fine) is a driver-authoritative OUTCOME — only the driver evaluates
/// it; the resulting earnings drop replicates via GameNet's EarningsSync and the stop via the synced BusState.
[RequireComponent(typeof(TiledRoadStreamer))]
public class PoliceHazard : MonoBehaviour
{
    public static PoliceHazard Instance { get; private set; }

    /// Fired (driver/solo) when the bus is fined for speeding past a checkpoint — the fine amount. HUD pings it.
    public static System.Action<int> Fined;

    [Header("Spawn cadence")]
    [Tooltip("Seconds between police checkpoints.")]
    public float spawnIntervalSec = 60f;
    [Tooltip("Random +/- jitter (s) on the interval so it isn't metronomic.")]
    public float spawnIntervalJitter = 8f;
    [Tooltip("How far AHEAD of the bus a new checkpoint is placed (m). Keep beyond the smog so it fades in, " +
             "never pops in. Also gives the driver time to read the warning + slow down.")]
    public float spawnAhead = 200f;
    [Tooltip("Recycle a checkpoint once the bus is this far PAST it (m behind).")]
    public float cullBehind = 60f;
    [Tooltip("Max live checkpoints at once.")]
    public int maxLive = 2;

    [Header("Speed gate")]
    [Tooltip("Speed limit through the checkpoint (km/h). Above this when you cross = fined + hard-stopped.")]
    public float speedLimitKmh = 45f;
    [Tooltip("Fine deducted for speeding past (Tk). Earnings may go negative.")]
    public int fine = 500;
    [Tooltip("Half-length of the trigger zone around the checkpoint point (m). The cross is detected when the " +
             "bus passes from ahead-of to behind-of the checkpoint within this band.")]
    public float zoneHalfLength = 6f;

    [Header("Debug")]
    [Tooltip("Press to force-spawn a checkpoint right ahead (verify visuals without waiting). None = off.")]
    public KeyCode debugSpawnKey = KeyCode.P;
    [Tooltip("Log spawn/recycle to the Console.")]
    public bool debugLog = false;

    [Header("Figures + marker")]
    [Tooltip("Lateral inset from the lane edge for the two officers (m), on the LEFT (driving) side.")]
    public float officerInset = 1.0f;
    [Tooltip("Officer billboard height (m).")]
    public float officerHeight = 1.85f;
    [Tooltip("Height of the warning marker above the road (m). Tall so it reads over the smog/traffic.")]
    public float markerHeight = 6.0f;
    [Tooltip("Marker quad size (m).")]
    public float markerSize = 3.2f;
    public Color warnColor = new Color(1f, 0.85f, 0.15f, 1f);   // amber: warning
    public Color okColor   = new Color(0.3f, 0.9f, 0.4f, 1f);   // green: you're slow enough
    public Color overColor = new Color(1f, 0.2f, 0.15f, 1f);    // red: too fast, you'll be fined

    class Checkpoint
    {
        public GameObject root;          // container parented to the road (rides curves + floating origin)
        public WorldSign marker;         // overhead "SLOW · 45" warning text sign
        public BillboardCharacter male, female;
        public float metres;             // signed distance ahead of the bus (decays each frame)
        public float lateralOfficerL, lateralOfficerR;
        public bool triggered;           // fined/cleared once — don't re-evaluate
        public float lastMetres;         // previous frame's metres (to detect the crossing of 0)
    }

    TiledRoadStreamer _road;
    Transform _parent;
    readonly List<Checkpoint> _live = new List<Checkpoint>();
    readonly Stack<Checkpoint> _free = new Stack<Checkpoint>();
    float _spawnTimer;

    // SELF-ATTACH: guarantee the hazard exists on the road even if the scene was saved before this component was
    // added (the editor "Create Tiled Road" menu adds it, but an already-saved scene won't have it). Mirrors how
    // StopRequestIndicator self-attaches — so no scene re-save or menu run is required.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        var road = Object.FindAnyObjectByType<TiledRoadStreamer>();
        if (road != null && road.GetComponent<PoliceHazard>() == null)
            road.gameObject.AddComponent<PoliceHazard>();
    }

    void Awake()
    {
        Instance = this;
        _road = GetComponent<TiledRoadStreamer>();
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("PoliceHazards");
        go.hideFlags = HideFlags.DontSave;
        _parent = go.transform;
        SceneHierarchy.Parent(go, SceneHierarchy.Category.World);
        // first checkpoint comes fairly soon (not a full interval) so the hazard is visible early in a shift.
        _spawnTimer = Mathf.Min(spawnIntervalSec, 20f);
        CharacterSprites.Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_parent != null) Destroy(_parent.gameObject);
    }

    /// Floating-origin: checkpoints are parented to the road + tracked road-relative (metres), so the generic
    /// root shift moves them with the world and metres is unaffected. No-op, kept for interface symmetry.
    public void OnOriginShifted(Vector3 delta) { }

    void Update()
    {
        if (_road == null) return;
        float busSpeedMps = BusController.Instance != null ? BusController.Instance.SpeedMps : 0f;
        float dt = Time.deltaTime;

        // DEBUG: force a checkpoint right ahead with the P key (verify visuals/gate without waiting for the timer).
        if (debugSpawnKey != KeyCode.None && Input.GetKeyDown(debugSpawnKey)) TrySpawn();

        // advance the spawn timer only while the shift is actually running
        bool running = ShiftManager.Instance == null || ShiftManager.Instance.IsRunning;
        if (running)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = Mathf.Max(8f, spawnIntervalSec + Random.Range(-spawnIntervalJitter, spawnIntervalJitter));
                TrySpawn();
            }
        }

        // tick live checkpoints
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var cp = _live[i];
            cp.lastMetres = cp.metres;
            cp.metres -= busSpeedMps * dt;   // static in the world → recedes relative to the moving bus

            // reposition along the road (rides curves + floating origin)
            if (_road.SampleRoad(cp.metres, 0f, out Vector3 cpos, out Vector3 fwd, out Vector3 right))
            {
                PlaceFigures(cp, cpos, fwd, right);
                EvaluateGate(cp, busSpeedMps);
                UpdateMarker(cp, busSpeedMps);
            }

            if (cp.metres < -cullBehind) { Recycle(cp); _live.RemoveAt(i); }
        }
    }

    void TrySpawn()
    {
        if (_live.Count >= maxLive) { if (debugLog) Debug.Log("[PoliceHazard] skip: at maxLive"); return; }
        if (_road.MetresAhead < spawnAhead * 0.5f)            // not enough road built yet — try again next interval
        {
            if (debugLog) Debug.Log($"[PoliceHazard] skip: MetresAhead {_road.MetresAhead:0} < {spawnAhead * 0.5f:0}");
            return;
        }
        var cp = _free.Count > 0 ? _free.Pop() : NewCheckpoint();
        cp.metres = spawnAhead;
        cp.lastMetres = spawnAhead;
        cp.triggered = false;
        // officers flank the LEFT (driving) lane edge; under LHT forward lanes are on -X (negative lateral).
        var zone = _road.Zone;
        float edge = zone != null ? zone.DriveHalf : 7f;
        cp.lateralOfficerL = -(edge - officerInset);            // just inside the kerb on our side
        cp.lateralOfficerR = -(edge * 0.35f);                   // toward the lane centre (waving you down)
        if (cp.root != null) cp.root.SetActive(true);
        _live.Add(cp);
        if (debugLog) Debug.Log($"[PoliceHazard] spawned checkpoint {spawnAhead:0}m ahead (live={_live.Count})");
    }

    Checkpoint NewCheckpoint()
    {
        var cp = new Checkpoint();
        var root = new GameObject("Checkpoint");
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(_parent, false);
        cp.root = root;

        cp.male   = BillboardCharacter.Create("Police_M", new Color(0.2f, 0.25f, 0.5f), officerHeight, Vector3.zero, root.transform);
        cp.female = BillboardCharacter.Create("Police_F", new Color(0.3f, 0.3f, 0.55f), officerHeight, Vector3.zero, root.transform);
        if (CharacterSprites.PoliceMale   != null) cp.male.SetSprite(CharacterSprites.PoliceMale);
        if (CharacterSprites.PoliceFemale != null) cp.female.SetSprite(CharacterSprites.PoliceFemale);

        BuildMarker(cp, root.transform);
        return cp;
    }

    void BuildMarker(Checkpoint cp, Transform parent)
    {
        // an ICON badge ("!" police warning) with a small text label above — build-safe (no URP shader lookup).
        cp.marker = WorldSign.Create(parent, "PoliceWarning", WorldSign.Icon.Police, markerSize * 0.9f);
        cp.marker.SetText("SLOW 45");
    }

    void PlaceFigures(Checkpoint cp, Vector3 cpos, Vector3 fwd, Vector3 right)
    {
        Vector3 up = Vector3.up;
        // two officers across the checkpoint point
        if (_road.SampleRoad(cp.metres, cp.lateralOfficerL, out Vector3 mp, out _, out _))
            { mp.y = cpos.y; cp.male.transform.position = mp; }
        if (_road.SampleRoad(cp.metres + 1.5f, cp.lateralOfficerR, out Vector3 fp, out _, out _))
            { fp.y = cpos.y; cp.female.transform.position = fp; }

        // overhead sign centred over the lane, floating + billboarded toward the camera
        cp.marker.transform.position = cpos + up * markerHeight;
        cp.marker.FaceCamera();
    }

    void UpdateMarker(Checkpoint cp, float busSpeedMps)
    {
        if (cp.marker == null) return;
        float kmh = Mathf.Abs(busSpeedMps) * 3.6f;
        Color c;
        if (cp.triggered) c = okColor;                                  // already passed — neutral green
        else if (cp.metres > 60f) c = warnColor;                       // far off → just a warning
        else c = kmh > speedLimitKmh ? overColor : okColor;            // close → red if speeding, green if safe
        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
        cp.marker.SetColor(c * pulse);
    }

    // DRIVER-AUTHORITATIVE: only the local driver (or solo) evaluates the gate. A conductor proxy bus doesn't
    // own the real speed, and the earnings/stop replicate from the driver anyway.
    void EvaluateGate(Checkpoint cp, float busSpeedMps)
    {
        if (cp.triggered) return;
        var gn = BamePlastic.Net.GameNet.Instance;
        if (gn != null && gn.Active && !gn.IsDriver) return;   // non-driver: marker only, no judging

        // the bus CROSSES the checkpoint when metres goes from >0 (ahead) to <=0 (behind) within the zone band
        bool crossed = cp.lastMetres > 0f && cp.metres <= 0f && Mathf.Abs(cp.metres) <= zoneHalfLength + Mathf.Abs(busSpeedMps) * Time.deltaTime;
        // also catch a fast pass that overshoots the band in one frame
        if (!crossed && cp.lastMetres > 0f && cp.metres < -zoneHalfLength && cp.lastMetres < zoneHalfLength * 2f) crossed = true;
        if (!crossed) return;

        cp.triggered = true;
        float kmh = Mathf.Abs(busSpeedMps) * 3.6f;
        if (kmh > speedLimitKmh + 0.5f)
        {
            if (BusController.Instance != null) BusController.Instance.HardStop();
            if (ShiftManager.Instance != null) ShiftManager.Instance.AddEarnings(-Mathf.Abs(fine));
            Sfx.Play("police_whistle", 0.9f);   // no-op if the clip isn't present
            Fined?.Invoke(Mathf.Abs(fine));
        }
    }

    void Recycle(Checkpoint cp)
    {
        if (cp.root != null) cp.root.SetActive(false);
        _free.Push(cp);
    }
}
