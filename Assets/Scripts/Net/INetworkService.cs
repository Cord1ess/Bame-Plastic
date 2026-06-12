using System;

namespace BamePlastic.Net
{
    /// The seam between the lobby UI and the backend. The whole menu/lobby talks ONLY to this interface, so
    /// the faked StubNetworkService (now) and a real WebSocketNetworkService against Spring Boot's
    /// /ws/session/{roomId} (later) are drop-in interchangeable — no UI changes.
    ///
    /// All calls are async-by-event: a method kicks off the action and the result arrives via the events,
    /// matching how a real socket behaves (request → server pushes the new room state to everyone).
    public interface INetworkService
    {
        // ---- session ----
        string LocalPlayerName { get; set; }
        RoomInfo CurrentRoom { get; }

        // ---- room lifecycle ----
        void CreateRoom();                         // become host + Driver; RoomJoined fires with the new room
        void RefreshRoomList();                    // RoomListUpdated fires with open rooms (the browser)
        void JoinRoom(string code);                // join by code; RoomJoined or JoinFailed fires
        void LeaveRoom();                          // RoomLeft fires

        // ---- in-room actions ----
        void ClaimRole(Role role);                 // take/swap into a role (driver-mandatory rules enforced)
        void SetReady(bool ready);                 // toggle local ready
        void StartShift();                         // driver/host only; ShiftStarting fires with the seed

        // ---- events (server → client) ----
        event Action<RoomInfo> RoomJoined;         // you joined/created a room
        event Action<RoomInfo> RoomUpdated;        // any change to the current room (slots/ready)
        event Action RoomLeft;
        event Action<string> JoinFailed;           // reason
        event Action<RoomListing[]> RoomListUpdated;
        event Action<int> ShiftStarting;           // seed — load the game scene now
        event Action<RoleReassign> RoleReassigned; // a player dropped mid-shift → the driver seat failed over

        // pumped each frame by the owner so the stub can simulate timers; real impl can no-op or poll socket
        void Tick(float dt);
    }
}
