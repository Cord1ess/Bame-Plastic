using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using BamePlastic.Net;

/// The shift = the run. Owns the day->night clock, YOUR earnings + bus health, and the rival
/// company-bus leaderboard. This is the single source of truth the HUD reads and (later) the
/// Spring Boot backend mirrors — gameplay systems push into it via AddEarnings()/Damage().
///
/// SETUP: lives on a "ShiftManager" child of the GameManager object (see GameManager). It auto-finds
/// the DayNightController, auto-generates rivals if none are set, and spawns the HUD itself.
public class ShiftManager : MonoBehaviour
{
    public static ShiftManager Instance { get; private set; }

    [Header("Shift")]
    [Tooltip("Length of one shift (day->night) in seconds. 600 = 10 minutes. Lower it (e.g. 45) to test the end-of-shift summary quickly.")]
    public float shiftDuration = 600f;
    public string playerName = "You";
    [Tooltip("Start the shift immediately on scene load, bypassing the living menu (testing shortcut). " +
             "Leave OFF so the scene opens as the menu.")]
    public bool autoStart = false;

    [Header("Bus health")]
    public float maxHealth = 100f;

    [Header("Rivals (other company buses)")]
    [Tooltip("Leave empty to auto-generate a few Dhaka-flavoured rivals at start.")]
    public List<RivalBus> rivals = new List<RivalBus>();

    [Header("Day/Night tie-in")]
    [Tooltip("Optional — auto-found if left empty. The shift drives this cycle so dusk == shift end.")]
    public DayNightController dayNight;
    [Range(0f, 1f)] public float startTimeOfDay = 0.24f;  // morning
    [Range(0f, 1f)] public float endTimeOfDay = 0.86f;    // night

    [Header("Placeholder income (TEMP — superseded by collect-or-lose-it passenger fares)")]
    [Tooltip("OFF for real play: passive taka masks the collect-or-lose-it fare loop (a fare is income ONLY if a " +
             "conductor collects it before the rider leaves). Turn ON only to keep the loop playable while testing " +
             "without conductors.")]
    public bool enablePlaceholderIncome = false;
    [Tooltip("Slow passive taka while the shift runs, so the loop is playable before passengers exist.")]
    public float passiveTakaPerSecond = 7f;
    [Tooltip("Press to simulate collecting a fare.")]
    public KeyCode debugEarnKey = KeyCode.E;
    public int debugFareAmount = 25;
    [Tooltip("Press to simulate crash damage (test the health bar).")]
    public KeyCode debugDamageKey = KeyCode.H;
    public float debugDamageAmount = 10f;

    [Header("Restart")]
    public KeyCode restartKey = KeyCode.R;

    // ---- Runtime state (read by the HUD) ----
    public float TimeRemaining { get; private set; }
    public int Earnings { get; private set; }
    public float Health { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsOver { get; private set; }

    float _takaAccumulator;
    float _earningsFloat;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        // Initialise idle — the shift does NOT auto-run. The scene opens in MENU mode (living backdrop); the
        // clock/HUD/rivals only start when MenuMode calls BeginShift() on Start/Solo. autoStart lets you run
        // the game scene directly (no menu) for testing.
        TimeRemaining = shiftDuration;
        Health = maxHealth;
        Earnings = 0;
        _earningsFloat = 0f;
        IsRunning = false;
        IsOver = false;

        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightController>();
        // hold the sky at the opening sunrise during the menu
        if (dayNight != null) { dayNight.externalTimeControl = true; dayNight.SetShiftProgress(0f); }

        if (autoStart) BeginShift();   // testing shortcut: run the shift immediately, bypassing the menu
    }

    /// Begin the actual shift: start the clock, reset rivals, spawn the HUD. Called by MenuMode when the
    /// player hits Start/Solo (the menu→play transition), or on Start if autoStart is on.
    public void BeginShift()
    {
        TimeRemaining = shiftDuration;
        Health = maxHealth;
        Earnings = 0;
        _earningsFloat = 0f;
        FaresCollected = 0;
        IsRunning = true;
        IsOver = false;

        // ALWAYS rebuild the canonical 5 standings at the start of every shift, so a stale serialized list (or a
        // RivalBrain that linked an old-named entry last run) can never leave wrong names on the board.
        GenerateDefaultRivals();
        foreach (var r in rivals) r.ResetEarnings();
        if (dayNight != null) { dayNight.externalTimeControl = true; dayNight.SetShiftProgress(0f); }

        if (FindAnyObjectByType<ShiftHud>() == null)
        {
            GameObject hud = new GameObject("ShiftHUD");
            hud.AddComponent<ShiftHud>();
            SceneHierarchy.Parent(hud, SceneHierarchy.Category.UI);
        }
        SpeedometerHud.Spawn();   // gameplay HUD — only now (never during the menu)
    }

