# Bame Plastic — Networking & Multiplayer Architecture

The multiplayer plan for Bame Plastic: **3 players share one bus** (1 driver + 2 conductors), competing
against AI rivals. Many such 3-player **rooms** run at once. The backend is **Spring Boot** (a course
requirement). This doc is the spec to build against — protocol, message types, authority model, and the
efficiency rules that make it feel lag-free.

> **Reality check on "no lag":** network latency is physics — a round trip can't be zero. The goal is to
> make latency **invisible**, not absent. This design does that via driver-authority (the driver has zero
> perceived lag), interpolation (hides the conductors' view delay), deterministic local generation (the
> road never syncs), and tiny binary messages. That is the achievable, fast version of the goal.

---

## Model: rooms (lobbies), not a global 3-player cap

"3 players" is **per room**, not a server total. The server hosts **many independent rooms** at once;
players in one room never see or affect another. Capacity is bounded by concurrent WebSocket connections,
not by the 3-per-bus rule.

```
Spring Boot server
├── Room ABC123 → driver + conductor1 + conductor2   (one bus)
├── Room XYZ789 → driver + conductor1 + conductor2   (another bus)
├── Room DEF456 → driver (+ waiting for 2 more)
└── … hundreds of rooms
```

**Capacity (for context):** each room is ~20 small messages/sec. A small cloud VM handles ~1k–5k
concurrent connections (≈300–1600 rooms); a tuned server, 10k–50k+. A course project sees single-digit to
low-dozens of rooms — capacity is a non-issue. The server is a **dumb relay** (forwards bytes, simulates
nothing), which is what keeps it cheap and scalable.

---

## Authority model (this is what makes it fast)

**The DRIVER's client is authoritative** for the bus AND the cabin/passengers. Reasons it's the fast,
correct choice:
- Only one player drives one bus → no contention, no conflict resolution, no rollback needed.
- The driver simulates locally → **driving feels instant for the driver, always** (zero perceived lag).
- Conductors aren't driving; they collect fares. A bus position that's 30–60ms old is **invisible** to
  them once interpolated.

**Conductors send INTENTS, not state.** A conductor doesn't move the authoritative passenger — it sends
"I want to shove passenger 7" / "I collected fare from passenger 3". The **driver's client** applies it to
the authoritative `BusPassengers` sim and **broadcasts the result**, so all 3 clients converge on the same
cabin state. This reuses the existing `BusPassengers` / `SplineStopSpawner` unchanged — they just run on
the driver and replicate outcomes.

**The road is NOT synced.** It's seed-deterministic (`TiledRoadStreamer._autoSeed`): the host picks the
seed, sends it once at room start, and all 3 clients generate the **identical** road locally. Zero road
traffic, perfectly consistent. (This is why the seed-determinism work matters here.)

**Not server-authoritative.** The server never simulates the game. Server-auth would add latency + require
writing bus physics in Java — slow to build and slower at runtime. Client-auth (driver owns the bus) is
both easier and faster. Acceptable for a co-op-friendly game; revisit only if anti-cheat becomes a hard
requirement.

---

## Transport

- **Raw WebSocket** via Spring Boot's `WebSocketHandler` (NOT STOMP — STOMP is pub/sub framing overhead
  built for chat, wrong tool for game ticks). One socket endpoint: `/ws/session/{roomId}`.
