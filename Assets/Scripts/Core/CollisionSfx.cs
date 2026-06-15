using UnityEngine;

/// Plays a collision "thud/crunch" whenever the bus takes an impact (BusController.Impacted). No clip was
/// provided, so the sound is GENERATED once in code: a short noise burst through a fast-decaying low-pass-ish
/// envelope = a punchy bus-body thud. Pitch/volume scale with impact severity. 2D, throttled so a continuous
/// rival-press clash doesn't machine-gun. Auto-spawned in the game scene.
public class CollisionSfx : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<CollisionSfx>() != null) return;
        new GameObject("CollisionSfx").AddComponent<CollisionSfx>();
    }

    [Range(0f, 1f)] public float volume = 0.7f;
    public float minGap = 0.18f;     // don't retrigger faster than this (continuous clashes)

    AudioSource _src;
    AudioClip _thud;
    float _lastAt = -10f;

    void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 0f; _src.playOnAwake = false;
        _thud = BuildThud();
    }

    void OnEnable()  { BusController.Impacted += OnImpact; }
    void OnDisable() { BusController.Impacted -= OnImpact; }

    void OnImpact(float severity)
    {
        if (_src == null || _thud == null) return;
        if (Time.time - _lastAt < minGap) return;
        _lastAt = Time.time;
        severity = Mathf.Clamp01(severity);
        // harder hit → lower pitch (heavier) + louder
        _src.pitch = Mathf.Lerp(1.15f, 0.75f, severity);
        _src.PlayOneShot(_thud, volume * Mathf.Lerp(0.4f, 1f, severity));
    }

    // a ~0.22s body-thud: low-frequency thump + a short noisy crunch, both exp-decaying. Generated once.
    static AudioClip BuildThud()
    {
        int sr = 44100;
        int n = (int)(sr * 0.22f);
        var data = new float[n];
        var rng = new System.Random(12345);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float env = Mathf.Exp(-t * 22f);                 // fast decay
            // low thump: a ~90Hz sine sliding down
            float freq = Mathf.Lerp(120f, 55f, Mathf.Clamp01(t / 0.22f));
            phase += freq / sr;
            float thump = Mathf.Sin((float)(phase * 2.0 * Mathf.PI));
            // crunch: filtered noise, louder at the very start
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 60f) * 0.6f;
            data[i] = Mathf.Clamp((thump * 0.8f + noise) * env, -1f, 1f);
        }
        var clip = AudioClip.Create("CollisionThud", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}
