using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using BamePlastic.Net;

/// ESC pause — server-synced in multiplayer. SOLO: Esc freezes the game (Time.timeScale = 0) + shows the
/// overlay. MULTIPLAYER: pause is DRIVER-AUTHORITATIVE — any player's Esc asks the driver to pause; the driver
/// toggles the shared PauseState (GameNet) and everyone freezes together via BusController.GamePaused (NOT
/// timeScale, so the network keeps flowing and the bus simply coasts to a halt). Resuming works the same way.
/// Auto-spawned in the game scene; play-only. Overlay built with the PixelUI theme.
public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<PauseManager>() != null) return;
        new GameObject("PauseManager").AddComponent<PauseManager>();
    }

    bool _paused;
    GameObject _overlay;
    Text _byLabel;
    bool _subscribed;

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var gn = GameNet.Instance;
        if (gn != null) gn.PauseChanged -= OnNetPause;
        SetPausedLocal(false);   // never leave the game frozen if this is torn down
    }

    void Update()
    {
        // late-subscribe to GameNet (it may spawn a frame after us)
        if (!_subscribed && GameNet.Instance != null && GameNet.Instance.Active)
        {
            GameNet.Instance.PauseChanged += OnNetPause;
            _subscribed = true;
        }

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    public void TogglePause() => RequestPause(!_paused);

    public void RequestPause(bool paused)
    {
        var gn = GameNet.Instance;
        if (gn != null && gn.Active)
        {
            // MULTIPLAYER: route through driver authority. Driver sets+broadcasts; conductor asks the driver.
            if (gn.IsDriver) gn.DriverSetPause(paused, 0);
            else gn.RequestPause(paused);
            // don't set locally yet — wait for the authoritative PauseState echo (OnNetPause) so all agree.
        }
        else
        {
            // SOLO: immediate.
            SetPausedLocal(paused);
            ShowOverlay(paused, "PAUSED");
        }
    }

    void OnNetPause(bool paused, int whoSlot)
    {
        SetPausedLocal(paused);
        string who = whoSlot == 0 ? "DRIVER" : (whoSlot == 1 ? "CONDUCTOR 1" : "CONDUCTOR 2");
        ShowOverlay(paused, paused ? "PAUSED BY " + who : "PAUSED");
    }

    void SetPausedLocal(bool paused)
    {
        _paused = paused;
        BusController.GamePaused = paused;
        var gn = GameNet.Instance;
        bool mp = gn != null && gn.Active;
        // SOLO can hard-freeze with timeScale; MP must keep ticking (network + proxy interpolation) so it only
        // uses the GamePaused input-suppression above.
        Time.timeScale = (paused && !mp) ? 0f : 1f;
    }

    // ---------- overlay ----------
    void ShowOverlay(bool show, string title)
    {
        if (show) EnsureOverlay();
        if (_overlay != null) _overlay.SetActive(show);
        if (show && _byLabel != null) _byLabel.text = title;
    }

    void EnsureOverlay()
    {
        if (_overlay != null) return;
        var canvas = PixelUI.Canvas(transform, "PauseCanvas", 80);
        _overlay = canvas.gameObject;

        // dim scrim (full-screen, eats clicks)
        var scrim = PixelUI.Block(canvas.transform, "Scrim", new Color(0.04f, 0.05f, 0.08f, 0.72f));
        var srt = scrim.rectTransform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero; scrim.raycastTarget = true;

        var panel = PixelUI.Panel(canvas.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 420));
        Transform pan = panel.transform;

        _byLabel = PixelUI.Label(pan, "Title", "PAUSED", 44, TextAnchor.UpperCenter, PixelUI.Ink);
        var trt = _byLabel.rectTransform; trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1); trt.pivot = new Vector2(0.5f, 1);
        trt.anchoredPosition = new Vector2(0, -34); trt.sizeDelta = new Vector2(420, 56);

        float bw = 360, bh = 64, gap = 80, y0 = -150;
        PixelUIWidgets.Button(pan, "Resume", "RESUME", new Vector2(0.5f, 1), new Vector2(0, y0), new Vector2(bw, bh),
                              () => RequestPause(false), PixelUI.Green);
        PixelUIWidgets.Button(pan, "Settings", "SETTINGS", new Vector2(0.5f, 1), new Vector2(0, y0 - gap), new Vector2(bw, bh),
                              OpenSettings, PixelUI.InkDim);
        PixelUIWidgets.Button(pan, "Leave", "LEAVE SHIFT", new Vector2(0.5f, 1), new Vector2(0, y0 - gap * 2), new Vector2(bw, bh),
                              LeaveShift, PixelUI.Red);

        _menuPanel = panel.gameObject;   // the pause button column (hidden while Settings is open)
        _overlay.SetActive(false);
    }

    GameObject _menuPanel;
    SettingsScreen _settings;

    void OpenSettings()
    {
        // build the full Settings panel INTO the pause canvas (no MenuController in-game; Back closes it).
        if (_settings == null)
            _settings = new SettingsScreen(_overlay.transform, null, onBack: CloseSettings);
        if (_menuPanel != null) _menuPanel.SetActive(false);
        _settings.SetVisible(true);
    }

    void CloseSettings()
    {
        if (_settings != null) _settings.SetVisible(false);
        if (_menuPanel != null) _menuPanel.SetActive(true);
    }

    void LeaveShift()
    {
        SetPausedLocal(false);
        Time.timeScale = 1f;
        var ctx = SessionContext.Instance;
        bool multiplayer = ctx != null && ctx.IsMultiplayer;
        if (ctx != null && ctx.Net != null) ctx.Net.LeaveRoom();

        // close THIS pause overlay first (it lives on a canvas we're about to leave behind)
        if (_overlay != null) _overlay.SetActive(false);

        if (multiplayer)
        {
            // MP: a clean scene reload is safest (drops the netcode/proxy state) back to the lobby front-end.
            SceneFlow.GoToGame();
        }
        else
        {
            // SOLO: return to the living menu IN PLACE — the bus parks where it is and the menu rebuilds around
            // it, so the streamed road/buildings stay (a scene reload was dropping them + making the bus fall).
            MenuMode.ReturnToMenu();
        }
    }
}
