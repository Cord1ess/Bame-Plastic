using System;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;

namespace BamePlastic.Net
{
    /// The REAL backend seam: talks to the Spring Boot relay over one raw WebSocket (/ws/session). The lobby
    /// (create/join/list/role/ready/start) is JSON control messages; the in-game hot path will relay binary on
    /// the SAME socket later. Implements INetworkService identically to StubNetworkService, so it's a drop-in
    /// swap — the menu code doesn't change.
    ///
    /// Connection is lazy: the first CreateRoom/JoinRoom/RefreshRoomList opens the socket, queues the intent,
    /// and sends it once connected. Tick(dt) pumps the message queue (required by NativeWebSocket, esp. WebGL).
    public class WebSocketNetworkService : INetworkService, IGameNet
    {
        readonly string _url;                  // ws://host:8080/ws/session
        WebSocket _ws;
        bool _connecting;
        readonly Queue<string> _outbox = new Queue<string>();   // messages queued before the socket is open

        public string LocalPlayerName { get; set; } = "Player";
        public RoomInfo CurrentRoom { get; private set; }

        public event Action<RoomInfo> RoomJoined;
        public event Action<RoomInfo> RoomUpdated;
        public event Action RoomLeft;
        public event Action<string> JoinFailed;
        public event Action<RoomListing[]> RoomListUpdated;
        public event Action<int> ShiftStarting;
        public event Action<RoleReassign> RoleReassigned;

        public WebSocketNetworkService(string url) { _url = url; }

        // ---------------- connection ----------------
        async void Connect()
        {
            if (_ws != null || _connecting) return;
            _connecting = true;
            _ws = new WebSocket(_url);
            _ws.OnOpen += () => { _connecting = false; FlushOutbox(); };
            _ws.OnError += (e) => { Debug.LogWarning($"[Net] socket error: {e}"); JoinFailed?.Invoke("Connection error"); };
            _ws.OnClose += (c) => { _ws = null; _connecting = false; };
            _ws.OnMessage += OnMessage;
            try { await _ws.Connect(); }
            catch (Exception ex) { _connecting = false; _ws = null; Debug.LogWarning($"[Net] connect failed: {ex.Message}"); JoinFailed?.Invoke("Can't reach server"); }
        }

        void FlushOutbox() { while (_outbox.Count > 0) RawSend(_outbox.Dequeue()); }

        void Send(string jsonMsg)
        {
            if (_ws != null && _ws.State == WebSocketState.Open) RawSend(jsonMsg);
            else { _outbox.Enqueue(jsonMsg); Connect(); }
        }

        async void RawSend(string jsonMsg)
        {
            try { if (_ws != null) await _ws.SendText(jsonMsg); }
            catch (Exception ex) { Debug.LogWarning($"[Net] send failed: {ex.Message}"); }
        }

        // ---------------- IGameNet: binary game hot-path ----------------
        public event Action<byte[]> OnBinary;

        public void SendBinary(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            if (_ws != null && _ws.State == WebSocketState.Open) RawSendBinary(frame);
            // if not connected yet, drop it — game frames are per-tick + only sent in-game (socket is open by then)
        }
        async void RawSendBinary(byte[] frame)
        {
            try { if (_ws != null) await _ws.Send(frame); }
            catch (Exception ex) { Debug.LogWarning($"[Net] binary send failed: {ex.Message}"); }
        }

        // ---------------- INetworkService: lobby ----------------
        public void CreateRoom()      => Send(Msg($"{{\"t\":\"create\",\"name\":{Q(LocalPlayerName)}}}"));
        public void RefreshRoomList() => Send("{\"t\":\"list\"}");
        public void JoinRoom(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) { JoinFailed?.Invoke("Enter a room code"); return; }
            Send($"{{\"t\":\"join\",\"code\":{Q(code.Trim().ToUpperInvariant())},\"name\":{Q(LocalPlayerName)}}}");
        }
        public void LeaveRoom()       { Send("{\"t\":\"leave\"}"); CurrentRoom = null; }
        public void ClaimRole(Role r) => Send($"{{\"t\":\"role\",\"role\":{(int)r}}}");
        public void SetReady(bool rd) => Send($"{{\"t\":\"ready\",\"ready\":{(rd ? "true" : "false")}}}");
        public void StartShift()      => Send("{\"t\":\"start\"}");

        // ---------------- pump ----------------
        public void Tick(float dt)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
        }

        // ---------------- incoming ----------------
        void OnMessage(byte[] bytes)
        {
            // lobby control is JSON text (starts with '{'); the game hot-path is BINARY (first byte = a small
            // msg id < 0x20, never '{'). Route binary frames to the game-net layer.
            if (bytes == null || bytes.Length == 0) return;
            if (bytes[0] != (byte)'{') { OnBinary?.Invoke(bytes); return; }

            string s = System.Text.Encoding.UTF8.GetString(bytes);
            var msg = JsonUtility.FromJson<SrvMsg>(s);
            switch (msg.t)
            {
                case "joined":
                    CurrentRoom = ToRoom(msg);
                    RoomJoined?.Invoke(CurrentRoom);
                    break;
                case "room":
                    CurrentRoom = ToRoom(msg);
                    RoomUpdated?.Invoke(CurrentRoom);
                    break;
                case "left":
                    CurrentRoom = null;
                    RoomLeft?.Invoke();
                    break;
                case "list":
                    RoomListUpdated?.Invoke(ToListings(msg));
                    break;
                case "start":
                    ShiftStarting?.Invoke(msg.seed);
                    break;
                case "reassign":
                    RoleReassigned?.Invoke(new RoleReassign { newDriverName = msg.newDriver, promotedFromRole = msg.promotedFrom });
                    break;
                case "error":
                    JoinFailed?.Invoke(string.IsNullOrEmpty(msg.reason) ? "Error" : msg.reason);
                    break;
            }
        }

        RoomInfo ToRoom(SrvMsg m)
        {
            var r = new RoomInfo { code = m.code, hostName = m.host, seed = m.seed };
            if (m.slots != null)
                foreach (var s in m.slots)
                    r.slots.Add(new PlayerSlot
                    {
                        role = (Role)s.role,
                        name = s.name,
                        isAI = s.ai,
                        ready = s.ready,
                        isLocal = (s.role == m.yourRole)
                    });
            // ensure 3 slots exist even if server sent fewer (defensive)
            for (int i = 0; i < 3; i++) if (r.Slot((Role)i) == null) r.slots.Add(new PlayerSlot { role = (Role)i });
            return r;
        }

        RoomListing[] ToListings(SrvMsg m)
        {
            if (m.rooms == null) return Array.Empty<RoomListing>();
            var outv = new RoomListing[m.rooms.Length];
            for (int i = 0; i < m.rooms.Length; i++)
                outv[i] = new RoomListing { code = m.rooms[i].code, hostName = m.rooms[i].host, humans = m.rooms[i].humans };
            return outv;
        }

        // ---------------- json helpers ----------------
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Msg(string s) => s;   // readability passthrough

        // server → client message shape (superset; JsonUtility ignores absent fields)
        [Serializable] class SrvMsg
        {
            public string t;
            public string code;
            public string host;
            public int seed;
            public int yourRole = -1;
            public string reason;
            public string newDriver;        // "reassign" event
            public int promotedFrom = -1;   // "reassign" event
            public SrvSlot[] slots;
            public SrvRoomRow[] rooms;
        }
        [Serializable] class SrvSlot { public int role; public string name; public bool ai; public bool ready; }
        [Serializable] class SrvRoomRow { public string code; public string host; public int humans; }
    }
}
