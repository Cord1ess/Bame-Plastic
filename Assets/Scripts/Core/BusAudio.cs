using UnityEngine;

/// Bus horn. Hold Horn (H / gamepad north) to honk. Auto-spawns, 2D audio.
///
/// DESKTOP/STANDALONE: procedural two-tone synth via OnAudioFilterRead (no clip needed).
/// WEBGL: OnAudioFilterRead is NOT supported (no Unity audio thread — WebGL routes through Web Audio), so
///   the synth is skipped. To get a horn on web, assign `webglHornClip` (a short recorded honk) and it'll
///   play via a normal AudioSource on web. Left null = silent horn on WebGL (harmless).
public class BusAudio : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // gameplay only — don't spawn the bus audio in the menu scene
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<BusAudio>() == null)
        {
            var go = new GameObject("BusAudio");
            go.AddComponent<BusAudio>();
            SceneHierarchy.Parent(go, SceneHierarchy.Category.World);
        }
    }

    [Range(0f, 1f)] public float hornVolume = 0.32f;
    [Tooltip("Optional recorded horn used ONLY on WebGL (where the procedural synth can't run). Leave null = silent horn on web.")]
    public AudioClip webglHornClip;

    int _sampleRate;
    double _hornA, _hornB;
    float _env;                 // smooth on/off so it doesn't click
    volatile bool _hornOn;
    AudioSource _src;

    void Start()
    {
        _sampleRate = AudioSettings.outputSampleRate;
        if (_sampleRate <= 0) _sampleRate = 44100;

        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 0f;
        _src.playOnAwake = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: no audio thread → use a clip-based horn (if provided). Don't start the silent carrier
        // (it only exists to drive OnAudioFilterRead, which WebGL won't call).
        _src.clip = webglHornClip;
        _src.loop = true;
#else
        // Desktop: silent carrier clip so OnAudioFilterRead is invoked; the synth fills it each block.
        _src.clip = AudioClip.Create("busHornLoop", _sampleRate, 1, _sampleRate, false);
        _src.loop = true;
        _src.Play();
#endif
    }

    void Update()
    {
        GameInput gi = GameInput.Instance;
        _hornOn = gi != null && gi.horn != null && gi.horn.IsPressed();

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: drive the clip directly (play while held, stop when released).
        if (_src != null && _src.clip != null)
        {
            if (_hornOn && !_src.isPlaying) _src.Play();
            else if (!_hornOn && _src.isPlaying) _src.Stop();
            _src.volume = hornVolume;
        }
#endif
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    void OnAudioFilterRead(float[] data, int channels)
    {
        float sr = _sampleRate <= 0 ? 44100f : _sampleRate;
        float target = _hornOn ? 1f : 0f;

        for (int i = 0; i < data.Length; i += channels)
        {
            _env = Mathf.Lerp(_env, target, 0.002f);   // click-free attack/release

            float sample = 0f;
            if (_env > 0.0001f)
            {
                _hornA += 370.0 / sr; if (_hornA >= 1.0) _hornA -= 1.0;
                _hornB += 466.0 / sr; if (_hornB >= 1.0) _hornB -= 1.0;
                float horn = (Mathf.Sin((float)(_hornA * 2.0 * Mathf.PI)) + Mathf.Sin((float)(_hornB * 2.0 * Mathf.PI))) * 0.5f;
                sample = horn * hornVolume * _env;
            }

            sample = Mathf.Clamp(sample, -1f, 1f);
            for (int c = 0; c < channels; c++) data[i + c] = sample;
        }
    }
#endif
}