    void Update()
    {
        if (IsOver)
        {
            if (Input.GetKeyDown(restartKey)) RestartShift();
            return;
        }
        if (!IsRunning) return;

        float dt = Time.deltaTime;

        // Clock
        TimeRemaining -= dt;
        if (TimeRemaining <= 0f) { TimeRemaining = 0f; EndShift(); return; }

        // Day/night follows the SHIFT SCHEDULE (quick sunrise → long sunny day → dusk → short night),
        // shaped by DayNightController's phase fractions — not a flat lerp.
        if (dayNight != null)
        {
            float progress = 1f - (TimeRemaining / shiftDuration);
            dayNight.SetShiftProgress(progress);
        }

        // Rivals tick — adaptive rubber-band against the PLAYER's current earnings (they chase + pressure you).
        for (int i = 0; i < rivals.Count; i++) rivals[i].Tick(dt, Earnings);
        UpdateRivalWarning();

        // Placeholder income / debug (removed once real passengers feed AddEarnings)
        if (enablePlaceholderIncome)
        {
            _takaAccumulator += passiveTakaPerSecond * dt;
            if (_takaAccumulator >= 1f)
            {
                int whole = Mathf.FloorToInt(_takaAccumulator);
                _takaAccumulator -= whole;
                AddEarnings(whole);
            }
            if (Input.GetKeyDown(debugEarnKey)) AddEarnings(debugFareAmount);
            if (Input.GetKeyDown(debugDamageKey)) Damage(debugDamageAmount);
        }
    }

    /// Fired when a passenger leaves WITHOUT their fare collected — the lost amount. HUD subscribes for a ping.
    public static System.Action<int> FareLost;
    /// Called by Passenger when an unpaid rider alights (driver/solo only). Routes the lost-fare signal to the HUD.
    public static void ReportFareLost(int amount) { if (amount > 0) FareLost?.Invoke(amount); }

    /// Number of fares collected this shift (career-tracked at shift end for achievements).
    public int FaresCollected { get; private set; }
    /// Called when a conductor successfully collects a fare (driver/solo authoritative).
    public void ReportFareCollected() { FaresCollected++; }

    // ---- Public API (the surface the backend will mirror) ----
    /// Add (or, with a negative amount, deduct — e.g. a police fine) taka. Earnings may go NEGATIVE (a bad shift
    /// can leave you in the red); only the displayed int is clamped to a sane range, not floored at zero.
    public void AddEarnings(int taka)
    {
        if (taka == 0) return;
        _earningsFloat += taka;
        Earnings = Mathf.RoundToInt(_earningsFloat);
    }

    public void Damage(float amount) => Health = Mathf.Clamp(Health - amount, 0f, maxHealth);
    public void Repair(float amount) => Health = Mathf.Clamp(Health + amount, 0f, maxHealth);

    /// MULTIPLAYER (conductor clients): the DRIVER is authoritative for earnings/health; this overwrites the
    /// local HUD numbers with the driver's truth (sent low-rate via GameNet EarningsSync).
    public void SetFromNetwork(int earnings, int health)
    {
        Earnings = earnings; _earningsFloat = earnings;
        Health = Mathf.Clamp(health, 0f, maxHealth);
    }

    void EndShift()
    {
        IsRunning = false;
        IsOver = true;
        ReportResult();
    }

    /// LEAVE SHIFT → back to the living menu IN PLACE (no scene reload). Reset the run to idle and tear down the
    /// gameplay-only HUDs so MenuMode can rebuild the menu cleanly at the current spot. Does NOT report/award
    /// (you abandoned the shift). Day/night holds at the menu's sunrise again.
    public void EndToMenu()
    {
        IsRunning = false;
        IsOver = false;
        _reported = false;
        TimeRemaining = shiftDuration;
        Earnings = 0; _earningsFloat = 0f;
        Health = maxHealth;
        FaresCollected = 0;
        if (dayNight != null) { dayNight.externalTimeControl = true; dayNight.SetShiftProgress(0f); }

        // tear down the gameplay HUDs (rebuilt next BeginShift). Find by type so we don't hold stale refs.
        var hud = FindAnyObjectByType<ShiftHud>(); if (hud != null) Destroy(hud.gameObject);
        var speedo = FindAnyObjectByType<SpeedometerHud>(); if (speedo != null) Destroy(speedo.gameObject);
    }

