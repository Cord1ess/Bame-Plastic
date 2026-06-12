using UnityEngine;
using UnityEngine.Audio;

/// Persistent game settings (PlayerPrefs) + applying them. Audio volumes route through an AudioMixer (built
/// at runtime if none is assigned); graphics applies quality/fullscreen/resolution; player name is the
/// lobby/standings label. Read/write via the static props; call ApplyAll() on boot and after changes.
public static class SettingsStore
{
    const string K_Master = "vol_master", K_Music = "vol_music", K_Sfx = "vol_sfx";
    const string K_Quality = "gfx_quality", K_Fullscreen = "gfx_fullscreen", K_ResW = "gfx_resw", K_ResH = "gfx_resh";
    const string K_Fps = "gfx_fps";
    const string K_VSync = "gfx_vsync", K_RenderScale = "gfx_renderscale";
    const string K_Name = "player_name";
    const string K_Shake = "gp_shake", K_GuideLine = "gp_guideline", K_InvertCam = "gp_invertcam", K_Mic = "gp_mic";

    /// Frame-rate cap options (index → value). -1 = uncapped (VSync off, target -1).
    public static readonly int[] FpsOptions = { 30, 60, 120, 144, -1 };
    public static string FpsLabel(int v) => v < 0 ? "UNCAPPED" : v.ToString();

    public static float MasterVol { get => PlayerPrefs.GetFloat(K_Master, 0.9f); set => PlayerPrefs.SetFloat(K_Master, value); }
    public static float MusicVol  { get => PlayerPrefs.GetFloat(K_Music, 0.7f);  set => PlayerPrefs.SetFloat(K_Music, value); }
    public static float SfxVol    { get => PlayerPrefs.GetFloat(K_Sfx, 0.85f);   set => PlayerPrefs.SetFloat(K_Sfx, value); }

    public static int QualityIndex { get => PlayerPrefs.GetInt(K_Quality, QualitySettings.GetQualityLevel()); set => PlayerPrefs.SetInt(K_Quality, value); }
    public static bool Fullscreen  { get => PlayerPrefs.GetInt(K_Fullscreen, 1) == 1; set => PlayerPrefs.SetInt(K_Fullscreen, value ? 1 : 0); }
    public static int TargetFps    { get => PlayerPrefs.GetInt(K_Fps, 60); set => PlayerPrefs.SetInt(K_Fps, value); }
    public static bool VSync       { get => PlayerPrefs.GetInt(K_VSync, 0) == 1; set => PlayerPrefs.SetInt(K_VSync, value ? 1 : 0); }
    /// URP render scale (0.6–1.0). Lower = sharper FPS win without touching window resolution (WebGL-safe).
    public static float RenderScale { get => Mathf.Clamp(PlayerPrefs.GetFloat(K_RenderScale, 1f), 0.5f, 1f); set => PlayerPrefs.SetFloat(K_RenderScale, Mathf.Clamp(value, 0.5f, 1f)); }

    // ---- gameplay options (read by the relevant systems; safe defaults) ----
    public static float CameraShake { get => Mathf.Clamp01(PlayerPrefs.GetFloat(K_Shake, 1f)); set => PlayerPrefs.SetFloat(K_Shake, Mathf.Clamp01(value)); }
    public static bool GuideLine   { get => PlayerPrefs.GetInt(K_GuideLine, 1) == 1; set => PlayerPrefs.SetInt(K_GuideLine, value ? 1 : 0); }
    /// Conductor microphone (shout-to-call / fare-bonus). Default ON.
    public static bool MicEnabled  { get => PlayerPrefs.GetInt(K_Mic, 1) == 1; set => PlayerPrefs.SetInt(K_Mic, value ? 1 : 0); }
    public static bool InvertCam   { get => PlayerPrefs.GetInt(K_InvertCam, 0) == 1; set => PlayerPrefs.SetInt(K_InvertCam, value ? 1 : 0); }

    public static string PlayerName
    {
        get { string n = PlayerPrefs.GetString(K_Name, ""); return string.IsNullOrWhiteSpace(n) ? "Player" : n; }
        set => PlayerPrefs.SetString(K_Name, value);
    }

    static AudioMixer _mixer;

    public static void ApplyAll()
    {
        ApplyAudio();
        ApplyGraphics();
        Save();
    }

    /// Apply ALL graphics settings at once (quality + framerate + resolution + fullscreen). The Settings
    /// "APPLY" button calls this, so changes commit together rather than piecemeal.
    public static void ApplyGraphics()
    {
        ApplyQuality();
        ApplyFramerate();
        ApplyRenderScale();
        ApplyResolution();
        Save();
    }

    public static void ApplyFramerate()
    {
        int fps = TargetFps;
        // VSync ON → let the display drive the cap (target ignored). VSync OFF → honour the fps cap (-1 = uncapped).
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync ? -1 : (fps < 0 ? -1 : fps);
    }

    /// Apply URP render scale via the active pipeline asset (window resolution untouched → WebGL-safe perf knob).
    public static void ApplyRenderScale()
    {
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                 as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (rp != null) rp.renderScale = RenderScale;
    }

    public static void ApplyResolution()
    {
#if !UNITY_WEBGL && !UNITY_EDITOR
        Screen.SetResolution(ResW, ResH, Fullscreen);
#endif
    }

    public static void ApplyAudio()
    {
        // Route to a mixer if one exists in Resources (Audio/MainMixer with exposed Master/Music/SFX params);
        // otherwise fall back to AudioListener master volume so sound still scales with no setup.
        if (_mixer == null) _mixer = Resources.Load<AudioMixer>("Audio/MainMixer");
        if (_mixer != null)
        {
            _mixer.SetFloat("Master", LinearToDb(MasterVol));
            _mixer.SetFloat("Music", LinearToDb(MusicVol));
            _mixer.SetFloat("SFX", LinearToDb(SfxVol));
        }
        else
        {
            AudioListener.volume = MasterVol;   // simple global fallback (music/sfx split needs the mixer)
        }
    }

    public static void ApplyQuality()
    {
        int q = Mathf.Clamp(QualityIndex, 0, QualitySettings.names.Length - 1);
        // applyExpensiveChanges (the 2nd arg) recreates render targets immediately. On WebGL that can stall or
        // lose the GL context → the build "crashes and won't reload". Apply CHEAPLY on web (let it settle over a
        // frame); only force the expensive path on native builds.
#if UNITY_WEBGL && !UNITY_EDITOR
        QualitySettings.SetQualityLevel(q, false);
#else
        QualitySettings.SetQualityLevel(q, true);
#endif
        // Only touch screen/fullscreen in a real BUILD — doing it in the Editor rescales/locks the Game view
        // (the stuck 0.31x scale) and isn't meaningful there.
#if !UNITY_WEBGL && !UNITY_EDITOR
        Screen.fullScreen = Fullscreen;
#endif
    }

    /// Store the chosen resolution (does NOT apply — the APPLY button calls ApplyResolution()/ApplyGraphics).
    public static void SetResolution(int w, int h)
    {
        PlayerPrefs.SetInt(K_ResW, w); PlayerPrefs.SetInt(K_ResH, h);
    }
    public static int ResW => PlayerPrefs.GetInt(K_ResW, Screen.currentResolution.width);
    public static int ResH => PlayerPrefs.GetInt(K_ResH, Screen.currentResolution.height);

    public static void Save() => PlayerPrefs.Save();

    static float LinearToDb(float v) => v <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(v)) * 20f;
}
