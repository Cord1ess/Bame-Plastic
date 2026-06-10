# Bame Plastic — Backend + Multiplayer Setup

What's implemented (matches `NETWORKING.md`) and how to run it. The backend is the Spring Boot project in
`Backend/backend`; the Unity client talks to it through the `INetworkService` seam (no UI rewrite).

---

## What's built

### Backend (`Backend/backend`, Spring Boot 4 / Java 17 / Maven)
- **WebSocket relay** at `ws://<host>:8080/ws/session` (raw WebSocket, no STOMP):
  - `realtime/Room.java` — in-memory room: 3 role slots (0=Driver,1=Conductor1,2=Conductor2), host, seed.
  - `realtime/RoomManager.java` — thread-safe room registry; 6-char codes; host-picked seed.
  - `realtime/SessionSocketHandler.java` — **lobby over JSON** (create/join/list/role/ready/start/leave +
    room-state pushes) **and binary relay** for the in-game hot path (forwarded verbatim, server decodes nothing).
  - `config/WebSocketConfig.java` — registers the endpoint, allows all origins (dev).
- **REST** (`controller/ShiftController.java`): `POST /api/shift/result`, `GET /api/leaderboard` →
  `entity/ShiftResult.java` + `repository/ShiftResultRepository.java` (additive; doesn't touch the teammate's
  entities).
- **`config/SecurityConfig.java`** — dev: permit-all + CORS (no login yet; players are name-only).
- Config: `application.properties` keeps the teammate's Postgres DB (`bame_plastic_db`, postgres/postgres),
  env-overridable (`DB_URL`/`DB_USER`/`DB_PASS`/`PORT`).

### Unity client
- `Assets/Scripts/Net/WebSocketNetworkService.cs` — implements `INetworkService` over **NativeWebSocket**;
  JSON lobby codec + (later) binary game frames on the same socket. Drop-in swap for `StubNetworkService`.
- `Assets/Scripts/Core/SessionContext.cs` — `USE_BACKEND`/`BACKEND_URL` choose stub vs real; PlayerPrefs
  `net.offline=1` forces the stub at runtime (offline fallback).
- **NativeWebSocket is VENDORED** (MIT) at `Assets/Plugins/NativeWebSocket/` (`WebSocket.cs` standalone+WebGL,
  `WebSocketFactory.cs` + `WebSocket.jslib` for WebGL) — NOT a UPM package, so it needs no internet/git to
  compile. (The git-UPM install failed offline; vendoring is self-contained.)

---

## The lobby protocol (JSON over the WebSocket)

Client → server (text frames):
```
{"t":"create","name":"Alice"}                 create a room (you become host + Driver)
{"t":"join","code":"ABC123","name":"Bob"}     join by code
{"t":"list"}                                  list open rooms
{"t":"role","role":1}                         claim a role (0/1/2)
{"t":"ready","ready":true}                    toggle ready
{"t":"start"}                                 host only; all humans must be ready
{"t":"leave"}                                 leave the room
```
Server → client (text frames):
```
{"t":"joined", code, host, seed, yourRole, slots:[{role,name,ai,ready}×3]}
{"t":"room",   ...same shape, pushed on any change...}
{"t":"list",   rooms:[{code,host,humans}]}
{"t":"start",  seed}                          everyone loads the game scene with this seed
{"t":"left"}                                  you left
{"t":"error",  reason}
```
In-game hot path (later): **binary** frames (first byte = msg id, per the `NETWORKING.md` table) are relayed
verbatim to the other room members. `WebSocketNetworkService.OnMessage` already ignores non-`{` frames so the
game net layer can claim them.

---

## Run it

### 1. Backend
Prereqs: **JDK 17+** and **PostgreSQL** running with a DB named `bame_plastic_db` (user `postgres`, pass
`postgres` — or override with env vars / edit `application.properties`).
```
# create the DB once (psql or pgAdmin):
#   CREATE DATABASE bame_plastic_db;

cd Backend/backend
./mvnw spring-boot:run            # Windows: .\mvnw.cmd spring-boot:run
```
Server is up on `http://localhost:8080`. Tables auto-create (`ddl-auto=update`).
Quick check: `GET http://localhost:8080/api/leaderboard` → `[]`.

### 2. Unity
- Open the project; Unity resolves NativeWebSocket from the manifest (needs internet once).
- `SessionContext.USE_BACKEND` is `true` and `BACKEND_URL` is `ws://localhost:8080/ws/session`.
- Press Play → Main Menu → **Play Online** → **Host** (create) or **Join** with a code.
- To play WITHOUT the server, set `USE_BACKEND=false` (or PlayerPrefs `net.offline=1`) → uses the stub.

### 3. Test multiplayer locally (2+ clients)
- Run one client in the Editor and a second as a **standalone build** (or two builds), both pointing at
  `ws://localhost:8080`. Host in one, copy the room code, Join in the other. Ready up → host Starts → both
  load the game scene with the same seed.

---

## Status & next (per NETWORKING.md build order)
- [x] 1. Spring Boot relay + RoomInit/join/leave (lobby).
- [x] 2. Unity `NetClient` (NativeWebSocket) + lobby codec.
- [ ] 3. Driver-authority `BusState` broadcast (binary) + conductor interpolated bus proxy. ← next
- [ ] 4. Conductor intents → driver applies → passenger events broadcast.
- [x] 5. REST shift result + leaderboard (endpoints ready; wire `ShiftManager` to POST on shift end).
- [ ] 6. Matchmaking polish (room browser is functional via `{"t":"list"}`).

The lobby is end-to-end now. The in-game state sync (steps 3–4) is the next phase — the binary relay path is
already open on the same socket.
