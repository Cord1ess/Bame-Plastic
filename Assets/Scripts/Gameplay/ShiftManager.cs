using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("Placeholder income (TEMP — replaced by passenger fares in step 2)")]
    public bool enablePlaceholderIncome = true;
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
        IsRunning = true;
        IsOver = false;

        if (rivals == null || rivals.Count == 0) GenerateDefaultRivals();
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

        // Rivals tick
        for (int i = 0; i < rivals.Count; i++) rivals[i].Tick(dt);

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

    // ---- Public API (the surface the backend will mirror) ----
    public void AddEarnings(int taka)
    {
        if (taka == 0) return;
        _earningsFloat += taka;
        if (_earningsFloat < 0f) _earningsFloat = 0f;
        Earnings = Mathf.RoundToInt(_earningsFloat);
    }

    public void Damage(float amount) => Health = Mathf.Clamp(Health - amount, 0f, maxHealth);
    public void Repair(float amount) => Health = Mathf.Clamp(Health + amount, 0f, maxHealth);

    void EndShift()
    {
        IsRunning = false;
        IsOver = true;
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
        rivals = new List<RivalBus>
        {
            new RivalBus { name = "Raida",       fareInterval = 3.5f },
            new RivalBus { name = "Boishakhi",   fareInterval = 4.0f },
            new RivalBus { name = "Projapoti",   fareInterval = 5.0f },
            new RivalBus { name = "Mirpur Link", fareInterval = 3.2f },
        };
    }
}