- **REST** (Spring MVC) for everything non-realtime: auth, save shift result, fetch leaderboard.
- **Unity client:** `NativeWebSocket` (WebGL-compatible — wraps the browser's native WebSocket). Unity NGO
  is deliberately NOT used: on WebGL it's forced onto WebSocket anyway (losing its UDP edge), and it
  doesn't run on Spring Boot, so it can't satisfy the requirement. On WebGL, this relay's latency is
  **identical** to what NGO would achieve.

---

## Wire format — BINARY, quantized (the biggest efficiency lever)

All hot-path messages are packed bytes, not JSON. First byte = message type; the rest is a tight payload.
Little-endian. Rare/meta messages (join, role assign) may use a small JSON or binary blob — clarity over
bytes where frequency is low.

**Quantization** (lossy but imperceptible, shrinks payloads hugely):
- Position: relative to a room origin, quantized to cm — `short` per axis (±327 m range) or `int` if
  larger. Y rarely changes (road at ground) → can send less often.
- Yaw (heading): the only meaningful rotation → 1 `ushort` = 0..65535 mapped to 0..360° (≈0.005° step).
- Speed: `short` (cm/s) or a quantized `byte`/`ushort` if range allows.

### Message types

| Id | Name | Dir | Freq | Payload (after 1-byte id) | Bytes |
|----|------|-----|------|---------------------------|-------|
| 0x01 | `BusState` | driver → room | ~20–30Hz adaptive | posX,posZ (short cm) · posY (short, optional) · yaw (ushort) · speed (short) | ~10–12 |
| 0x02 | `ConductorPose` | conductor → room | ~10–15Hz | playerSlot (byte) · local cabin pos (2×short) | ~6 |
| 0x03 | `Intent_Shove` | conductor → driver | on action | passengerId (ushort) | 3 |
| 0x04 | `Intent_Fare` | conductor → driver | on action | passengerId (ushort) · haggleResult (byte) | 4 |
| 0x05 | `PassengerBoard` | driver → room | on event | passengerId (ushort) · cabinSpotIdx (byte) · isSeat (byte) | 5 |
| 0x06 | `PassengerLeave` | driver → room | on event | passengerId (ushort) | 3 |
| 0x07 | `FareConfirmed` | driver → room | on event | playerSlot (byte) · amount (ushort) | 4 |
| 0x10 | `RoomInit` | server → client | once | seed (int) · yourSlot (byte) · role (byte) · playerCount (byte) | ~7 |
| 0x11 | `PlayerJoin` / `0x12 PlayerLeave` | server → room | lifecycle | slot (byte) · role (byte) | 3 |

> Add types as gameplay grows, but keep the hot path (0x01–0x02) tiny. A `BusState` at ~12 bytes × 25Hz =
> ~300 B/s per driver — trivial.

---

## Tick & smoothing (adaptive 20–30Hz)

- **Driver broadcasts `BusState` at 20–30 Hz**, and **skips a send** when the bus has barely moved since
  the last (delta threshold on pos+yaw). Sending every frame (60Hz) is wasted bytes — after interpolation
  the eye can't tell. Adaptive = max efficiency, same smoothness.
- **Conductors render the remote bus by INTERPOLATION**, not snapping: keep the last two `BusState`s and
  lerp between them, rendered ~`interpDelay` (≈50–100ms) behind real time. This deliberate small buffer is
  what turns stepwise 25Hz updates into perfectly smooth motion. The delay is imperceptible; the smoothness
  is not. (Mirrors the interpolation lessons from the road/floating-origin work.)
- **The driver does NOT interpolate its own bus** — it's local and instant.
- **Conductor poses** interpolate the same way for the other two players' avatars.

---

## Spring Boot server shape (relay + REST)

**WebSocket relay** — minimal, simulates nothing:
```
@Component
class SessionSocketHandler extends BinaryWebSocketHandler {
  // roomId -> Set<WebSocketSession> ; slot/role per session
  afterConnectionEstablished: parse roomId from path, add to room, assign slot/role,
                              send RoomInit (seed, slot, role), broadcast PlayerJoin
  handleBinaryMessage:        forward the raw bytes to the OTHER sessions in the same room
                              (the server does not decode game payloads — pure relay)
  afterConnectionClosed:      remove from room, broadcast PlayerLeave; GC empty rooms
}
```
- The **seed** for a room is generated server-side (or by the first/driver client) and sent in `RoomInit`
  so all clients build the identical road.
- Relay = forward bytes within a room. Server-side per-message cost is sub-millisecond.

**REST (Spring MVC + JPA/DB):**
```
POST /api/auth/login                  → token
POST /api/shift/result                → { playerId, earnings, busHealth, durationSec } → persist
GET  /api/leaderboard?scope=global    → top company standings
POST /api/room            (optional)  → create room, returns roomId (matchmaking)
GET  /api/room/{id}       (optional)  → room status (player count) for a lobby browser
```

---

## Unity client plan (WebGL)

- **`NetClient`** (new) — owns the `NativeWebSocket` connection, encodes/decodes the binary messages,
  exposes events (`OnBusState`, `OnIntentShove`, …). One place for all serialization.
- **Role split** (ties into existing `RoleController`):
  - **Driver client:** runs `BusController` + `BusPassengers` + `SplineStopSpawner` authoritatively;
    broadcasts `BusState` (adaptive tick) + passenger events; receives conductor intents and applies them.
  - **Conductor client:** does NOT run bus physics; drives a **remote bus proxy** from interpolated
    `BusState`; runs only its own conductor logic; sends intents/poses.
- **Road:** every client runs `TiledRoadStreamer` with the room seed → identical road, no sync.
- **Single-player / offline:** `NetClient` disabled → everything runs locally as it does today (the bus
  auto-drives unmanned, per the existing design). The net layer is additive, not a rewrite.

---

## Efficiency checklist (the "squeeze out the most" rules)

- [ ] Raw WebSocket, **no STOMP**.
- [ ] **Binary, quantized** payloads (see table) — never JSON on the hot path.
- [ ] **Adaptive 20–30Hz** driver tick with a delta-skip; never per-frame.
- [ ] **Interpolation** on all remote-rendered transforms (bus proxy, conductor avatars), ~50–100ms buffer.
- [ ] **Driver-authoritative** — no server simulation, no rollback.
- [ ] **Seed-deterministic road** — zero road traffic.
- [ ] **Relay-only server** — forwards bytes, decodes nothing on the hot path.
- [ ] **Co-locate the server** with players for low RTT (see hosting).
- [ ] Coalesce/avoid redundant sends (don't broadcast unchanged state).

---

## Hosting

- **Now (development):** run Spring Boot on **localhost / LAN**. Effectively zero latency — ideal for
  building and testing the protocol. Unity connects to `ws://localhost:PORT/ws/session/{id}`.
- **Production:** host in the region **closest to players** (e.g. Singapore/Mumbai for South Asia) — the
  single biggest real-world latency factor; keeps RTT ~30–50ms. Use `wss://` (TLS) for a WebGL page served
  over HTTPS, and enable **CORS** for the REST API (a WebGL build is a web page; cross-origin rules apply).

---

## What this does NOT need (don't over-build)

- No client-side prediction / rollback (driver-auth removes the need).
- No server-authoritative physics (would add latency + Java-side simulation).
- No road/world sync (seed-deterministic).
- No NGO/Mirror (doesn't fit the Spring Boot requirement; no perf gain on WebGL).

---

## Build order (when you reach netcode — after Phase C)

1. Spring Boot: `SessionSocketHandler` relay + `RoomInit`/join/leave. Test with two browser tabs echoing.
2. Unity `NetClient` (NativeWebSocket) + binary codec + the message table.
3. Driver authority: broadcast `BusState` (adaptive), conductor interpolated bus proxy.
4. Conductor intents → driver applies → passenger events broadcast.
5. REST: shift result + leaderboard (pairs with the existing `ShiftManager`).
6. Matchmaking/lobby (optional): `POST /api/room`, room browser.

Related: `ROAD_SYSTEM.md` (seed-deterministic road), the `ShiftManager` (earnings/standings the leaderboard
mirrors), `RoleController` (driver/conductor split this builds on).
```
