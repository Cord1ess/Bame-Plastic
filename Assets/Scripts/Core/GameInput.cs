using UnityEngine;
using UnityEngine.InputSystem;

/// Central input for the game on the NEW Input System, defined in code so there's no .inputactions
/// asset to author/wire — keyboard AND gamepad both work immediately. Two action sets:
///   • Driving  — accelerate / brake / steer / drift   (the bus)
///   • OnFoot   — move / action / altAction            (the conductors)
/// RoleController enables exactly ONE set at a time, so input can never leak to the bus while you're
/// controlling a conductor (that's the "bus moves while conducting" fix). ToggleRole is always on.
///
/// Auto-creates itself on first access — you don't place it in the scene. To rebind later, this is the
/// one file to edit (or we can swap to an .inputactions asset without touching the gameplay scripts).
public class GameInput : MonoBehaviour
{
    static GameInput _instance;
    public static GameInput Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameInput>();
                if (_instance == null)
                {
                    var go = new GameObject("GameInput");
                    _instance = go.AddComponent<GameInput>();
                    SceneHierarchy.Parent(go, SceneHierarchy.Category.Logic);
                }
            }
            return _instance;
        }
    }

    // Driving
    public InputAction accelerate;
    public InputAction brake;
    public InputAction steer;   // -1..1
    public InputAction drift;

    // On foot (conductors)
    public InputAction move;    // Vector2
    public InputAction action;  // grab / throw / haggle
    public InputAction altAction; // throw

    // Global
    public InputAction toggleRole;
    public InputAction horn;

    bool _built;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        Build();
        LoadOverrides();        // apply any saved rebinds before enabling
        EnableDriving();
        toggleRole.Enable();
        horn.Enable();
    }

    void Build()
    {
        if (_built) return;
        _built = true;

        accelerate = new InputAction("Accelerate", InputActionType.Button);
        accelerate.AddBinding("<Keyboard>/w");
        accelerate.AddBinding("<Keyboard>/upArrow");
        accelerate.AddBinding("<Gamepad>/rightTrigger");
        accelerate.AddBinding("<Gamepad>/buttonSouth");

        brake = new InputAction("Brake", InputActionType.Button);
        brake.AddBinding("<Keyboard>/s");
        brake.AddBinding("<Keyboard>/downArrow");
        brake.AddBinding("<Gamepad>/leftTrigger");
        brake.AddBinding("<Gamepad>/buttonEast");

        steer = new InputAction("Steer", InputActionType.Value, expectedControlType: "Axis");
        steer.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");
        steer.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/rightArrow");
        steer.AddBinding("<Gamepad>/leftStick/x");

        drift = new InputAction("Drift", InputActionType.Button);
        drift.AddBinding("<Keyboard>/space");
        drift.AddBinding("<Gamepad>/rightShoulder");

        move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        move.AddBinding("<Gamepad>/leftStick");

        action = new InputAction("Action", InputActionType.Button);
        action.AddBinding("<Keyboard>/e");
        action.AddBinding("<Gamepad>/buttonSouth");

        altAction = new InputAction("AltAction", InputActionType.Button);
        altAction.AddBinding("<Keyboard>/q");
        altAction.AddBinding("<Gamepad>/buttonWest");

        toggleRole = new InputAction("ToggleRole", InputActionType.Button);
        toggleRole.AddBinding("<Keyboard>/c");
        toggleRole.AddBinding("<Gamepad>/start");

        horn = new InputAction("Horn", InputActionType.Button);   // always on — any role can honk
        horn.AddBinding("<Keyboard>/h");
        horn.AddBinding("<Gamepad>/buttonNorth");
    }

    public void EnableDriving()
    {
        accelerate.Enable(); brake.Enable(); steer.Enable(); drift.Enable();
        move.Disable(); action.Disable(); altAction.Disable();
    }

    public void EnableOnFoot()
    {
        accelerate.Disable(); brake.Disable(); steer.Disable(); drift.Disable();
        move.Enable(); action.Enable(); altAction.Enable();
    }

    // ---------- REBINDING ----------
    /// One rebindable control row: a display name, the action, the binding index to override, and whether it's
    /// the gamepad binding (so the Settings UI can split keyboard vs gamepad columns + start the right rebind).
    public struct Bindable { public string label; public InputAction action; public int bindingIndex; public bool gamepad;
        public Bindable(string l, InputAction a, int i, bool g) { label = l; action = a; bindingIndex = i; gamepad = g; } }

    /// The user-rebindable controls. Binding indices follow the order added in Build(): for a 1DAxis composite,
    /// the composite head is one index and each part (Negative/Positive) is its own index. For simple actions the
    /// keyboard binding is index 0 and the gamepad binding is the matching later index.
    public Bindable[] Rebindables()
    {
        return new[]
        {
            new Bindable("Accelerate (Key)",  accelerate, 0, false),
            new Bindable("Accelerate (Pad)",  accelerate, 2, true),   // 0=w,1=upArrow,2=rightTrigger,3=buttonSouth
            new Bindable("Brake (Key)",       brake,      0, false),
            new Bindable("Brake (Pad)",       brake,      2, true),
            // steer composite #1 indices: 0 = composite head, 1 = Negative(a)=left, 2 = Positive(d)=right
            new Bindable("Steer Left (Key)",  steer,      1, false),
            new Bindable("Steer Right (Key)", steer,      2, false),
            new Bindable("Drift (Key)",       drift,      0, false),
            new Bindable("Drift (Pad)",       drift,      1, true),
            new Bindable("Action (Key)",      action,     0, false),
            new Bindable("Action (Pad)",      action,     1, true),
            new Bindable("Alt Action (Key)",  altAction,  0, false),
            new Bindable("Alt Action (Pad)",  altAction,  1, true),
            new Bindable("Switch Role (Key)", toggleRole, 0, false),
            new Bindable("Switch Role (Pad)", toggleRole, 1, true),
            new Bindable("Horn (Key)",        horn,       0, false),
            new Bindable("Horn (Pad)",        horn,       1, true),
        };
    }

    /// The current human-readable binding for a Bindable (e.g. "W", "Right Trigger").
    public static string DisplayFor(Bindable b)
    {
        if (b.action == null || b.bindingIndex < 0 || b.bindingIndex >= b.action.bindings.Count) return "—";
        return UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(
            b.action.bindings[b.bindingIndex].effectivePath,
            UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.OmitDevice);
    }

    UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation _rebindOp;

    /// Start an interactive rebind for `b`. Disables the action during the rebind, restricts to the right device
    /// kind, calls onComplete (with success), and persists overrides. Cancel with the Escape/Start button.
    public void StartRebind(Bindable b, System.Action<bool> onComplete)
    {
        if (b.action == null) return;
        _rebindOp?.Dispose();
        bool wasEnabled = b.action.enabled;
        b.action.Disable();
        var op = b.action.PerformInteractiveRebinding(b.bindingIndex)
            .WithControlsExcluding("<Mouse>")
            .OnMatchWaitForAnother(0.1f);
        if (b.gamepad) op.WithExpectedControlType("Button").WithControlsHavingToMatchPath("<Gamepad>");
        else op.WithControlsHavingToMatchPath("<Keyboard>");
        op.OnComplete(o => { o.Dispose(); _rebindOp = null; if (wasEnabled) b.action.Enable(); SaveOverrides(); onComplete?.Invoke(true); })
          .OnCancel(o => { o.Dispose(); _rebindOp = null; if (wasEnabled) b.action.Enable(); onComplete?.Invoke(false); });
        _rebindOp = op;
        op.Start();
    }

    /// Reset every rebindable action to its code-defined defaults and clear the saved overrides.
    public void ResetBindings()
    {
        accelerate.RemoveAllBindingOverrides(); brake.RemoveAllBindingOverrides(); steer.RemoveAllBindingOverrides();
        drift.RemoveAllBindingOverrides(); move.RemoveAllBindingOverrides(); action.RemoveAllBindingOverrides();
        altAction.RemoveAllBindingOverrides(); toggleRole.RemoveAllBindingOverrides(); horn.RemoveAllBindingOverrides();
        SettingsStore.ClearBindingOverrides();
    }

    // The persisted actions, in a stable order. Each stores its override JSON under its own PlayerPrefs key
    // (one key per action — avoids any delimiter collision with the JSON the Input System emits).
    InputAction[] PersistOrder => new[] { accelerate, brake, steer, drift, action, altAction, toggleRole, horn };

    void SaveOverrides()
    {
        var order = PersistOrder;
        for (int i = 0; i < order.Length; i++)
            SettingsStore.SetBindingOverride(i, order[i].SaveBindingOverridesAsJson());
    }

    void LoadOverrides()
    {
        var order = PersistOrder;
        for (int i = 0; i < order.Length; i++)
        {
            string json = SettingsStore.GetBindingOverride(i);
            if (!string.IsNullOrEmpty(json)) order[i].LoadBindingOverridesFromJson(json);
        }
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        _rebindOp?.Dispose();
        accelerate?.Dispose(); brake?.Dispose(); steer?.Dispose(); drift?.Dispose();
        move?.Dispose(); action?.Dispose(); altAction?.Dispose(); toggleRole?.Dispose(); horn?.Dispose();
        _instance = null;
    }
}
