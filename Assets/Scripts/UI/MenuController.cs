using UnityEngine;
using BamePlastic.Net;

/// Orchestrates the front-end: Title → Lobby → Settings, all built in code with the PixelUI theme. Created
/// at runtime by MenuMode as an OVERLAY on the living game scene — so it draws the menu OVER the parked bus +
/// traffic (NO opaque backdrop; just a subtle top/bottom darkening for text legibility). One canvas; each
/// screen is a child panel toggled on/off. Talks to the network through SessionContext.Net.
/// PLAY-ONLY (created by MenuMode only in play) — no [ExecuteAlways], so no edit-mode duplicate canvases.
public class MenuController : MonoBehaviour
{
    public enum Screen { Auth, Title, Lobby, Settings, Store, Leaderboard, Achievements }

    [Tooltip("The screen shown first.")]
    public Screen startScreen = Screen.Title;

    Canvas _canvas;
    AuthScreen _auth;
    MainMenuScreen _title;
    LobbyScreen _lobby;
    SettingsScreen _settings;
    StoreScreen _store;
    LeaderboardScreen _leaderboard;
    AchievementsScreen _achievements;
    WalletBar _wallet;
    Screen _current = Screen.Title;

    INetworkService _editNet;   // throwaway stub for edit-mode preview (no SessionContext side effects)
    INetworkService Net => Application.isPlaying ? SessionContext.Ensure().Net : (_editNet ??= new StubNetworkService());

    // Build in OnEnable (fires reliably right after MenuMode AddComponents this in play).
    void OnEnable()
    {
        if (Application.isPlaying)
        {
            SessionContext.Ensure();
            SessionContext.Instance.Net.LocalPlayerName = SettingsStore.PlayerName;
            SettingsStore.ApplyAll();
            PlayerAccount.RestoreSession();
            if (PlayerAccount.LoggedIn) PlayerAccount.Refresh();   // re-pull wallet/unlocks for a restored session
        }
        Build();
        // gate on login: a fresh player must sign in (or pick guest) before the title
        Show(Application.isPlaying && !PlayerAccount.LoggedIn ? Screen.Auth : startScreen);
    }

    void Build()
    {
        if (_canvas != null) return;                             // already built (avoid duplicate canvases)
        // clear any stale canvas left by a prior build/recompile so screens never stack
        var stale = transform.Find("MenuCanvas");
        if (stale != null) { if (Application.isPlaying) Destroy(stale.gameObject); else DestroyImmediate(stale.gameObject); }

        _canvas = PixelUI.Canvas(transform, "MenuCanvas", 50);   // high sort order: draws over the world/HUD

        // NO backdrop, NO scrim — the living game world shows through cleanly behind the menu.
        _auth = new AuthScreen(_canvas.transform, this);
        _title = new MainMenuScreen(_canvas.transform, this);
        _lobby = new LobbyScreen(_canvas.transform, this, Net);
        _settings = new SettingsScreen(_canvas.transform, this);
        _store = new StoreScreen(_canvas.transform, this);
        _leaderboard = new LeaderboardScreen(_canvas.transform, this);
        _achievements = new AchievementsScreen(_canvas.transform, this);
        // top-right wallet (Bhara + "+") — visible on the Title screen; "+" opens the store on the GET BHARA tab
        _wallet = new WalletBar(_canvas.transform, OpenStore);
    }

    public void Show(Screen s)
    {
        _current = s;
        _auth?.SetVisible(s == Screen.Auth);
        _title?.SetVisible(s == Screen.Title);
        _lobby?.SetVisible(s == Screen.Lobby);
        _settings?.SetVisible(s == Screen.Settings);
        _store?.SetVisible(s == Screen.Store);
        _leaderboard?.SetVisible(s == Screen.Leaderboard);
        _achievements?.SetVisible(s == Screen.Achievements);
        // wallet rides along the Title + Store screens only
        _wallet?.SetVisible(s == Screen.Title || s == Screen.Store);
        if (s == Screen.Lobby) _lobby?.OnShown();

        // crew are only clickable role-pickers while IN the lobby; elsewhere they're just scenery
        var mm = _menuMode;
        if (mm != null) foreach (var c in mm.Crew) if (c != null) c.SetInteractable(s == Screen.Lobby);

        // GAMEPAD/KEYBOARD: give the EventSystem a starting focus on the newly-shown screen, so a controller can
        // navigate without first clicking. Deferred a frame so SetVisible has activated the screen's widgets.
        if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(FocusFirstSelectableNextFrame());
    }

    System.Collections.IEnumerator FocusFirstSelectableNextFrame()
    {
        yield return null;
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) yield break;
        // pick the first ACTIVE, interactable Selectable under this menu canvas (screens hide via SetActive(false)).
        var all = GetComponentsInChildren<UnityEngine.UI.Selectable>(false);
        foreach (var sel in all)
        {
            if (sel != null && sel.isActiveAndEnabled && sel.interactable)
            {
                es.SetSelectedGameObject(sel.gameObject);
                yield break;
            }
        }
        es.SetSelectedGameObject(null);
    }

    /// Called by AuthScreen on a successful login/signup (or guest) — sync the player name and enter the title.
    public void OnAuthSuccess()
    {
        if (PlayerAccount.LoggedIn)
        {
            SettingsStore.PlayerName = PlayerAccount.Username;
            if (SessionContext.Instance != null) SessionContext.Instance.Net.LocalPlayerName = PlayerAccount.Username;
            PlayerAccount.ClaimDailyBonus();   // grant the once-a-day Bhara (the WalletBar shows the toast)
        }
        Show(Screen.Title);
    }

    public void OpenStore() => Show(Screen.Store);
    public void OpenLeaderboard() => Show(Screen.Leaderboard);
    public void OpenAchievements() => Show(Screen.Achievements);

    public void Logout() { PlayerAccount.Logout(); Show(Screen.Auth); }

    MenuMode _menuMode;   // set when this menu lives in the game scene (living backdrop); drives transitions

    /// Wired by MenuMode: the menu is overlaying the live game scene, so "start" transitions in-scene rather
    /// than loading the game scene.
    public void SetMenuMode(MenuMode mm) => _menuMode = mm;

    /// Hide the whole menu UI when the shift begins (the HUD takes over).
    public void HideForPlay()
    {
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    /// A crew member in the 3D lineup was clicked (role pick) — forward to the lobby to claim/swap that role.
    public void OnCrewPicked(BamePlastic.Net.Role role) => _lobby?.OnCrewPicked(role);

    public MenuMode MenuMode => _menuMode;

    // ---- actions wired from the screens ----
    public void PlayMultiplayer() => Show(Screen.Lobby);
    public void OpenSettings() => Show(Screen.Settings);
    public void BackToTitle() => Show(Screen.Title);

    /// Called by the lobby when the driver starts the shift (the room/seed already captured by SessionContext).
    public void StartFromLobby()
    {
        if (_menuMode != null) _menuMode.StartShift(true);
        else SceneFlow.GoToGame();
    }

    public void PlaySolo()
    {
        if (SessionContext.Instance != null) SessionContext.Instance.BeginSoloShift();
        if (_menuMode != null) _menuMode.StartShift(false);
        else SceneFlow.GoToGame();
    }

    public void Quit()
    {
#if UNITY_WEBGL
        // no-op on web (can't quit a browser tab); the button is hidden on WebGL anyway
#elif UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
