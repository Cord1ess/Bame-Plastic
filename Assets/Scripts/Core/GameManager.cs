using UnityEngine;

/// Top-level coordinator for Bame Plastic. Sits on the root "GameManager" object; each subsystem manager
/// (DayNight, Shift, Role) lives on a CHILD object holding its own script. GameManager finds them in its
/// children, exposes them through one access point (so other code asks GameManager instead of
/// FindObjectByType), and owns a simple game-state machine.
///
/// Recommended hierarchy:
///   GameManager            [GameManager]
///   ├── DayNight           [DayNightController]
///   ├── ShiftManager       [ShiftManager]
///   └── RoleController     [RoleController]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Boot, Playing, ShiftOver }

    [Header("State")]
    [SerializeField] GameState _state = GameState.Boot;
    public GameState State => _state;

    /// Fired whenever the game state changes (old, new). UI/audio/etc. can subscribe.
    public static System.Action<GameState, GameState> StateChanged;

    [Header("Managers (auto-found in children if left empty)")]
    public DayNightController dayNight;
    public ShiftManager shift;
    public RoleController role;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ResolveManagers();
    }

    void Start()
    {
        // Boot -> Playing once the scene is up. (Later: gate this behind an intro/loading screen.)
        SetState(GameState.Playing);
    }

    /// Find the child managers if not wired in the inspector. Children-first keeps it self-contained.
    public void ResolveManagers()
    {
        if (dayNight == null) dayNight = GetComponentInChildren<DayNightController>(true);
        if (shift == null) shift = GetComponentInChildren<ShiftManager>(true);
        if (role == null) role = GetComponentInChildren<RoleController>(true);
    }

    public void SetState(GameState next)
    {
        if (_state == next) return;
        GameState prev = _state;
        _state = next;
        StateChanged?.Invoke(prev, next);
    }

    // Convenience accessors so callers can do GameManager.Instance.Shift instead of FindAnyObjectByType.
    public DayNightController DayNight => dayNight;
    public ShiftManager Shift => shift;
    public RoleController Role => role;
}
