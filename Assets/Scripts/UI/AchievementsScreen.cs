using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// Achievements panel — the full catalog with this player's unlocked state, pulled from the backend
/// (AchievementController /api/auth/achievements/{playerId}). Clean pixel panel: scrollable list, each row a
/// name + description + Bhara reward + a locked/unlocked badge. Refreshes on open. Built in code like the other
/// screens; toggled by MenuController.
public class AchievementsScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;
    RectTransform _listContent;
    Text _status;

    const float PW = 900f, PH = 760f, RowH = 76f;

    public AchievementsScreen(Transform parent, MenuController menu)
    {
        _menu = menu;
        _root = new GameObject("AchievementsScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);

        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PW, PH));
        Transform pan = panel.transform;
        float pad = 34f, innerW = PW - pad * 2f;

        var heading = PixelUI.Label(pan, "Heading", "ACHIEVEMENTS", 40, TextAnchor.UpperLeft, PixelUI.Ink);
        var hrt = heading.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1);
        hrt.anchoredPosition = new Vector2(pad, -22); hrt.sizeDelta = new Vector2(innerW, 48);

        var sub = PixelUI.Label(pan, "Sub", "Earn these across your career — each grants Bhara", 18, TextAnchor.UpperLeft, PixelUI.InkDim);
        var srt = sub.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 1);
        srt.anchoredPosition = new Vector2(pad, -72); srt.sizeDelta = new Vector2(innerW, 26);

        BuildList(pan, pad, innerW);

        _status = PixelUI.Label(pan, "Status", "", 20, TextAnchor.MiddleCenter, PixelUI.InkDim);
        var st = _status.rectTransform; st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f); st.pivot = new Vector2(0.5f, 0.5f);
        st.anchoredPosition = new Vector2(0, 0); st.sizeDelta = new Vector2(innerW, 40);

        PixelUIWidgets.Button(pan, "Refresh", "⟳ REFRESH", new Vector2(1, 0), new Vector2(-pad, 22), new Vector2(220, 56),
                              Refresh, PixelUI.Cyan);
        PixelUIWidgets.Button(pan, "Back", "◀ BACK", new Vector2(0, 0), new Vector2(pad, 22), new Vector2(220, 56),
                              () => _menu.BackToTitle(), PixelUI.InkDim);
    }

    void BuildList(Transform pan, float pad, float w)
    {
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewport.transform.SetParent(pan, false);
        var vrt = (RectTransform)viewport.transform;
        vrt.anchorMin = new Vector2(0, 1); vrt.anchorMax = new Vector2(0, 1); vrt.pivot = new Vector2(0, 1);
        vrt.anchoredPosition = new Vector2(pad, -110); vrt.sizeDelta = new Vector2(w, PH - 110 - 96);
        var vimg = viewport.GetComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f);
        var mask = viewport.GetComponent<Mask>(); mask.showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        _listContent = (RectTransform)content.transform;
        _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1); _listContent.pivot = new Vector2(0.5f, 1);
        _listContent.anchoredPosition = Vector2.zero; _listContent.sizeDelta = Vector2.zero;

        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.content = _listContent; scroll.viewport = vrt;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
    }

    public void SetVisible(bool on) { _root.SetActive(on); if (on) Refresh(); }

    void Refresh()
    {
        ClearRows();
        if (!PlayerAccount.LoggedIn) { _status.text = "Sign in to track achievements"; return; }
        _status.text = "Loading…";
        PlayerAccount.FetchAchievements(OnJson, _ => { _status.text = "Could not load achievements"; });
    }

    [Serializable] class Row { public string code; public string name; public string description; public int bhara; public bool unlocked; }
    [Serializable] class Payload { public Row[] achievements; }

    void OnJson(string json)
    {
        ClearRows();
        Payload p = null;
        try { p = JsonUtility.FromJson<Payload>(json); } catch { }
        if (p == null || p.achievements == null || p.achievements.Length == 0) { _status.text = "No achievements yet"; return; }

        int unlockedCount = 0;
        float w = PW - 68f;
        float y = 0f;
        foreach (var r in p.achievements)
        {
            if (r.unlocked) unlockedCount++;
            AddRow(r, w, y);
            y -= RowH;
        }
        _listContent.sizeDelta = new Vector2(0, p.achievements.Length * RowH);
        _status.text = unlockedCount + " / " + p.achievements.Length + " unlocked";
    }

    void AddRow(Row r, float w, float y)
    {
        var go = new GameObject("Ach_" + r.code, typeof(RectTransform));
        go.transform.SetParent(_listContent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(0, RowH - 8f);

        var bg = PixelUI.Block(go.transform, "Bg", r.unlocked ? new Color(0.16f, 0.14f, 0.07f, 0.5f) : new Color(0.08f, 0.08f, 0.11f, 0.5f));
        var brt = bg.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        // badge: a filled star if unlocked, a hollow marker if locked (ASCII-safe for the pixel font)
        var badge = PixelUI.Label(go.transform, "Badge", r.unlocked ? "★" : "·", 34, TextAnchor.MiddleCenter,
                                  r.unlocked ? PixelUI.Gold : PixelUI.InkDim);
        var bart = badge.rectTransform; bart.anchorMin = new Vector2(0, 0); bart.anchorMax = new Vector2(0, 1); bart.pivot = new Vector2(0, 0.5f);
        bart.offsetMin = new Vector2(12, 0); bart.sizeDelta = new Vector2(56, 0); bart.anchoredPosition = new Vector2(12, 0);

        var name = PixelUI.Label(go.transform, "Name", r.name, 24, TextAnchor.UpperLeft, r.unlocked ? PixelUI.Ink : PixelUI.InkDim);
        var nrt = name.rectTransform; nrt.anchorMin = new Vector2(0, 1); nrt.anchorMax = new Vector2(1, 1); nrt.pivot = new Vector2(0, 1);
        nrt.offsetMin = new Vector2(80, -34); nrt.offsetMax = new Vector2(-120, -6);

        var desc = PixelUI.Label(go.transform, "Desc", r.description, 17, TextAnchor.UpperLeft, PixelUI.InkDim);
        var drt = desc.rectTransform; drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0, 1);
        drt.offsetMin = new Vector2(80, -64); drt.offsetMax = new Vector2(-120, -34);

        var reward = PixelUI.Label(go.transform, "Reward", "+" + r.bhara + " B", 22, TextAnchor.MiddleRight, r.unlocked ? PixelUI.Green : PixelUI.InkDim);
        var rrt = reward.rectTransform; rrt.anchorMin = new Vector2(1, 0); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(1, 0.5f);
        rrt.offsetMin = new Vector2(-115, 0); rrt.offsetMax = new Vector2(-12, 0); rrt.anchoredPosition = new Vector2(-12, 0);
    }

    readonly List<GameObject> _rows = new List<GameObject>();
    void ClearRows()
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_listContent.GetChild(i).gameObject);
        _rows.Clear();
    }
}
