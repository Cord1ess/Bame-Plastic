using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BamePlastic.Net;

/// Turns the GAME scene's opening into a living main menu: the bus sits parked, the 3 crew (driver + 2
/// conductors) stand in a LINEUP in front of the bus at a 3/4 angle, ambient traffic flows past, the sky
/// holds at sunrise, and the pixel menu UI overlays it. In the lobby you pick your role by clicking a crew
/// member (the camera zooms to them). Pressing Start smoothly transitions the SAME scene into gameplay.
///
/// PLAY-ONLY by design: this NEVER runs or creates objects in edit mode (no [ExecuteAlways]). All menu
/// objects (camera framing, crew, UI, event system) are built in Start and torn down in OnDestroy, so nothing
/// can ever get baked/embedded into the scene. To preview the menu, press Play. Tune framing via the
/// inspector + replay.
public class MenuMode : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (ActiveScene() != SceneFlow.Game) return;
        if (FindAnyObjectByType<MenuMode>() != null) return;
        var go = new GameObject("MenuMode");
        go.AddComponent<MenuMode>();
    }
    static string ActiveScene() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

    [Header("Camera framing (bus-relative). ALIGN: position the scene Main Camera, then right-click this " +
            "component ▸ 'Capture Current Camera' to lock it in as the menu shot.")]
    [Tooltip("Camera position relative to the bus: +X right, +Y up, +Z forward. Negative Z = behind the bus.")]
    public Vector3 camOffset = new Vector3(0f, 4f, -9f);
    [Tooltip("Where the camera looks, relative to the bus.")]
    public Vector3 lookOffset = new Vector3(0f, 1.5f, 8f);
    [Tooltip("Menu FOV. Match the gameplay camera (60) so the scene doesn't look zoomed vs play.")]
    public float menuFov = 60f;

    [Header("Crew — standing along the SIDE of the bus (door side)")]
    [Tooltip("Centre of the crew, relative to the bus: out to the door side, roughly mid-length.")]
    public Vector3 lineupOffset = new Vector3(-2.6f, 0f, 0f);
    [Tooltip("Spacing along the bus's length between crew members.")]
    public float crewSpacing = 2.0f;
    public float crewHeight = 1.85f;

    [Header("Transition to gameplay")]
    public float transitionTime = 1.4f;

    BusController _bus;
    Camera _cam;
    BusCameraFollow _follow;
    MenuController _menu;
    readonly List<MenuCrewMember> _crew = new List<MenuCrewMember>();
    bool _started;
    float _baseFov;

    public IReadOnlyList<MenuCrewMember> Crew => _crew;

    bool _built;

    // ---------- lifecycle (PLAY ONLY — nothing runs/creates in edit mode) ----------
    IEnumerator Start()
    {
        // wait for the bus + camera (the road generator seats the bus on load)
        float t = 0f;
        while ((_bus == null || _cam == null) && t < 5f) { Resolve(); t += Time.deltaTime; yield return null; }
        EnterMenu();
    }

    /// Park the bus, frame the camera, and build the crew + menu UI. Safe to call once the bus/camera exist
    /// (idempotent via _built). Start() calls it after the load wait; ReturnToMenu calls it directly mid-game.
    public void EnterMenu()
    {
        if (_built) return;
        Resolve();
        _built = true;
        if (GameManager.Instance != null) GameManager.Instance.SetState(GameManager.GameState.Menu);
        if (_bus != null) { _bus.controlEnabled = false; _bus.HardStop(); }   // parked + momentum killed
        BuildWorld();
    }

    void Resolve()
    {
        if (_bus == null) _bus = BusController.Instance != null ? BusController.Instance : FindAnyObjectByType<BusController>();
        if (_follow == null) _follow = FindAnyObjectByType<BusCameraFollow>();
        if (_cam == null) _cam = _follow != null ? _follow.GetComponent<Camera>() : Camera.main;
    }

    void BuildWorld()
    {
        Resolve();
        if (_bus == null || _cam == null) return;

        _baseFov = _cam.fieldOfView;                     // remember gameplay FOV to restore on play start
        if (_follow != null) _follow.enabled = false;    // we drive the camera in menu mode

        var dn = FindAnyObjectByType<DayNightController>();
        if (dn != null) { dn.externalTimeControl = true; dn.SetShiftProgress(0f); }   // hold at sunrise

        FrameMenu();
        SpawnCrew();
        BuildMenuUI();
    }

    void FrameMenu()
    {
        Transform bt = _bus.transform;
        Vector3 pos = bt.position + bt.right * camOffset.x + Vector3.up * camOffset.y + bt.forward * camOffset.z;
        Vector3 look = bt.position + bt.right * lookOffset.x + Vector3.up * lookOffset.y + bt.forward * lookOffset.z;
        _cam.transform.position = pos;
        _cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
        _cam.fieldOfView = menuFov;
    }

    Vector3 CrewPos(int i)
    {
        Transform bt = _bus.transform;
        Vector3 centre = bt.position + bt.right * lineupOffset.x + Vector3.up * lineupOffset.y + bt.forward * lineupOffset.z;
        // spread them ALONG the bus's length (forward axis), centred, so they stand in a row beside the bus
        float off = (i - 1) * crewSpacing;               // -1,0,+1 down the bus's side
        Vector3 p = centre + bt.forward * off;
        p.y = bt.position.y;
        return p;
    }

    void SpawnCrew()
    {
        ClearCrew();
        Color[] cols = { PixelUI.Gold, PixelUI.Cyan, PixelUI.Green };
        // the menu/lobby lineup uses the real POSE sprites (arms-crossed) per role: Driver, Conductor 1, Conductor 2.
        CharacterSprites.Build();
        Sprite[] poses = { CharacterSprites.DriverPose, CharacterSprites.C1Pose, CharacterSprites.C2Pose };
        for (int i = 0; i < 3; i++)
        {
            var bc = BillboardCharacter.Create("MenuCrew_" + (Role)i, cols[i], crewHeight, CrewPos(i), null);
            if (poses[i] != null) bc.SetSprite(poses[i]);     // real art (else keep the tinted placeholder)
            var go = bc.gameObject;
            go.hideFlags = HideFlags.DontSave;           // preview objects aren't serialized into the scene
            var member = go.AddComponent<MenuCrewMember>();
            member.Setup(this, (Role)i, cols[i]);
            _crew.Add(member);
        }
    }

    void ClearCrew()
    {
        foreach (var m in _crew) if (m != null) Kill(m.gameObject);
        _crew.Clear();
    }

    void BuildMenuUI()
    {
        if (_menu != null) return;
        // destroy any orphaned MenuUI from a prior instance/recompile so we never end up with two menus
        foreach (var existing in FindObjectsByType<MenuController>()) Kill(existing.gameObject);
        EnsureEventSystem();
        var go = new GameObject("MenuUI");
        go.hideFlags = HideFlags.DontSave;               // not serialized; rebuilt each load (edit & play)
        _menu = go.AddComponent<MenuController>();
        _menu.SetMenuMode(this);
    }

    void ClearMenuUI()
    {
        if (_menu != null) { Kill(_menu.gameObject); _menu = null; }
        var es = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es != null && es.gameObject.hideFlags == HideFlags.DontSave) Kill(es.gameObject);
    }

    static void Kill(GameObject go)
    {
        if (go != null) Destroy(go);   // play-only component
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.hideFlags = HideFlags.DontSave;
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
    }

    // ---------- crew pick (lobby) ----------
    /// A crew member was clicked → just claim that role (no zoom). The selection OUTLINE on the crew member
    /// shows which one you hold (set in Refresh via SetClaimedByLocal).
    public void OnCrewClicked(Role role)
    {
        if (_menu != null) _menu.OnCrewPicked(role);
    }

    IEnumerator MoveCam(Vector3 endPos, Quaternion endRot, float endFov, float dur)
    {
        Vector3 p0 = _cam.transform.position; Quaternion r0 = _cam.transform.rotation; float f0 = _cam.fieldOfView;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, t / dur);
            _cam.transform.position = Vector3.Lerp(p0, endPos, u);
            _cam.transform.rotation = Quaternion.Slerp(r0, endRot, u);
            _cam.fieldOfView = Mathf.Lerp(f0, endFov, u);
            yield return null;
        }
    }

    /// Return to the MENU IN PLACE (no scene reload) — used by "Leave Shift". The bus parks WHERE IT IS and the
    /// menu rebuilds around it, so the streamed road/buildings/world stay exactly as they are (a scene reload was
    /// what dropped them + made the bus fall). Resets the shift to idle, then re-creates a MenuMode whose Start
    /// re-frames the camera, parks the bus, and rebuilds the crew + menu UI at the current spot.
    public static void ReturnToMenu()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        Debug.Log("[MenuMode] ReturnToMenu — rebuilding the living menu in place.");
        Time.timeScale = 1f;
        BusController.GamePaused = false;

        // tear down any existing MenuMode (shouldn't be one mid-shift, but be safe) so we don't stack menus
        foreach (var existing in FindObjectsByType<MenuMode>(FindObjectsSortMode.None)) Destroy(existing.gameObject);

        // stop the shift + hide its gameplay HUD; back to the Boot/Menu game state
        var shift = ShiftManager.Instance != null ? ShiftManager.Instance : FindAnyObjectByType<ShiftManager>();
        if (shift != null) shift.EndToMenu();
        if (GameManager.Instance != null) GameManager.Instance.SetState(GameManager.GameState.Menu);

        // re-create the living menu + build it IMMEDIATELY (the bus/camera already exist mid-game, so no need to
        // wait for load like the first-boot path). EnterMenu re-parks the bus, frames the camera, and shows the UI.
        var go = new GameObject("MenuMode");
        var mm = go.AddComponent<MenuMode>();
        mm.EnterMenu();
    }

    // ---------- start → gameplay ----------
    public void StartShift(bool multiplayer)
    {
        if (_started) return;
        _started = true;
        StartCoroutine(TransitionToPlay());
    }

    IEnumerator TransitionToPlay()
    {
        if (_menu != null) _menu.HideForPlay();
        ClearCrew();

        Vector3 startPos = _cam.transform.position;
        Quaternion startRot = _cam.transform.rotation;
        float startFov = _cam.fieldOfView;

        if (GameManager.Instance != null) GameManager.Instance.EnterPlaying();
        if (_bus != null) _bus.controlEnabled = true;
        var shift = ShiftManager.Instance != null ? ShiftManager.Instance : FindAnyObjectByType<ShiftManager>();
        if (shift != null) shift.BeginShift();

        float t = 0f;
        while (t < transitionTime && _follow != null && _bus != null)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, t / transitionTime);
            _follow.GetChasePose(out Vector3 chasePos, out Quaternion chaseRot);
            _cam.transform.position = Vector3.Lerp(startPos, chasePos, u);
            _cam.transform.rotation = Quaternion.Slerp(startRot, chaseRot, u);
            _cam.fieldOfView = Mathf.Lerp(startFov, _baseFov, u);
            yield return null;
        }
        if (_cam != null) _cam.fieldOfView = _baseFov;
        if (_follow != null) _follow.enabled = true;     // chase camera resumes
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // tear down everything we created so nothing lingers (the transition destroys us; this also covers
        // stop-play / scene unload)
        ClearCrew();
        ClearMenuUI();
    }
}