    bool _reported;
    /// At shift end: award the run's earnings to the logged-in account (career taka + Bhara) and post the result
    /// to the leaderboard. In MULTIPLAYER only the DRIVER reports the shared bus earnings (avoids triple-count);
    /// each player still awards to their OWN account via the driver-confirmed earnings shown on their HUD.
    void ReportResult()
    {
        if (_reported) return;
        _reported = true;

        var gn = BamePlastic.Net.GameNet.Instance;
        bool mpNonDriver = gn != null && gn.Active && !gn.IsDriver;

        // every human credits their own wallet with the run's earnings (Bhara conversion happens server-side).
        // Also report career stats (fares collected, finished #1) so the server can unlock achievements.
        if (PlayerAccount.LoggedIn && Earnings > 0)
            PlayerAccount.AwardEarnings(Earnings, FaresCollected, PlayerRank() == 1);

        // leaderboard post — driver (or solo) only, so a co-op shift logs one shared result
        if (!mpNonDriver) PostLeaderboard();
    }

    void PostLeaderboard()
    {
        int elapsed = Mathf.RoundToInt(Mathf.Max(0f, shiftDuration - TimeRemaining));
        string room = SessionContext.Instance != null && SessionContext.Instance.Room != null
                      ? SessionContext.Instance.Room.code : "";
        // best-effort fire-and-forget POST to the leaderboard.
        BamePlastic.Net.LeaderboardApi.PostResult(playerName, Earnings, Mathf.RoundToInt(Health), elapsed, room);
    }

    void RestartShift()
    {
        Instance = null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---- Leaderboard ----
    public struct Standing
    {
        public string name;
        public int taka;
        public bool isPlayer;
    }

    static readonly List<Standing> _standings = new List<Standing>();
    public List<Standing> GetStandings()
    {
        _standings.Clear();
        _standings.Add(new Standing { name = playerName, taka = Earnings, isPlayer = true });
        for (int i = 0; i < rivals.Count; i++)
            _standings.Add(new Standing { name = rivals[i].name, taka = rivals[i].Taka, isPlayer = false });
        _standings.Sort((a, b) => b.taka.CompareTo(a.taka));
        return _standings;
    }

    public int PlayerRank()
    {
        var s = GetStandings();
        for (int i = 0; i < s.Count; i++) if (s[i].isPlayer) return i + 1;
        return s.Count;
    }

    void GenerateDefaultRivals()
    {
        // 5 named company buses with different aggression so the pack SPREADS around the player: a couple tend to
        // lead (aggression > 1, the ones to beat), a couple trail, one sits near parity. The adaptive Tick makes
        // each surge/ease relative to YOUR earnings, so the board stays a live race all shift.
        rivals = new List<RivalBus>
        {
            new RivalBus { name = "Balaka",         aggression = 1.18f, baseEarnPerSec = 7f },
            new RivalBus { name = "Victor Classic", aggression = 1.05f, baseEarnPerSec = 6.5f },
            new RivalBus { name = "Raida",          aggression = 0.95f, baseEarnPerSec = 6f },
            new RivalBus { name = "Mirpur Link",    aggression = 0.85f, baseEarnPerSec = 5.5f },
            new RivalBus { name = "Osim",           aggression = 0.72f, baseEarnPerSec = 5f },
        };
    }

    // ---- "a rival is ahead" warning (HUD reads RivalAhead / TopRivalName) ----
    /// True when the leading rival is earning more than the player — the HUD flashes a warning.
    public bool RivalAhead { get; private set; }
    /// The name + taka of the current leader (for the warning text).
    public string LeaderName { get; private set; }
    public int LeaderTaka { get; private set; }

    void UpdateRivalWarning()
    {
        int bestTaka = Earnings; string bestName = playerName; bool playerLeads = true;
        for (int i = 0; i < rivals.Count; i++)
        {
            if (rivals[i].Taka > bestTaka) { bestTaka = rivals[i].Taka; bestName = rivals[i].name; playerLeads = false; }
        }
        RivalAhead = !playerLeads;
        LeaderName = bestName;
        LeaderTaka = bestTaka;
    }
}
