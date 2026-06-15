using UnityEngine;

/// Global city ambience — a faint 2D loop (Resources/Sounds/ambient_city) under the whole shift. Auto-spawned
/// in the game scene; ducks while paused. Silent if the clip is missing.
public class WorldAmbience : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.3f;
    AudioSource _src;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<WorldAmbience>() != null) return;
        new GameObject("WorldAmbience").AddComponent<WorldAmbience>();
    }

    void Start()
    {
        var clip = Resources.Load<AudioClip>("Sounds/ambient_city");
        if (clip == null) return;
        _src = gameObject.AddComponent<AudioSource>();
        _src.clip = clip; _src.loop = true; _src.spatialBlend = 0f; _src.volume = volume; _src.playOnAwake = false;
        _src.Play();
    }

    void Update()
    {
        if (_src == null) return;
        float target = BusController.GamePaused ? volume * 0.3f : volume;
        _src.volume = Mathf.Lerp(_src.volume, target, 4f * Time.deltaTime);
    }
}
