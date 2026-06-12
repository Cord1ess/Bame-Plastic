using UnityEngine;

/// All player-bus audio, driven by real clips in Resources/Sounds:
///   • HORN — hold Horn (H / gamepad north) for a CONSTANT looping honk (real clip); release stops it. The honk
///     also shoves nearby small vehicles toward the kerb (Dhaka horn culture).
///   • ENGINE — a start clip on shift begin, then a crossfade between IDLE and DRIVE loops by speed, pitched by RPM.
///   • GEAR — a one-shot on each gear change.
///   • RADIO — a faint, SPATIAL loop placed just in front of the bus, like a radio playing up ahead.
/// Auto-spawns in the game scene. Clips load by name; any missing clip is simply silent (no crash).
public class BusAudio : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<BusAudio>() == null)
        {
            var go = new GameObject("BusAudio");
            go.AddComponent<BusAudio>();
            SceneHierarchy.Parent(go, SceneHierarchy.Category.World);
        }
    }

    [Header("Volumes")]
    [Range(0f, 1f)] public float hornVolume = 0.7f;
    [Range(0f, 1f)] public float engineVolume = 0.9f;     // prominent driving engine
    [Range(0f, 1f)] public float gearVolume = 0.3f;       // quiet — must not dull the engine
    [Range(0f, 1f)] public float radioVolume = 0.4f;      // faint but audible (was inaudible at 0.22 + full 3D)
    public float enginePitchMin = 0.85f, enginePitchMax = 1.6f;

    [Header("Horn → traffic nudge (Dhaka horn culture)")]
    public float hornNudgeRange = 16f;
    public float hornNudgeLateral = 0.9f;
    [Tooltip("Radio sits this far in FRONT of the bus (spatial), so it sounds like it's playing up ahead.")]
    public float radioAheadMetres = 6f;

    AudioSource _horn, _idle, _drive, _gear, _radio;
    bool _hornWas;
    int _lastGear = -1;
    bool _started;

    void Start()
    {
        _horn  = MakeSource("Horn",  Load("horn_player"), loop: true,  spatial: 0f, vol: 0f);
        _idle  = MakeSource("Idle",  Load("engine_idle"), loop: true,  spatial: 0f, vol: 0f);
        _drive = MakeSource("Drive", Load("engine_drive"),loop: true,  spatial: 0f, vol: 0f);
        _gear  = MakeSource("Gear",  Load("gear_shift"),  loop: false, spatial: 0f, vol: gearVolume);
        if (_idle != null && _idle.clip != null) _idle.Play();
        if (_drive != null && _drive.clip != null) _drive.Play();

        // RADIO — placed in front of the bus. The LISTENER is on the chase camera (~11-16m behind + above), so a
        // fully-3D source 6m AHEAD of the bus would be ~20m+ away and fall silent. Use a PARTIAL spatial blend
        // (positional flavour — pans/feels forward) but mostly 2D so it's always audible, with a generous range.
        var radioGo = new GameObject("BusRadio");
        radioGo.transform.SetParent(transform, false);
        _radio = radioGo.AddComponent<AudioSource>();
        _radio.clip = Load("bus_radio"); _radio.loop = true; _radio.playOnAwake = false;
        _radio.spatialBlend = 0.5f;                 // half 2D / half positional → "in front" feel, never cuts out
        _radio.volume = radioVolume; _radio.dopplerLevel = 0f;
        _radio.minDistance = 6f; _radio.maxDistance = 60f; _radio.rolloffMode = AudioRolloffMode.Linear;
        if (_radio.clip != null) _radio.Play();

        // engine START one-shot when the shift kicks off (played once)
        var startClip = Load("engine_start");
        if (startClip != null) AudioSource.PlayClipAtPoint(startClip, transform.position, engineVolume);
    }

    AudioSource MakeSource(string name, AudioClip clip, bool loop, float spatial, float vol)
    {
        if (clip == null) return null;
        var go = new GameObject("Src_" + name); go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.spatialBlend = spatial; s.volume = vol; s.playOnAwake = false;
        return s;
    }

    static AudioClip Load(string n) => Resources.Load<AudioClip>("Sounds/" + n);

    void Update()
    {
        var bus = BusController.Instance;

        // ---- HORN: hold = constant looping honk ----
        GameInput gi = GameInput.Instance;
        bool hornOn = gi != null && gi.horn != null && gi.horn.IsPressed();
        if (_horn != null)
        {
            if (hornOn && !_horn.isPlaying) _horn.Play();
            else if (!hornOn && _horn.isPlaying) _horn.Stop();
            _horn.volume = hornVolume;
        }
        if (hornOn && !_hornWas) NudgeTrafficAside();   // nudge once per press
        _hornWas = hornOn;

        if (bus == null) return;

        // keep the radio just ahead of the bus (in its forward direction)
        if (_radio != null) _radio.transform.position = bus.transform.position + bus.transform.forward * radioAheadMetres + Vector3.up * 1.5f;

        // ---- ENGINE: crossfade idle↔drive by speed, pitch by RPM ----
        float rpm = Mathf.Clamp01(bus.Rpm01);
        float spd = Mathf.Clamp01(bus.SpeedNormalized);
        float pitch = Mathf.Lerp(enginePitchMin, enginePitchMax, rpm);
        bool paused = BusController.GamePaused;
        float master = paused ? 0f : engineVolume;
        if (_idle != null)  { _idle.pitch = pitch * 0.9f; _idle.volume  = Mathf.Lerp(_idle.volume,  master * (1f - spd) * 0.9f, 5f * Time.deltaTime); }
        if (_drive != null) { _drive.pitch = pitch;       _drive.volume = Mathf.Lerp(_drive.volume, master * (0.25f + 0.75f * spd), 5f * Time.deltaTime); }

        // ---- GEAR shift one-shot ----
        int gear = bus.Gear;
        if (_started && _gear != null && gear != _lastGear && _lastGear >= 0)
            _gear.PlayOneShot(_gear.clip, gearVolume);
        _lastGear = gear; _started = true;
    }

    // Dhaka horn culture: a honk makes nearby SMALL vehicles (rickshaws/cars) scoot toward the kerb. Buses/
    // trucks ignore it. Uses TrafficVehicle.Nudge (road-relative → deterministic, MP-safe).
    void NudgeTrafficAside()
    {
        var ts = TrafficSystem.Instance;
        if (ts == null) return;
        var live = ts.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var v = live[i];
            if (v == null || !v.InUse) continue;
            if (v.kind == TrafficVehicle.Kind.Bus || v.kind == TrafficVehicle.Kind.Truck) continue;
            if (Mathf.Abs(v.metresFromBus) > hornNudgeRange || v.metresFromBus < -3f) continue;
            float toKerb = Mathf.Sign(v.lateral == 0f ? 1f : v.lateral);
            v.Nudge(0f, toKerb * hornNudgeLateral);
        }
    }
}
