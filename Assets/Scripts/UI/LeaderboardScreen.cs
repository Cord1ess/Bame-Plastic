using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// Global CAREER leaderboard — every player ranked by lifetime taka earned, pulled live from the backend
/// (AccountController /api/auth/leaderboard/career). A clean pixel panel: header row + scrollable ranked list,
/// gold/silver/bronze accents on the top 3, the local player's row highlighted. Refreshes on open. Built in
/// code like every other screen; toggled by MenuController. Read-only.
public class LeaderboardScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;
    RectTransform _listContent;
    Text _status;

    const float PW = 900f, PH = 760f, RowH = 56f;

    public LeaderboardScreen(Transform parent, MenuController menu)
    {
        _menu = menu;
        _root = new GameObject("LeaderboardScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);

        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PW, PH));
        Transform pan = panel.transform;
        float pad = 34f, innerW = PW - pad * 2f;

        var heading = PixelUI.Label(pan, "Heading", "GLOBAL STANDINGS", 40, TextAnchor.UpperLeft, PixelUI.Ink);
        var hrt = heading.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1);
        hrt.anchoredPosition = new Vector2(pad, -22); hrt.sizeDelta = new Vector2(innerW, 48);

        var sub = PixelUI.Label(pan, "Sub", "Lifetime taka earned across every shift", 18, TextAnchor.UpperLeft, PixelUI.InkDim);
        var srt = sub.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 1);
        srt.anchoredPosition = new Vector2(pad, -72); srt.sizeDelta = new Vector2(innerW, 26);

        // column header
        HeaderRow(pan, pad, innerW);

        // scrollable list area
        BuildList(pan, pad, innerW);

        _status = PixelUI.Label(pan, "Status", "", 20, TextAnchor.MiddleCenter, PixelUI.InkDim);
        var st = _status.rectTransform; st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f); st.pivot = new Vector2(0.5f, 0.5f);
        st.anchoredPosition = new Vector2(0, 0); st.sizeDelta = new Vector2(innerW, 40);

        // refresh + back
        PixelUIWidgets.Button(pan, "Refresh", "⟳ REFRESH", new Vector2(1, 0), new Vector2(-pad, 22), new Vector2(220, 56),
                              Refresh, PixelUI.Cyan);
        PixelUIWidgets.Button(pan, "Back", "◀ BACK", new Vector2(0, 0), new Vector2(pad, 22), new Vector2(220, 56),
                              () => _menu.BackToTitle(), PixelUI.InkDim);
    }

    void HeaderRow(Transform pan, float pad, float w)
    {
        var hr = new GameObject("ColHeader", typeof(RectTransform));
        hr.transform.SetParent(pan, false);
        var rt = (RectTransform)hr.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(pad, -108); rt.sizeDelta = new Vector2(w, 30);
        Col(hr.transform, "#",       0.00f, 0.10f, w, PixelUI.InkDim, TextAnchor.MiddleLeft);
        Col(hr.transform, "PLAYER",  0.12f, 0.62f, w, PixelUI.InkDim, TextAnchor.MiddleLeft);
        Col(hr.transform, "TAKA",    0.62f, 1.00f, w, PixelUI.InkDim, TextAnchor.MiddleRight);
        var rule = PixelUI.Block(hr.transform, "Rule", PixelUI.FrameSoft);
        var rrt = rule.rectTransform; rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 0); rrt.pivot = new Vector2(0.5f, 0);
        rrt.anchoredPosition = new Vector2(0, -4); rrt.sizeDelta = new Vector2(0, 2);
    }

    void BuildList(Transform pan, float pad, float w)
    {
        // a simple clipped scroll view (rows stacked top-down; scrolls if they overflow)
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewport.transform.SetParent(pan, false);
        var vrt = (RectTransform)viewport.transform;
        vrt.anchorMin = new Vector2(0, 1); vrt.anchorMax = new Vector2(0, 1); vrt.pivot = new Vector2(0, 1);
        vrt.anchoredPosition = new Vector2(pad, -146); vrt.sizeDelta = new Vector2(w, PH - 146 - 96);
        var vimg = viewport.GetComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f); // near-invisible, needed for Mask
        var mask = viewport.GetComponent<Mask>(); mask.showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        _listContent = (RectTransform)content.transform;
        _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1); _listContent.pivot = new Vector2(0.5f, 1);
        _listContent.anchoredPosition = Vector2.zero; _listContent.sizeDelta = new Vector2(0, 0);

        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.content = _listContent; scroll.viewport = vrt;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
    }

    static Text Col(Transform parent, string text, float xMin, float xMax, float rowW, Color color, TextAnchor align)
    {
        var t = PixelUI.Label(parent, "Col", text, 22, align, color);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(rowW * xMin + 8f, 0); rt.offsetMax = new Vector2(-(rowW * (1f - xMax)) - 8f, 0);
        return t;
    }

    void Refresh()
    {
        _status.text = "Loading…";
        ClearRows();
        LeaderboardApi.FetchCareer(OnRows);
    }

    // demo placeholder standings — shown when the backend has no real entries (offline / empty DB) so the board
    // never looks broken in a demo. Dhaka bus-company flavour. The local player (if any) is merged in by rank.
    static readonly (string name, long taka)[] DemoRows =
    {
        ("Shonar Bangla Paribahan", 184500),
        ("Dhaka Express",           152300),
        ("Mirpur Super",            121800),
        ("Gulistan Gulshan",         98750),
        ("Bismillah Paribahan",      87200),
        ("Turag Transport",          73600),
        ("Bahon",                    61400),
        ("Raja City",                52900),
        ("Shikkha Paribahan",        41250),
        ("Local Komol",              28700),
    };

    void OnRows(LeaderboardApi.CareerRow[] rows)
    {
        ClearRows();
        bool empty = rows == null || rows.Length == 0;
        if (empty)
        {
            rows = BuildDemoRows();
            _status.text = "demo standings (no server data yet)";
        }
        else _status.text = "";

        string me = PlayerAccount.LoggedIn ? PlayerAccount.Username : null;

        float w = PW - 68f;
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            bool isMe = me != null && string.Equals(r.username, me, System.StringComparison.OrdinalIgnoreCase);
            BuildRow(i, r, w, isMe);
        }
        _listContent.sizeDelta = new Vector2(0, rows.Length * RowH + 8f);
    }

    // build the demo board, merging the logged-in player in at the right rank by their career taka.
    LeaderboardApi.CareerRow[] BuildDemoRows()
    {
        var list = new System.Collections.Generic.List<LeaderboardApi.CareerRow>();
        foreach (var d in DemoRows)
            list.Add(new LeaderboardApi.CareerRow { username = d.name, totalEarnings = d.taka, bhara = (int)(d.taka / 100 * 10) });

        if (PlayerAccount.LoggedIn)
        {
            list.Add(new LeaderboardApi.CareerRow {
                username = PlayerAccount.Username, totalEarnings = PlayerAccount.TotalEarnings, bhara = PlayerAccount.Bhara });
        }
        list.Sort((a, b) => b.totalEarnings.CompareTo(a.totalEarnings));
        for (int i = 0; i < list.Count; i++) { var r = list[i]; r.rank = i + 1; list[i] = r; }
        return list.ToArray();
    }

    void BuildRow(int i, LeaderboardApi.CareerRow r, float w, bool isMe)
    {
        var row = PixelUI.Panel(_listContent, "Row" + i, new Vector2(0, 1), new Vector2(0, -i * RowH), new Vector2(w, RowH - 6f), soft: true);
        var rrt = row.rectTransform; rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1);
        rrt.offsetMin = new Vector2(0, rrt.offsetMin.y); rrt.offsetMax = new Vector2(0, rrt.offsetMax.y);

        // medal accent for the top 3; cyan tint for the local player
        Color rankColor = r.rank == 1 ? PixelUI.Gold
                        : r.rank == 2 ? new Color(0.8f, 0.83f, 0.9f)
                        : r.rank == 3 ? new Color(0.8f, 0.55f, 0.3f)
                        : PixelUI.InkDim;
        if (isMe) row.color = new Color(PixelUI.Cyan.r, PixelUI.Cyan.g, PixelUI.Cyan.b, 0.22f);

        Col(row.transform, "#" + r.rank, 0.00f, 0.10f, w, rankColor, TextAnchor.MiddleLeft);
        Col(row.transform, isMe ? r.username + "  (you)" : r.username, 0.12f, 0.62f, w, isMe ? PixelUI.Cyan : PixelUI.Ink, TextAnchor.MiddleLeft);
        Col(row.transform, "Tk " + r.totalEarnings.ToString("N0"), 0.62f, 1.00f, w, PixelUI.Gold, TextAnchor.MiddleRight);
    }

    void ClearRows()
    {
        if (_listContent == null) return;
        for (int c = _listContent.childCount - 1; c >= 0; c--)
            Object.Destroy(_listContent.GetChild(c).gameObject);
    }

    public void SetVisible(bool on)
    {
        _root.SetActive(on);
        if (on) Refresh();
    }
}
