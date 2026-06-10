using UnityEngine;

/// Slides a UI element in FROM THE LEFT and eases to its resting X — decelerating, settling exactly on
/// target with NO overshoot. The REST position is captured ONCE (the first time it's armed) and reused for
/// every replay, so re-showing the screen can never mistake the off-screen start for "rest" (that bug made
/// everything vanish when returning to the menu). Play mode only.
public class SpringIn : MonoBehaviour
{
    RectTransform _rt;
    float _restX, _offset, _delay, _delayLeft;
    bool _restCaptured;
    bool _running;

    [Tooltip("Approach speed: higher = snaps to rest faster. Only ever moves toward the target (no overshoot).")]
    public float speed = 12f;

    /// Arm the slide-in. restX is IGNORED after the first call — the true rest X is captured once from the
    /// element's authored position. `offset` = how far left it starts; `delay` before it moves.
    public void Play(float restXHint, float offset, float delay)
    {
        _rt = (RectTransform)transform;
        if (!_restCaptured) { _restX = _rt.anchoredPosition.x; _restCaptured = true; }  // capture ONCE
        _offset = Mathf.Abs(offset);
        _delay = delay; _delayLeft = delay;
        _running = true;
        var p = _rt.anchoredPosition; p.x = _restX - _offset; _rt.anchoredPosition = p;  // start off-screen left
    }

    void OnEnable()
    {
        // replay when the screen is re-shown — but only if we already know the rest X (armed at least once)
        if (_restCaptured) { _delayLeft = _delay; _running = true;
            var p = _rt.anchoredPosition; p.x = _restX - _offset; _rt.anchoredPosition = p; }
    }

    void Update()
    {
        if (!_running || _rt == null) return;
        if (_delayLeft > 0f) { _delayLeft -= Time.unscaledDeltaTime; return; }

        float x = _rt.anchoredPosition.x;
        x = Mathf.Lerp(x, _restX, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        var p = _rt.anchoredPosition; p.x = x; _rt.anchoredPosition = p;

        if (_restX - x < 0.5f)
        {
            p.x = _restX; _rt.anchoredPosition = p;
            _running = false;
        }
    }

    /// Snap to rest immediately (used as a safety so a screen is never left mid-flight / off-screen).
    public void SnapToRest()
    {
        if (!_restCaptured || _rt == null) return;
        var p = _rt.anchoredPosition; p.x = _restX; _rt.anchoredPosition = p;
        _running = false;
    }
}
