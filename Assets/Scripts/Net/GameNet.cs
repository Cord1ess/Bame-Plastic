using System;
using UnityEngine;

namespace BamePlastic.Net
{
    /// The in-game multiplayer sync hub (driver-authoritative model, NETWORKING.md). Auto-spawned in the game
    /// scene; no-op in solo (IsMultiplayer == false) so single-player is unchanged. Owns the binary codec and
    /// routes frames between the bus, conductors, and passenger systems:
    ///   - DRIVER: broadcasts BusState; receives conductor intents, applies them, broadcasts results.
    ///   - CONDUCTOR: drives a proxy bus from interpolated BusState; sends pose + action intents; applies results.
    /// PLAY-ONLY (no [ExecuteAlways]); created once, torn down on scene unload.
    public class GameNet : MonoBehaviour
    {
        public static GameNet Instance { get; private set; }

        IGameNet _net;
        public bool Active { get; private set; }     // true only in a real multiplayer session
        public Role LocalRole { get; private set; }
        public bool IsDriver => LocalRole == Role.Driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
            if (FindAnyObjectByType<GameNet>() != null) return;
            var go = new GameObject("GameNet");
            go.AddComponent<GameNet>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var ctx = SessionContext.Instance;
            if (ctx == null || !ctx.IsMultiplayer || ctx.Net is not IGameNet game)
            {
                Active = false;            // SOLO / no real backend → fully inert; everything runs locally as before
                return;
            }
            _net = game;
            LocalRole = ctx.LocalRole;
            Active = true;
            _net.OnBinary += OnFrame;
        }

        void OnDestroy()
        {
            if (_net != null) _net.OnBinary -= OnFrame;
            if (Instance == this) Instance = null;
        }

        // ---- outbound ----
        readonly NetWriter _w = new NetWriter(32);
        /// Begin a frame with `id`; write payload via the returned writer; call Flush() to send.
        NetWriter Begin(byte id) { _w.Reset(); _w.U8(id); return _w; }
        void Flush() { _net?.SendBinary(_w.ToArray()); }

        // ---- inbound routing (handlers filled in by later steps) ----
        void OnFrame(byte[] bytes)
        {
            if (!Active || bytes == null || bytes.Length < 1) return;
            byte id = bytes[0];
            var r = new NetReader(bytes, 1);
            switch (id)
            {
                case MsgId.BusState:        OnBusState(ref r); break;
                case MsgId.AvatarPose:      OnAvatarPose(ref r); break;
                case MsgId.IntentCollect:   OnIntentCollect(ref r); break;
                case MsgId.IntentGrab:      OnIntentGrab(ref r); break;
                case MsgId.IntentThrow:     OnIntentThrow(ref r); break;
                case MsgId.IntentShove:     OnIntentShove(ref r); break;
                case MsgId.PassengerBoard:  OnPassengerBoard(ref r); break;
                case MsgId.PassengerAlight: OnPassengerAlight(ref r); break;
                case MsgId.FareCollected:   OnFareCollected(ref r); break;
                case MsgId.EarningsSync:    OnEarningsSync(ref r); break;
                case MsgId.PauseState:      OnPauseState(ref r); break;
                case MsgId.RoleReassign:    OnRoleReassign(ref r); break;
            }
        }

        void Start()
        {
            if (!Active) return;
            // CONDUCTOR: the bus is a PROXY (driven by the driver's pose), not simulated here.
            if (!IsDriver)
            {
                _bus = BusController.Instance;
                if (_bus != null) _bus.SetProxyMode(true);
            }
        }

        void Update()
        {
            if (!Active) return;
            TickSend();
            TickInterp();
        }

        // ================= BUS POSE SYNC =================
        BusController _bus;
        float _busSendTimer;
        Vector3 _lastSentPos; float _lastSentYaw = -999f;
        public const float BusSendHz = 25f;
        public const float InterpDelay = 0.09f;   // ~90ms behind real time → smooths 25Hz into fluid motion

        // interpolation buffer (two latest snapshots) for the proxy bus
        struct BusSnap { public double t; public Vector3 pos; public float yaw; public float speed; public bool c1Aboard; }
        BusSnap _snapA, _snapB; bool _haveA, _haveB;

