using UnityEngine;

/// Microphone capture for the conductor "shout" mechanics. Captures the default mic, computes a smoothed
/// LOUDNESS (0..1) each frame, and exposes whether the player is actively shouting (above a threshold). Both
/// conductors share one mic; the ACTIVE conductor (whoever is being controlled) consumes it. Solo/LOCAL only —
/// loudness modulates LOCAL gameplay feel (more boarders, fare bonus); it is NOT networked (each client mics
/// its own player). Play-only, auto-spawned. Degrades gracefully with NO mic / no permission (loudness stays 0,
/// everything still works — the mic is a BONUS, never required).
public class MicInput : MonoBehaviour
{
    public static MicInput Instance { get; private set; }

    [Tooltip("Raw amplitude that maps to loudness 1.0 (lower = more sensitive). Normal speech ~0.05-0.1.")]
    public float fullScaleAmplitude = 0.18f;
    [Tooltip("Loudness above this = 'shouting' (drives the bonuses).")]
    [Range(0f, 1f)] public float shoutThreshold = 0.55f;
    [Tooltip("Smoothing for the loudness meter (higher = snappier).")]
    public float responsiveness = 10f;

    public float Loudness { get; private set; }        // 0..1 smoothed
    public bool Shouting => Loudness >= shoutThreshold;
    public bool MicAvailable { get; private set; }
    public bool Enabled { get; set; } = true;          // player can toggle the mic off

    AudioClip _clip;
    string _device;
    int _lastSample;
    const int SampleWindow = 256;
    readonly float[] _buf = new float[SampleWindow];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<MicInput>() != null) return;
        new GameObject("MicInput").AddComponent<MicInput>();
    }

    void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }

    void Start()
    {
#if !UNITY_WEBGL
        // WebGL mic needs special handling; on native, grab the default device. (No-op-safe if none.)
        try
        {
            if (Microphone.devices != null && Microphone.devices.Length > 0)
            {
                _device = Microphone.devices[0];
                _clip = Microphone.Start(_device, true, 1, 44100);   // 1s looping ring buffer
                MicAvailable = _clip != null;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[Mic] unavailable: " + e.Message); MicAvailable = false; }
#else
        MicAvailable = false;   // WebGL mic capture is a separate effort; bonuses just stay off
#endif
    }

    void OnDestroy()
    {
#if !UNITY_WEBGL
        try { if (MicAvailable && !string.IsNullOrEmpty(_device)) Microphone.End(_device); } catch { }
#endif
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Enabled = SettingsStore.MicEnabled;   // player toggle in Settings ▸ Gameplay
        if (!MicAvailable || !Enabled || _clip == null || BusController.GamePaused) { Decay(); return; }

        int pos = Microphone.GetPosition(_device);
        if (pos < 0) { Decay(); return; }

        // read the newest SampleWindow samples (handle ring-buffer wrap)
        int start = pos - SampleWindow;
        if (start < 0) start += _clip.samples;
        if (!_clip.GetData(_buf, start)) { Decay(); return; }

        float sum = 0f;
        for (int i = 0; i < SampleWindow; i++) sum += _buf[i] * _buf[i];
        float rms = Mathf.Sqrt(sum / SampleWindow);
        float target = Mathf.Clamp01(rms / Mathf.Max(0.0001f, fullScaleAmplitude));

        Loudness = Mathf.Lerp(Loudness, target, Mathf.Clamp01(responsiveness * Time.deltaTime));
        _lastSample = pos;
    }

    void Decay() { Loudness = Mathf.Lerp(Loudness, 0f, Mathf.Clamp01(responsiveness * Time.deltaTime)); }
}
