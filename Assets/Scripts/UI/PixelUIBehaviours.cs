using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Runtime behaviours backing the PixelUIWidgets builders. Hard colour-swap states only (no gradients/tweens)
// to keep the crisp retro feel. Each is added by the matching PixelUIWidgets.* factory.

/// Cut-corner button: idle/hover/press fills + an accent underline shown on hover. Also implements the
/// EventSystem SELECT/SUBMIT handlers so a GAMEPAD or KEYBOARD can navigate + activate it (the same visuals as
/// pointer hover/click), not just the mouse. A sibling Selectable (added by the factory) makes it focusable.
public class PixelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
                           IPointerUpHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    Image _bg;
    Text _label;
    Image _underline;
    Action _onClick;
    Color _idle, _hover, _press, _accent;
    bool _interactable = true;
    bool _activeTab = false;       // when used as a tab: stays highlighted while selected

    RectTransform _rt;
    Vector2 _restPos;
    bool _posCaptured;

    public void Init(Image bg, Color accent, Action onClick)
    {
        _bg = bg; _accent = accent; _onClick = onClick;
        _idle  = PixelUI.BtnFill;        // buttons use their own brighter, raised fills
        _hover = PixelUI.BtnHover;
        _press = PixelUI.BtnPress;
        _bg.color = _idle;
        _rt = bg.rectTransform;
    }

    void CapturePos() { if (!_posCaptured) { _restPos = _rt.anchoredPosition; _posCaptured = true; } }
    void Sink(bool down) { CapturePos(); _rt.anchoredPosition = down ? _restPos + new Vector2(0f, -3f) : _restPos; }
    public void SetLabel(Text t)
    {
        // passing null clears the auto-built label (caller draws a custom one, e.g. two-part room rows)
        if (t == null && _label != null) UnityEngine.Object.Destroy(_label.gameObject);
        _label = t;
    }
    public void SetLabelText(string s) { if (_label) _label.text = s; }
    public void SetUnderline(Image u) { _underline = u; if (_underline) _underline.gameObject.SetActive(false); }
    public void SetOnClick(Action a) => _onClick = a;

    public void SetInteractable(bool on)
    {
        _interactable = on;
        _bg.color = on ? _idle : PixelUI.PanelAlt;
        if (_label) _label.color = on ? PixelUI.Ink : PixelUI.InkDim;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!_interactable) return;
        _bg.color = _hover;
        if (_underline) _underline.gameObject.SetActive(true);
        if (_label) _label.color = _accent;
    }
    public void OnPointerExit(PointerEventData e)
    {
        if (!_interactable) return;
        _bg.color = _activeTab ? _hover : _idle;
        if (_underline) _underline.gameObject.SetActive(_activeTab);
        if (_label) _label.color = _activeTab ? _accent : PixelUI.Ink;
        Sink(false);
    }

    /// Used when this button is a tab: keep it visually selected (fill lifted, underline + accent label on).
    public void SetActiveTab(bool on)
    {
        _activeTab = on;
        _bg.color = on ? _hover : _idle;
        if (_underline) _underline.gameObject.SetActive(on);
        if (_label) _label.color = on ? _accent : PixelUI.Ink;
    }
    public void OnPointerDown(PointerEventData e) { if (_interactable) { _bg.color = _press; Sink(true); } }
    public void OnPointerUp(PointerEventData e)   { if (_interactable) { _bg.color = _hover; Sink(false); } }
    public void OnPointerClick(PointerEventData e) { if (_interactable) _onClick?.Invoke(); }

    // ---- gamepad / keyboard navigation: mirror the hover/click visuals on EventSystem focus ----
    public void OnSelect(BaseEventData e)
    {
        if (!_interactable) return;
        _bg.color = _hover;
        if (_underline) _underline.gameObject.SetActive(true);
        if (_label) _label.color = _accent;
    }
    public void OnDeselect(BaseEventData e)
    {
        if (!_interactable) return;
        _bg.color = _activeTab ? _hover : _idle;
        if (_underline) _underline.gameObject.SetActive(_activeTab);
        if (_label) _label.color = _activeTab ? _accent : PixelUI.Ink;
    }
    public void OnSubmit(BaseEventData e) { if (_interactable) _onClick?.Invoke(); }
}