        void TickSend()
        {
            EnsureAvatars();
            SendAvatar();        // every role broadcasts its own crew member's pose
            if (!IsDriver) return;
            if (_bus == null) _bus = BusController.Instance;
            if (_bus == null) return;

            _busSendTimer += Time.deltaTime;
            if (_busSendTimer < 1f / BusSendHz) return;
            _busSendTimer = 0f;

            Vector3 pos = _bus.transform.position;
            float yaw = _bus.transform.eulerAngles.y;
            // adaptive delta-skip: don't resend if barely moved (after interpolation the eye can't tell)
            if (_lastSentYaw > -900f && (pos - _lastSentPos).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentYaw)) < 0.5f)
                return;
            _lastSentPos = pos; _lastSentYaw = yaw;

            bool stopReq = BusPassengers.Instance != null && BusPassengers.Instance.StopRequested;
            var w = Begin(MsgId.BusState);
            w.PosCm(pos.x); w.PosCm(pos.z); w.Yaw(yaw); w.SpeedCm(_bus.SpeedMps); w.Bool(_c1AboardAuthoritative); w.Bool(stopReq);
            Flush();

            SendEarningsSync();   // driver also reconciles earnings/health to the conductors (low rate)
        }

        void OnBusState(ref NetReader r)
        {
            if (IsDriver) return;   // driver is authoritative; ignore (shouldn't receive its own)
            float x = r.PosCm(), z = r.PosCm(), yaw = r.Yaw(), spd = r.SpeedCm();
            bool c1 = r.Bool();
            _stopRequestedProxy = r.Bool();
            // keep current Y (ground height comes from the local road, which is identical via the seed)
            float y = _bus != null ? _bus.transform.position.y : 0f;
            var snap = new BusSnap { t = NowSeconds(), pos = new Vector3(x, y, z), yaw = yaw, speed = spd, c1Aboard = c1 };
            _snapA = _snapB; _haveA = _haveB;     // shift
            _snapB = snap; _haveB = true;
            _c1AboardProxy = c1;
        }

        double NowSeconds() => Time.realtimeSinceStartupAsDouble;

        // C1-aboard flag: the DRIVER owns the authoritative value (set from C1's pose in step 3); proxies mirror
        // whatever BusState carried (for HUD / speed display).
        bool _c1AboardAuthoritative = true;
        bool _c1AboardProxy = true;
        public void SetC1AboardAuthoritative(bool aboard) { _c1AboardAuthoritative = aboard; }
        public bool C1Aboard => IsDriver ? _c1AboardAuthoritative : _c1AboardProxy;

        // stop-request flag synced on BusState: driver computes from its aboard riders; conductor clients read
        // this so the roof indicator shows for everyone (their mirrored riders don't run the ride timer).
        bool _stopRequestedProxy;
        public bool StopRequestedNet => IsDriver
            ? (BusPassengers.Instance != null && BusPassengers.Instance.StopRequested)
            : _stopRequestedProxy;

        void TickInterp()
        {
            if (IsDriver) return;
            if (_bus == null) { _bus = BusController.Instance; if (_bus == null) return; }
            if (!_haveB) return;
            if (!_haveA) { _bus.ProxySetPose(_snapB.pos, _snapB.yaw, _snapB.speed); return; }

            // render InterpDelay behind real time, lerping between the two latest snapshots → fluid motion
            double renderT = NowSeconds() - InterpDelay;
            double span = _snapB.t - _snapA.t;
            float u = span > 1e-4 ? Mathf.Clamp01((float)((renderT - _snapA.t) / span)) : 1f;
            Vector3 pos = Vector3.LerpUnclamped(_snapA.pos, _snapB.pos, u);
            float yaw = Mathf.LerpAngle(_snapA.yaw, _snapB.yaw, u);
            float spd = Mathf.Lerp(_snapA.speed, _snapB.speed, u);
            _bus.ProxySetPose(pos, yaw, spd);
        }

        // ================= REMOTE AVATARS + AVATAR POSE =================
        // We render the TWO roles the local player does NOT control as interpolated billboards. Each client
        // broadcasts its own crew member's pose (~AvatarHz). Driver/C2 poses are CABIN-local (ride the bus);
        // C1 is WORLD (runs around). C1's pose also carries the ABOARD bit → the driver sets the speed gate.
        public const float AvatarHz = 15f;
        float _avatarSendTimer;
        readonly RemoteAvatar[] _avatars = new RemoteAvatar[3];   // indexed by Role; null for the local role
        Transform _cabin;
        RoleController _roleController;     // cached (avatar pose lookup runs per-send, was a per-call Find)

        static readonly Color[] CrewColors = {
            new Color(0.95f, 0.8f, 0.35f),   // Driver — gold
            new Color(0.5f, 0.85f, 1f),      // Conductor1 — cyan
            new Color(0.6f, 0.95f, 0.6f),    // Conductor2 — green
        };

        void EnsureAvatars()
        {
            if (_cabin == null && BusPassengers.Instance != null) _cabin = BusPassengers.Instance.Cabin;
            for (int i = 0; i < 3; i++)
            {
                if ((Role)i == LocalRole) continue;            // never an avatar for our own role
                if (_avatars[i] == null)
                {
                    _avatars[i] = RemoteAvatar.Create("Remote_" + (Role)i, CrewColors[i], 1.9f, _cabin);
                    ApplyAvatarArt(_avatars[i], (Role)i);      // real sprite for the other player's crew
                }
            }
        }

        // give a remote-player avatar the right real sprite for its role (walk cycle auto-animates from motion).
        static void ApplyAvatarArt(RemoteAvatar av, Role role)
        {
            if (av == null || av.View == null) return;
            CharacterSprites.Build();
            switch (role)
            {
                case Role.Conductor1: if (CharacterSprites.C1Walk != null) av.View.SetWalk(CharacterSprites.C1Walk, 0.08f); break;
                case Role.Conductor2: if (CharacterSprites.C2WalkFront != null) av.View.SetWalk(CharacterSprites.C2WalkFront, 0.1f); break;
                // the driver sits at the wheel — others in the bus mostly see his BACK (front sprite as fallback)
                default: { var d = CharacterSprites.DriverBack ?? CharacterSprites.DriverFront; if (d != null) av.View.SetSprite(d); break; }
            }
        }

        // where is the LOCAL player's crew member right now, and in what space?
        bool LocalAvatarPose(out Vector3 pos, out RemoteAvatar.Mode mode, out bool aboard)
        {
            pos = Vector3.zero; mode = RemoteAvatar.Mode.Cabin; aboard = true;
            if (_roleController == null) _roleController = FindAnyObjectByType<RoleController>();   // cache (was per-send)
            var rc = _roleController;
            if (rc == null) return false;
            switch (LocalRole)
            {
                case Role.Driver:
                    if (BusController.Instance == null || _cabin == null) return false;
                    pos = _cabin.InverseTransformPoint(BusController.Instance.transform.position + Vector3.up * 1.2f);
                    mode = RemoteAvatar.Mode.Cabin; return true;
                case Role.Conductor1:
                    var c1 = rc.Conductor1Transform;
                    if (c1 == null) return false;
                    aboard = rc.Conductor1Aboard;
                    if (aboard && _cabin != null) { pos = _cabin.InverseTransformPoint(c1.position); mode = RemoteAvatar.Mode.Cabin; }
                    else { pos = c1.position; mode = RemoteAvatar.Mode.World; }
                    return true;
                case Role.Conductor2:
                    var c2 = rc.Conductor2Transform;
                    if (c2 == null || _cabin == null) return false;
                    pos = c2.localPosition; mode = RemoteAvatar.Mode.Cabin; return true;
            }
            return false;
        }

        void SendAvatar()
        {
            _avatarSendTimer += Time.deltaTime;
            if (_avatarSendTimer < 1f / AvatarHz) return;
            _avatarSendTimer = 0f;
            if (!LocalAvatarPose(out Vector3 pos, out var mode, out bool aboard)) return;
            var w = Begin(MsgId.AvatarPose);
            w.U8((byte)LocalRole).U8((byte)mode).PosCm16(pos.x).PosCm16(pos.y).PosCm16(pos.z).Bool(aboard);
            Flush();
        }

        void OnAvatarPose(ref NetReader r)
        {
            int slot = r.U8();
            var mode = (RemoteAvatar.Mode)r.U8();
            float x = r.PosCm16(), y = r.PosCm16(), z = r.PosCm16();
            bool aboard = r.Bool();
            if (slot < 0 || slot > 2 || (Role)slot == LocalRole) return;
            EnsureAvatars();
            var av = _avatars[slot];
            if (av == null) return;
            Vector3 pos = new Vector3(x, y, z);
            // Cabin-mode coords are cabin-local; World-mode are world. RemoteAvatar handles parenting.
            av.SetPose(pos, mode);
            // the DRIVER reads C1's aboard bit to drive the authoritative speed gate
            if (IsDriver && slot == (int)Role.Conductor1) _c1AboardAuthoritative = aboard;
        }
        // ================= FARE COLLECT (intent → driver authority → result) =================
        public void SendCollectIntent(ushort netId) { if (!Active || IsDriver || netId == 0) return; var w = Begin(MsgId.IntentCollect); w.U16(netId); Flush(); }

        void OnIntentCollect(ref NetReader r)
        {
            ushort netId = r.U16();
            if (!IsDriver) return;                 // only the driver applies intents (authority)
            var p = FindAboardByNet(netId);
            if (p == null) return;
            int amt = p.Collect();
            if (amt > 0)
            {
                if (ShiftManager.Instance != null) { ShiftManager.Instance.AddEarnings(amt); ShiftManager.Instance.ReportFareCollected(); }
                DriverFareCollected(p, amt, (byte)Role.Conductor2);   // a conductor collected it
            }
        }

        /// DRIVER: broadcast a confirmed fare so every client marks the rider paid + (conductors) reconcile HUD.
        public void DriverFareCollected(Passenger p, int amount, byte bySlot)
        {
            if (!Active || !IsDriver || p == null || p.NetId == 0) return;
            var w = Begin(MsgId.FareCollected); w.U16(p.NetId).U16((ushort)Mathf.Clamp(amount, 0, 65535)).U8(bySlot); Flush();
        }

        // driver-side: find an aboard passenger by NetId (it ran the real board, so the passenger has the id)
        Passenger FindAboardByNet(ushort netId)
        {
            var bp = BusPassengers.Instance; if (bp == null) return null;
            var list = bp.Aboard;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].NetId == netId) return list[i];
            return null;
        }

        void OnIntentGrab(ref NetReader r) { ushort id = r.U16(); /* step-6b: driver applies grab; minimal for now */ }
        void OnIntentThrow(ref NetReader r) { /* step-6b */ }
        void OnIntentShove(ref NetReader r) { ushort id = r.U16(); if (IsDriver) { var p = FindAboardByNet(id); if (p != null) BusPassengers.Instance?.ShovePassenger(p); } }
        // ================= PASSENGER BOARD / ALIGHT (driver-authoritative) =================
        ushort _nextNetId = 1;

        /// DRIVER: a passenger just boarded → assign a NetId + broadcast so conductors mirror the exact rider+spot.
        public void DriverPassengerBoarded(Passenger p, int spotIdx, bool isSeat)
        {
            if (!Active || !IsDriver || p == null) return;
            if (p.NetId == 0) p.NetId = _nextNetId++;
            var w = Begin(MsgId.PassengerBoard);
            w.U16(p.NetId).U16((ushort)Mathf.Max(0, p.PoolIndex)).U8((byte)spotIdx).Bool(isSeat);
            Flush();
        }
        /// DRIVER: a passenger alighted → broadcast so conductors remove the same rider.
        public void DriverPassengerAlighted(Passenger p)
        {
            if (!Active || !IsDriver || p == null || p.NetId == 0) return;
            var w = Begin(MsgId.PassengerAlight); w.U16(p.NetId); Flush();
        }

        // conductor-side map of NetId → passenger (mirrors the driver's cabin)
        readonly System.Collections.Generic.Dictionary<ushort, Passenger> _byNet = new System.Collections.Generic.Dictionary<ushort, Passenger>();
        public Passenger ByNetId(ushort id) => _byNet.TryGetValue(id, out var p) ? p : null;

        void OnPassengerBoard(ref NetReader r)
        {
            ushort netId = r.U16(); ushort poolIdx = r.U16(); int spotIdx = r.U8(); bool isSeat = r.Bool();
            if (IsDriver) return;   // driver already has the real one
            var pool = PassengerPool.Instance; var bp = BusPassengers.Instance;
            if (pool == null || bp == null) return;
            Passenger p = pool.ByIndex(poolIdx);
            if (p == null) return;
            p.NetId = netId;
            _byNet[netId] = p;
            bp.MirrorBoard(p, spotIdx, isSeat);   // place this exact rider in the same cabin spot (no stop sim)
        }

        void OnPassengerAlight(ref NetReader r)
        {
            ushort netId = r.U16();
            if (IsDriver) return;
            if (_byNet.TryGetValue(netId, out var p) && p != null)
            {
                BusPassengers.Instance?.MirrorAlight(p);
                _byNet.Remove(netId);
            }
        }

        void OnFareCollected(ref NetReader r)
        {
            ushort netId = r.U16(); int amount = r.U16(); int bySlot = r.U8();
            if (IsDriver) return;   // driver already applied it
            // mark the rider paid (turns green) on the conductor's screen; earnings come via EarningsSync
            var p = ByNetId(netId) ?? FindAboardByNet(netId);
            if (p != null) p.MirrorPaid();
        }

        // ================= EARNINGS / HEALTH RECONCILE (driver → room, low rate) =================
        float _earnSyncTimer;
        public const float EarnSyncHz = 2f;
        void SendEarningsSync()
        {
            if (!IsDriver) return;
            _earnSyncTimer += Time.deltaTime;
            if (_earnSyncTimer < 1f / EarnSyncHz) return;
            _earnSyncTimer = 0f;
            var sm = ShiftManager.Instance; if (sm == null) return;
            var w = Begin(MsgId.EarningsSync);
            w.I32(sm.Earnings).I16((short)Mathf.Clamp(Mathf.RoundToInt(sm.Health), short.MinValue, short.MaxValue));
            Flush();
        }
        void OnEarningsSync(ref NetReader r)
        {
            int earn = r.I32(); int health = r.I16();
            if (IsDriver) return;
            // authoritatively set the conductor's HUD numbers to the driver's truth
            ShiftManager.Instance?.SetFromNetwork(earn, health);
        }

        // ================= PAUSE (driver-authoritative) =================
        // Only the DRIVER can pause the SHARED game. A conductor's pause request is sent to the driver, who
        // toggles the authoritative pause and broadcasts PauseState to everyone → all three freeze together.
        public event System.Action<bool, int> PauseChanged;   // (paused, whoSlot)

        /// DRIVER: set + broadcast the shared pause state. (Conductors call RequestPause instead.)
        public void DriverSetPause(bool paused, int whoSlot)
        {
            if (!Active || !IsDriver) return;
            var w = Begin(MsgId.PauseState); w.Bool(paused).U8((byte)whoSlot); Flush();
            PauseChanged?.Invoke(paused, whoSlot);
        }

        /// CONDUCTOR: ask the driver to pause/resume (driver authority). Reuses PauseState as an intent frame —
        /// the driver echoes the authoritative state back to the room.
        public void RequestPause(bool paused)
        {
            if (!Active || IsDriver) return;
            var w = Begin(MsgId.PauseState); w.Bool(paused).U8((byte)LocalRole); Flush();
        }

        void OnPauseState(ref NetReader r)
        {
            bool paused = r.Bool(); int who = r.U8();
            if (IsDriver)
            {
                // a conductor requested → become authoritative + re-broadcast so everyone (incl. requester) agrees
                DriverSetPause(paused, who);
            }
            else
            {
                PauseChanged?.Invoke(paused, who);
            }
        }

        // ================= ROLE FAILOVER =================
        // The driver host tells the room a new driver slot has been assigned (after the old driver dropped).
        public event System.Action<int> RoleReassigned;   // newDriverSlot

        public void BroadcastRoleReassign(int newDriverSlot)
        {
            if (!Active) return;
            var w = Begin(MsgId.RoleReassign); w.U8((byte)newDriverSlot); Flush();
        }

        void OnRoleReassign(ref NetReader r)
        {
            int newDriver = r.U8();
            RoleReassigned?.Invoke(newDriver);
        }

        /// Promote THIS client to the driver authority at runtime (used by the failover path). Switches the bus
        /// from proxy to locally simulated and flips role bookkeeping so subsequent sends are driver-authoritative.
        public void PromoteToDriver()
        {
            LocalRole = Role.Driver;
            if (_bus == null) _bus = BusController.Instance;
            if (_bus != null) _bus.SetProxyMode(false);
        }
    }
}
