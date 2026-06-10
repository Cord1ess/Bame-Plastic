using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BamePlastic.Net
{
    /// In-memory fake backend so the whole menu/lobby flow works and FEELS live before Spring Boot exists.
    /// Implements INetworkService exactly as the real one will, so it's a drop-in swap later.
    ///
    /// Simulates: room-code generation, a browser list of open rooms, and bot players who trickle into your
    /// room and ready up on timers — so the lobby isn't a dead screen. Enforces the real rules: a room always
    /// has a Driver (host); conductor slots auto-fill with AI on start; roles are freely swappable; only the
    /// driver/host can start, and only once all humans are ready.
    public class StubNetworkService : INetworkService
    {
        static readonly string[] BotNames = { "Ravi", "Karim", "Sumi", "Jamal", "Nadia", "Tariq", "Bithi", "Faruk" };

        public string LocalPlayerName { get; set; } = "You";
        public RoomInfo CurrentRoom { get; private set; }

        public event Action<RoomInfo> RoomJoined;
        public event Action<RoomInfo> RoomUpdated;
        public event Action RoomLeft;
        public event Action<string> JoinFailed;
        public event Action<RoomListing[]> RoomListUpdated;
        public event Action<int> ShiftStarting;

        // simulation timers
        float _botJoinTimer;
        float _botReadyTimer;
        int _seedCounter = 1000;

        // ---- helpers ----
        static string NewCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new System.Text.StringBuilder("BAME-");
            for (int i = 0; i < 4; i++) sb.Append(chars[Random.Range(0, chars.Length)]);
            return sb.ToString();
        }

        RoomInfo MakeRoom(string code, string host, bool seatLocalAsDriver)
        {
            var r = new RoomInfo { code = code, hostName = host, seed = _seedCounter++ };
            for (int i = 0; i < 3; i++) r.slots.Add(new PlayerSlot { role = (Role)i });
            if (seatLocalAsDriver)
            {
                var d = r.Driver;
                d.name = LocalPlayerName; d.isLocal = true; d.ready = false;
            }
            return r;
        }

        // ---- room lifecycle ----
        public void CreateRoom()
        {
            CurrentRoom = MakeRoom(NewCode(), LocalPlayerName, true);   // host = local = Driver
            _botJoinTimer = Random.Range(2.5f, 5f);
            _botReadyTimer = Random.Range(4f, 7f);
            RoomJoined?.Invoke(CurrentRoom);
        }

        public void RefreshRoomList()
        {
            // fabricate a handful of open rooms hosted by bots
            int n = Random.Range(2, 5);
            var list = new RoomListing[n];
            for (int i = 0; i < n; i++)
                list[i] = new RoomListing { code = NewCode(), hostName = BotNames[Random.Range(0, BotNames.Length)], humans = Random.Range(1, 3) };
            RoomListUpdated?.Invoke(list);
        }

        public void JoinRoom(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Trim().Length < 4) { JoinFailed?.Invoke("Enter a valid room code"); return; }
            code = code.Trim().ToUpperInvariant();
            if (!code.StartsWith("BAME-")) code = "BAME-" + code;

            // joining an existing (bot-hosted) room: a bot is the Driver, you take the first free conductor
            CurrentRoom = MakeRoom(code, BotNames[Random.Range(0, BotNames.Length)], false);
            var driver = CurrentRoom.Driver; driver.name = CurrentRoom.hostName; driver.ready = true;
            var c1 = CurrentRoom.Slot(Role.Conductor1);
            c1.name = LocalPlayerName; c1.isLocal = true;
            _botReadyTimer = Random.Range(3f, 6f);
            RoomJoined?.Invoke(CurrentRoom);
        }

        public void LeaveRoom()
        {
            CurrentRoom = null;
            RoomLeft?.Invoke();
        }

        // ---- in-room actions ----
        public void ClaimRole(Role role)
        {
            if (CurrentRoom == null) return;
            PlayerSlot local = LocalSlot();
            if (local == null || local.role == role) return;

            PlayerSlot target = CurrentRoom.Slot(role);

            // SWAP semantics: whoever is in the target role (human, AI, or empty) trades places with the local
            // player. Driver-mandatory is preserved automatically because a swap never empties a role.
            string tName = target.name; bool tAI = target.isAI; bool tLocal = target.isLocal; bool tReady = target.ready;
            target.name = local.name; target.isAI = local.isAI; target.isLocal = local.isLocal; target.ready = local.ready;
            local.name = tName; local.isAI = tAI; local.isLocal = tLocal; local.ready = tReady;

            // host follows the Driver seat
            CurrentRoom.hostName = CurrentRoom.Driver.Occupied ? CurrentRoom.Driver.name : CurrentRoom.hostName;
            RoomUpdated?.Invoke(CurrentRoom);
        }

        public void SetReady(bool ready)
        {
            var local = LocalSlot();
            if (local == null) return;
            local.ready = ready;
            RoomUpdated?.Invoke(CurrentRoom);
        }

        public void StartShift()
        {
            if (CurrentRoom == null) return;
            if (!IsLocalDriver()) { JoinFailed?.Invoke("Only the driver can start"); return; }
            if (!CurrentRoom.AllReady) { JoinFailed?.Invoke("Everyone must be ready"); return; }
            // empty conductor seats become AI on start
            foreach (var s in CurrentRoom.slots)
                if (!s.Occupied) { s.isAI = true; }
            ShiftStarting?.Invoke(CurrentRoom.seed);
        }

        // ---- simulation ----
        public void Tick(float dt)
        {
            if (CurrentRoom == null) return;

            // a bot trickles into an empty conductor seat once, for liveliness (only in rooms you host)
            if (_botJoinTimer > 0f)
            {
                _botJoinTimer -= dt;
                if (_botJoinTimer <= 0f)
                {
                    PlayerSlot free = FirstFreeConductor();
                    if (free != null)
                    {
                        free.name = BotNames[Random.Range(0, BotNames.Length)];
                        RoomUpdated?.Invoke(CurrentRoom);
                    }
                }
            }

            // bots ready up after a beat so "everyone ready" can actually happen
            if (_botReadyTimer > 0f)
            {
                _botReadyTimer -= dt;
                if (_botReadyTimer <= 0f)
                {
                    bool changed = false;
                    foreach (var s in CurrentRoom.slots)
                        if (!s.isLocal && !s.isAI && !string.IsNullOrEmpty(s.name) && !s.ready) { s.ready = true; changed = true; }
                    if (changed) RoomUpdated?.Invoke(CurrentRoom);
                }
            }
        }

        // ---- internals ----
        PlayerSlot LocalSlot() => CurrentRoom?.slots.Find(s => s.isLocal);
        bool IsLocalDriver() { var d = CurrentRoom?.Driver; return d != null && d.isLocal; }
        PlayerSlot FirstFreeConductor()
        {
            foreach (var s in CurrentRoom.slots)
                if (s.role != Role.Driver && !s.Occupied) return s;
            return null;
        }
    }
}
