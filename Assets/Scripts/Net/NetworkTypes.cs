using System;
using System.Collections.Generic;

namespace BamePlastic.Net
{
    /// The three crew roles. Driver is mandatory in a room (the authoritative simulator later); the two
    /// conductor roles are auto-filled by AI if no human takes them.
    public enum Role { Driver = 0, Conductor1 = 1, Conductor2 = 2 }

    /// One occupant of a room slot. A slot is either a human (Name set, IsAI false), an AI fill (IsAI true),
    /// or empty (Name null/empty, IsAI false).
    [Serializable]
    public class PlayerSlot
    {
        public Role role;
        public string name;          // player display name (empty = nobody)
        public bool isLocal;         // this is the local player
        public bool isAI;            // filled by AI (only conductors)
        public bool ready;

        public bool Occupied => isAI || !string.IsNullOrEmpty(name);
        public string Display => isAI ? "AI" : (string.IsNullOrEmpty(name) ? "—" : name);
    }

    /// A room (lobby). Always has exactly 3 slots, indexed by Role. Carries the seed used to deterministically
    /// generate the shift (road/traffic/rivals) so all clients see the same world.
    [Serializable]
    public class RoomInfo
    {
        public string code;          // e.g. "BAME-4F2K"
        public string hostName;      // the driver hosts/starts
        public int seed;
        public List<PlayerSlot> slots = new List<PlayerSlot>();

        public PlayerSlot Slot(Role r) => slots.Find(s => s.role == r);
        public PlayerSlot Driver => Slot(Role.Driver);
        public int HumanCount { get { int n = 0; foreach (var s in slots) if (!s.isAI && !string.IsNullOrEmpty(s.name)) n++; return n; } }

        /// Everyone who is a human (non-AI, occupied) is ready, and a driver exists.
        public bool AllReady
        {
            get
            {
                if (Driver == null || !Driver.Occupied) return false;
                foreach (var s in slots)
                    if (!s.isAI && !string.IsNullOrEmpty(s.name) && !s.ready) return false;
                return true;
            }
        }
    }

    /// A summary row for the room browser.
    [Serializable]
    public struct RoomListing
    {
        public string code;
        public string hostName;
        public int humans;           // humans in the room (of 3)
    }
}
