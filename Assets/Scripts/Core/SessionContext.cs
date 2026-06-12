using UnityEngine;
using BamePlastic.Net;

/// Persists across scenes (DontDestroyOnLoad). Owns the single network service the lobby talks to, and
/// carries the chosen room / local role / world seed from the menu into the game scene so the shift spawns
/// the same deterministic world and gives the player the right crew role.
///
/// One access point: SessionContext.Instance. Created by Bootstrap (or lazily on first access) so it exists
/// whether you start from the menu or hit Play directly in the game scene (solo fallback).
public class SessionContext : MonoBehaviour
{
    public static SessionContext Instance { get; private set; }

    /// The backend seam. Stub now; swap to a WebSocket impl later without touching the UI.
    public INetworkService Net { get; private set; }

    // ---- carried into the game scene ----
    public bool IsMultiplayer { get; private set; }
    public Role LocalRole { get; private set; } = Role.Driver;
    public int Seed { get; private set; }
    public RoomInfo Room { get; private set; }

    // ---- backend selection ----
    // The server to connect to is chosen IN-GAME via the Play Online server picker (ServerConfig, PlayerPrefs).
    // "Offline" → the in-memory stub (no server needed). Otherwise → the WebSocket relay at ServerConfig.WsUrl.
    static INetworkService MakeService()
    {
        if (ServerConfig.Offline) return new StubNetworkService();
        return new WebSocketNetworkService(ServerConfig.WsUrl);
    }

    /// Rebuild the network service from the CURRENT ServerConfig (called when the player changes the server in
    /// the picker). Only valid before joining a room. Carries the player name over.
    public void RebuildService()
    {
        string name = Net != null ? Net.LocalPlayerName : "Player";
        Net = MakeService();
        Net.LocalPlayerName = name;
    }

    public static SessionContext Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("SessionContext");
            Instance = go.AddComponent<SessionContext>();
            DontDestroyOnLoad(go);
            Instance.Net = MakeService();
        }
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (Net == null) Net = MakeService();
    }

    void Update() => Net?.Tick(Time.deltaTime);

    /// Called by the lobby when the driver starts: capture the room/role/seed for the game scene.
    public void BeginMultiplayerShift(RoomInfo room, int seed)
    {
        IsMultiplayer = true;
        Room = room;
        Seed = seed;
        var local = room?.slots.Find(s => s.isLocal);
        LocalRole = local != null ? local.role : Role.Driver;
    }

    /// Solo quick-play: you drive, conductors are AI, fresh random seed.
    public void BeginSoloShift()
    {
        IsMultiplayer = false;
        Room = null;
        LocalRole = Role.Driver;
        Seed = Random.Range(1, int.MaxValue);
    }
}
