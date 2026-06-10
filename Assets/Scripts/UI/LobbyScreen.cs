using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// The lobby: three role cards in a row (Driver / Conductor 1 / Conductor 2), a room-code header, a room
/// browser + join-by-code entry, ready toggle, and START (driver/host only, once everyone's ready). Talks to
/// INetworkService; rebuilds the cards whenever the room updates. Built into a panel toggled by MenuController.
public class LobbyScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;
    readonly INetworkService _net;

    // two sub-views: the browser (pick/create a room) and the in-room view (cards + start)
    GameObject _browserView, _roomView;

    // room view widgets (minimal overlay — the 3D crew lineup is the picker)
    Text _codeLabel, _statusLabel, _roleLabel, _crewStatus;
    PixelButton _readyBtn, _startBtn;

    // browser widgets
    InputField _codeInput;
    Transform _listContent;
    float _listInnerW;

    public LobbyScreen(Transform parent, MenuController menu, INetworkService net)
    {
        _menu = menu; _net = net;

        _root = new GameObject("LobbyScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);         // hide FIRST so a build error can never leave a stray panel on screen

        BuildBrowser(_root.transform);
        BuildRoom(_root.transform);
        ShowBrowser(true);              // start on the browser; never have both sub-views active at once

        // events
        _net.RoomJoined += OnRoomJoined;
        _net.RoomUpdated += Refresh;
        _net.RoomLeft += OnRoomLeft;
        _net.RoomListUpdated += OnRoomList;
        _net.JoinFailed += OnJoinFailed;
        _net.ShiftStarting += OnShiftStarting;
    }

    public void OnShown()
    {
        // entering the lobby with no room → show the browser and refresh the list
        if (_net.CurrentRoom == null) { ShowBrowser(true); _net.RefreshRoomList(); }
        else { ShowBrowser(false); Refresh(_net.CurrentRoom); }
    }

    // ===================== BROWSER VIEW =====================
    void BuildBrowser(Transform parent)
    {
        _browserView = Panel(parent, "Browser");
        Transform p = _browserView.transform;

        // LEFT-ALIGNED, vertically-centred panel (matches the main menu; bus stays visible on the right).
        const float MX = 90f, PW = 560f, PH = 760f;
        var panel = PixelUI.Panel(p, "BrowserPanel", new Vector2(0, 0.5f), new Vector2(MX, 0f), new Vector2(PW, PH));
        Transform b = panel.transform;
        float pad = 32f, innerW = PW - pad * 2f;

        // heading
        var head = PixelUI.Label(b, "Heading", "PLAY ONLINE", 44, TextAnchor.UpperLeft, PixelUI.Ink);
        TL(head.rectTransform, pad, -26, innerW, 52);

        // ── HOST ──
        PixelUIWidgets.Button(b, "Host", "HOST NEW ROOM", new Vector2(0, 1f), new Vector2(pad, -96), new Vector2(innerW, 70),
                              () => _net.CreateRoom(), PixelUI.Green);

        // divider label
        var or = PixelUI.Label(b, "Or", "— OR JOIN BY CODE —", 20, TextAnchor.UpperLeft, PixelUI.InkDim);
        TL(or.rectTransform, pad, -182, innerW, 26);

        // code input + join (side by side, fits in innerW)
        float joinW = 150f, codeW = innerW - joinW - 12f;
        _codeInput = PixelUIWidgets.Input(b, "Code", "", "BAME-XXXX", new Vector2(0, 1f), new Vector2(pad, -216), new Vector2(codeW, 60), null, 9);
        PixelUIWidgets.Button(b, "Join", "JOIN", new Vector2(0, 1f), new Vector2(pad + codeW + 12f, -216), new Vector2(joinW, 60),
                              () => _net.JoinRoom(_codeInput.text));

        // ── OPEN ROOMS list ──
        var roomsHead = PixelUI.Label(b, "RoomsHead", "OPEN ROOMS", 24, TextAnchor.UpperLeft, PixelUI.Gold);
        TL(roomsHead.rectTransform, pad, -300, innerW - 150f, 30);
        PixelUIWidgets.Button(b, "Refresh", "REFRESH", new Vector2(0, 1f), new Vector2(pad + innerW - 140f, -296), new Vector2(140, 40),
                              () => _net.RefreshRoomList(), PixelUI.InkDim);

        // list container (the rows fill downward)
        var listGo = new GameObject("List", typeof(RectTransform));
        listGo.transform.SetParent(b, false);
        var lrt = (RectTransform)listGo.transform;
        lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 1);
        lrt.anchoredPosition = new Vector2(pad, -344); lrt.sizeDelta = new Vector2(innerW, 320);
        _listContent = listGo.transform;
        _listInnerW = innerW;

        // back (bottom of the panel)
        PixelUIWidgets.Button(b, "Back", "◀ BACK", new Vector2(0, 0f), new Vector2(pad, 24), new Vector2(220, 56),
                              () => _menu.BackToTitle(), PixelUI.InkDim);
    }

    // anchor a graphic to the TOP-LEFT of its parent at (x, y), left-pivoted
    static void TL(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }

    void OnRoomList(RoomListing[] rooms)
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--) Object.Destroy(_listContent.GetChild(i).gameObject);
        if (rooms == null || rooms.Length == 0)
        {
            var empty = PixelUI.Label(_listContent, "Empty", "no open rooms — host one!", 22, TextAnchor.UpperLeft, PixelUI.InkDim);
            TL(empty.rectTransform, 0, -10, _listInnerW, 30);
            return;
        }
        int max = Mathf.Min(rooms.Length, 4);            // the panel fits ~4 rows; cap so it never overflows
        for (int i = 0; i < max; i++)
        {
            var r = rooms[i];
            var row = PixelUIWidgets.Button(_listContent, "Room" + i, "", new Vector2(0, 1f), new Vector2(0, -(i * 72f)),
                                            new Vector2(_listInnerW, 62), () => _net.JoinRoom(r.code));
            row.SetLabel(null);   // custom two-part label: code (left) + occupancy (right)
            var code = PixelUI.Label(row.transform, "Code", r.code, 24, TextAnchor.MiddleLeft, PixelUI.Ink);
            var crt = code.rectTransform; crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(18, 0); crt.offsetMax = new Vector2(-18, 0);
            var occ = PixelUI.Label(row.transform, "Occ", r.humans + "/3  " + r.hostName, 18, TextAnchor.MiddleRight, PixelUI.InkDim);
            var ort = occ.rectTransform; ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
            ort.offsetMin = new Vector2(18, 0); ort.offsetMax = new Vector2(-18, 0);
        }
    }

    // ===================== ROOM VIEW =====================
    void BuildRoom(Transform parent)
    {
        _roomView = Panel(parent, "Room");
        Transform p = _roomView.transform;

        // room code + hint, TOP-LEFT (matches the left-aligned menu; bus + crew visible on the right)
        _codeLabel = PixelUI.Label(p, "Code", "ROOM ----", 40, TextAnchor.UpperLeft, PixelUI.Gold, outline: true);
        var crt = _codeLabel.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(0, 1f); crt.pivot = new Vector2(0, 1f);
        crt.anchoredPosition = new Vector2(90, -110); crt.sizeDelta = new Vector2(700, 50);

        var hint = PixelUI.Label(p, "Hint", "CLICK A CREW MEMBER TO TAKE THEIR ROLE", 22, TextAnchor.UpperLeft, PixelUI.InkDim, outline: true);
        var hrt = hint.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1f); hrt.pivot = new Vector2(0, 1f);
        hrt.anchoredPosition = new Vector2(90, -160); hrt.sizeDelta = new Vector2(700, 28);

        // ---- minimal bottom overlay strip (the 3D crew lineup IS the picker) ----
        var bar = PixelUI.Panel(p, "Bar", new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(980, 120));

        // your current role (left)
        _roleLabel = PixelUI.Label(bar.transform, "YourRole", "PICK A ROLE", 30, TextAnchor.MiddleLeft, PixelUI.Gold);
        var yr = _roleLabel.rectTransform;
        yr.anchorMin = new Vector2(0, 0.5f); yr.anchorMax = new Vector2(0, 0.5f); yr.pivot = new Vector2(0, 0.5f);
        yr.anchoredPosition = new Vector2(28, 18); yr.sizeDelta = new Vector2(420, 44);
        _crewStatus = PixelUI.Label(bar.transform, "CrewStatus", "", 20, TextAnchor.MiddleLeft, PixelUI.InkDim);
        var cs = _crewStatus.rectTransform;
        cs.anchorMin = new Vector2(0, 0.5f); cs.anchorMax = new Vector2(0, 0.5f); cs.pivot = new Vector2(0, 0.5f);
        cs.anchoredPosition = new Vector2(28, -22); cs.sizeDelta = new Vector2(500, 26);

        // ready + start (right)
        _readyBtn = PixelUIWidgets.Button(bar.transform, "Ready", "READY", new Vector2(1, 0.5f), new Vector2(-360, 0), new Vector2(200, 76),
                                          ToggleReady, PixelUI.Green);
        _startBtn = PixelUIWidgets.Button(bar.transform, "Start", "START", new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(280, 76),
                                          () => _net.StartShift(), PixelUI.Gold);

        // status line just above the bar (errors / "waiting for the driver…")
        _statusLabel = PixelUI.Label(p, "Status", "", 24, TextAnchor.MiddleCenter, PixelUI.InkDim, outline: true);
        var slt = _statusLabel.rectTransform;
        slt.anchorMin = slt.anchorMax = new Vector2(0.5f, 0f); slt.pivot = new Vector2(0.5f, 0f);
        slt.anchoredPosition = new Vector2(0, 172); slt.sizeDelta = new Vector2(900, 30);

        // leave (top-left, above the room code)
        PixelUIWidgets.Button(p, "Leave", "◀ LEAVE", new Vector2(0, 1f), new Vector2(90, -40), new Vector2(180, 50),
                              () => _net.LeaveRoom(), PixelUI.InkDim);
    }

    public void ClaimRole(Role r) => _net.ClaimRole(r);
    public void OnCrewPicked(Role r) => _net.ClaimRole(r);   // a 3D crew member was clicked → claim/swap

    void ToggleReady()
    {
        var local = _net.CurrentRoom?.slots.Find(s => s.isLocal);
        _net.SetReady(local == null || !local.ready);
    }

    void Refresh(RoomInfo room)
    {
        if (room == null) return;
        ShowBrowser(false);
        _codeLabel.text = "ROOM " + room.code;

        var local = room.slots.Find(s => s.isLocal);
        bool localReady = local != null && local.ready;
        bool isDriver = room.Driver != null && room.Driver.isLocal;

        // your role label
        _roleLabel.text = local != null ? "YOU: " + RoleName(local.role) : "PICK A ROLE";

        // crew occupancy summary (who's in each seat)
        _crewStatus.text = SeatLine(room, Role.Driver) + "   " + SeatLine(room, Role.Conductor1) + "   " + SeatLine(room, Role.Conductor2);

        // highlight the 3D crew: mark the local player's claimed one
        var mm = _menu != null ? _menu.MenuMode : null;
        if (mm != null)
            foreach (var c in mm.Crew)
                if (c != null) c.SetClaimedByLocal(local != null && c.Role == local.role);

        _readyBtn.SetLabelText(localReady ? "UNREADY" : "READY");
        _readyBtn.SetInteractable(local != null);                 // can't ready until you've picked a role

        _startBtn.gameObject.SetActive(isDriver);
        _startBtn.SetInteractable(isDriver && room.AllReady);

        if (local == null) { _statusText("pick a crew member to claim a role", PixelUI.InkDim); }
        else if (!isDriver) _statusText(room.AllReady ? "waiting for the driver to start…" : "ready up…", room.AllReady ? PixelUI.Green : PixelUI.InkDim);
        else if (!room.AllReady) _statusText("waiting for everyone to ready up…", PixelUI.InkDim);
        else _statusText("", PixelUI.InkDim);
    }

    void _statusText(string t, Color c) { if (_statusLabel != null) { _statusLabel.text = t; _statusLabel.color = c; } }
    static string RoleName(Role r) => r == Role.Driver ? "DRIVER" : r == Role.Conductor1 ? "CONDUCTOR 1" : "CONDUCTOR 2";
    static string SeatLine(RoomInfo room, Role r)
    {
        var s = room.Slot(r);
        string who = s.isLocal ? "you" : s.Display;
        return RoleName(r).Substring(0, 1) + ":" + who;   // D:you  C:Ravi …
    }

    // ===================== events =====================
    void OnRoomJoined(RoomInfo room) { ShowBrowser(false); Refresh(room); }
    void OnRoomLeft() { ShowBrowser(true); _net.RefreshRoomList(); }
    void OnJoinFailed(string reason) { _statusLabel.text = reason; _statusLabel.color = PixelUI.Red; }

    void OnShiftStarting(int seed)
    {
        if (SessionContext.Instance != null) SessionContext.Instance.BeginMultiplayerShift(_net.CurrentRoom, seed);
        _menu.StartFromLobby();   // in-scene transition (or scene load if not in menu-mode)
    }

    // ===================== helpers =====================
    void ShowBrowser(bool browser)
    {
        _browserView.SetActive(browser);
        _roomView.SetActive(!browser);
    }

    GameObject Panel(Transform parent, string name)
    {
        var go = new GameObject(name + "View", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }


    public void SetVisible(bool on) => _root.SetActive(on);
}
