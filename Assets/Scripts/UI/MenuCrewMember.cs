using UnityEngine;
using BamePlastic.Net;

/// A clickable crew member in the menu lineup (the role selector). Wraps a BillboardCharacter with a click
/// collider; clicking it claims that role (no camera zoom). Shows a SELECTION OUTLINE — a slightly larger
/// accent-coloured billboard behind it — when hovered or when it's the locally-claimed role. Uses
/// OnMouseDown/Over (world collider) so it needs no UI raycaster.
[RequireComponent(typeof(BillboardCharacter))]
public class MenuCrewMember : MonoBehaviour
{
    MenuMode _menu;
    BillboardCharacter _view;
    Role _role;
    Color _base;
    bool _claimedByLocal;
    bool _hover;
    bool _interactable = true;

    SpriteRenderer _outline;     // the selection outline (a bigger sprite behind, accent-tinted)

    public Role Role => _role;

    public void Setup(MenuMode menu, Role role, Color baseColor)
    {
        _menu = menu; _role = role; _base = baseColor;
        _view = GetComponent<BillboardCharacter>();

        var col = gameObject.GetComponent<BoxCollider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.64f, 0f);
        col.size = new Vector3(0.7f, 1.3f, 0.7f);
        col.isTrigger = true;

        BuildOutline();
        Refresh();
    }

    void BuildOutline()
    {
        if (_outline != null) return;
        // a copy of the character sprite, behind + slightly bigger, in the role accent → reads as an outline
        var go = new GameObject("SelectOutline");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.02f);   // just behind
        go.transform.localScale = Vector3.one * 1.14f;
        go.hideFlags = HideFlags.DontSave;
        _outline = go.AddComponent<SpriteRenderer>();
        _outline.sprite = _view.sr != null ? _view.sr.sprite : null;
        _outline.color = _base;
        if (_view.sr != null) { _outline.sortingLayerID = _view.sr.sortingLayerID; _outline.sortingOrder = _view.sr.sortingOrder - 1; }
        _outline.enabled = false;
    }

    public void SetInteractable(bool on) { _interactable = on; Refresh(); }

    /// Mark this as the role the local player currently holds (outline stays on, brighter).
    public void SetClaimedByLocal(bool on) { _claimedByLocal = on; Refresh(); }

    void Refresh()
    {
        if (_view == null) return;
        // body tint: dim if not interactable, normal otherwise
        _view.SetColor(_interactable ? _base : _base * 0.55f);

        // outline shows when hovered or claimed; brighter when claimed
        bool show = _interactable && (_hover || _claimedByLocal);
        if (_outline != null)
        {
            _outline.enabled = show;
            _outline.color = _claimedByLocal ? Color.Lerp(_base, Color.white, 0.5f) : _base;
            // keep the outline sprite in sync (sprite is shared/lazy-created)
            if (_outline.sprite == null && _view.sr != null) _outline.sprite = _view.sr.sprite;
        }
    }

    void OnMouseEnter() { _hover = true; Refresh(); }
    void OnMouseExit()  { _hover = false; Refresh(); }
    void OnMouseDown()  { if (_interactable && _menu != null) _menu.OnCrewClicked(_role); }
}