/// Checkbox: shows/hides the check block; flat colour states.
public class PixelToggle : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    Image _box, _check;
    bool _value;
    Action<bool> _onChange;

    public void Init(Image box, Image check, bool value, Action<bool> onChange)
    {
        _box = box; _check = check; _onChange = onChange;
        SetValue(value, false);
    }
    public bool Value => _value;
    public void SetValue(bool v, bool notify = true)
    {
        _value = v;
        if (_check) _check.gameObject.SetActive(v);
        if (notify) _onChange?.Invoke(v);
    }
    public void OnPointerClick(PointerEventData e) => SetValue(!_value);
    public void OnPointerEnter(PointerEventData e) { if (_box) _box.color = PixelUI.PanelHover; }
    public void OnPointerExit(PointerEventData e)  { if (_box) _box.color = PixelUI.PanelAlt; }
}

/// Horizontal 0..1 slider with a chunky knob; drag or click anywhere on the track.
public class PixelSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    RectTransform _track, _fill, _knob;
    float _inner, _pad, _value;
    Action<float> _onChange;

    public void Init(RectTransform track, Image fill, RectTransform knob, float inner, float pad, float value, Action<float> onChange)
    {
        _track = track; _fill = fill.rectTransform; _knob = knob; _inner = inner; _pad = pad; _onChange = onChange;
        SetValue(value, false);
    }
    public float Value => _value;
    public void SetValue(float v, bool notify = true)
    {
        _value = Mathf.Clamp01(v);
        _fill.sizeDelta = new Vector2(_inner * _value, _fill.sizeDelta.y);
        _knob.anchoredPosition = new Vector2(_pad + _inner * _value, 0f);
        if (notify) _onChange?.Invoke(_value);
    }
    void Apply(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, e.position, e.pressEventCamera, out Vector2 lp))
        {
            float x = lp.x - _pad;                         // local x within the fill region
            SetValue(_inner > 0 ? x / _inner : 0f);
        }
    }
    public void OnPointerDown(PointerEventData e) => Apply(e);
    public void OnDrag(PointerEventData e) => Apply(e);
}

/// ◀ value ▶ stepper (a pixel-friendly dropdown).
public class PixelStepper : MonoBehaviour
{
    string[] _options;
    int _index;
    Text _value;
    Action<int> _onChange;

    public void Init(string[] options, int index, Text value, PixelButton prev, PixelButton next, Action<int> onChange)
    {
        _options = options ?? new string[0]; _index = index; _value = value; _onChange = onChange;
        prev.SetOnClick(() => Step(-1));
        next.SetOnClick(() => Step(+1));
        Refresh(false);
    }
    public int Index => _index;
    void Step(int d)
    {
        if (_options.Length == 0) return;
        _index = (_index + d + _options.Length) % _options.Length;
        Refresh(true);
    }
    public void SetIndex(int i, bool notify = false) { _index = Mathf.Clamp(i, 0, Mathf.Max(0, _options.Length - 1)); Refresh(notify); }
    void Refresh(bool notify)
    {
        if (_value && _options.Length > 0) _value.text = _options[_index];
        if (notify) _onChange?.Invoke(_index);
    }
}

/// Tab bar: marks the active tab (accent underline always on + brighter fill) and notifies on select.
public class PixelTabs : MonoBehaviour
{
    PixelButton[] _tabs;
    Action<int> _onSelect;
    int _active = -1;

    public void Init(PixelButton[] tabs, Action<int> onSelect)
    {
        _tabs = tabs; _onSelect = onSelect;
        Select(0);
    }
    public void Select(int i)
    {
        if (i == _active) return;
        _active = i;
        for (int t = 0; t < _tabs.Length; t++) _tabs[t].SetActiveTab(t == i);
        _onSelect?.Invoke(i);
    }
}
