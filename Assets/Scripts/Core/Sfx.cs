using System.Collections.Generic;
using UnityEngine;

/// One-shot sound effects from Resources/Sounds. `Play` is TRUE 2D (centred, no L/R pan) via a single shared
/// AudioSource + PlayOneShot — used for UI/feedback that should sound the same in both ears. `PlayAt` is a
/// world-positioned (spatial) one-shot for in-world events. Both cache clips and throttle per-name so a burst
/// (e.g. many passengers boarding at once) plays ONCE, not stacked.
public static class Sfx
{
    static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();
    static AudioSource _2d;     // shared centred 2D source

    static AudioClip Get(string name)
    {
        if (_cache.TryGetValue(name, out var c)) return c;
        c = Resources.Load<AudioClip>("Sounds/" + name);
        _cache[name] = c;
        return c;
    }

    static AudioSource Source2D()
    {
        if (_2d != null) return _2d;
        var go = new GameObject("Sfx2D");
        Object.DontDestroyOnLoad(go);
        _2d = go.AddComponent<AudioSource>();
        _2d.spatialBlend = 0f;          // pure 2D → centred, no left/right pan
        _2d.playOnAwake = false;
        return _2d;
    }

    static bool Throttled(string name, float minGap)
    {
        if (_lastPlay.TryGetValue(name, out float t) && Time.time - t < minGap) return true;
        _lastPlay[name] = Time.time;
        return false;
    }

    /// TRUE 2D one-shot (centred, no pan), throttled per-name.
    public static void Play(string name, float volume = 1f, float minGap = 0.08f)
    {
        var clip = Get(name);
        if (clip == null || !Application.isPlaying) return;
        if (Throttled(name, minGap)) return;
        Source2D().PlayOneShot(clip, volume);
    }

    /// world-positioned (spatial) one-shot, throttled per-name.
    public static void PlayAt(string name, Vector3 pos, float volume = 1f, float minGap = 0.08f)
    {
        var clip = Get(name);
        if (clip == null || !Application.isPlaying) return;
        if (Throttled(name, minGap)) return;
        AudioSource.PlayClipAtPoint(clip, pos, volume);
    }
}
