# Backend Server — Bame Plastic

Simple explanation of what the server does and how it's built.

---

## What it is

A **Spring Boot** server (Java). It's the middle-man that lets **3 players share one bus** over the internet,
and it talks to the database. It lives in **`Backend/backend`**.

Its job is small on purpose: it **passes messages between players** and **saves results**. It does **not**
run the game — each player's own computer runs the game. This keeps the server cheap, fast, and simple.

---

## The two things it does

### 1. Multiplayer (real-time, over a WebSocket)
A **WebSocket** is a live two-way phone line between a player and the server. The server's address for this is:
```
ws://localhost:8090/ws/session
```
Players connect here to make/join rooms and play together. The server acts as a **relay** — when one player
sends something, it forwards it to the other players in the same room. It doesn't open or understand the
game data; it just passes it along (which is why it's fast).

**The lobby** (make a room, join by code, pick a role, ready up, start) works over this same connection using
simple text messages. **The actual gameplay** (bus position, etc.) will use tiny fast binary messages on the
same line.

> **Why one player is "in charge":** the **driver's** computer is the authority for the bus. Everyone else
> sees the driver's bus smoothly. This means there's no lag for the driver and no complicated conflict-solving
> on the server.

### 2. Saving data & the leaderboard (normal web requests, REST)
For things that aren't real-time, the server has simple web addresses (REST API):
```
POST /api/shift/result      → save a finished shift's score
GET  /api/leaderboard       → get the top scores
GET  /api/admin/...         → read-only info for the dashboard
GET  /api/auth/achievements/{id} → a player's achievements (16 in AchievementCatalog)
POST /api/pay/initiate      → start an SSLCommerz (sandbox) Bhara-pack payment
POST /api/pay/success|fail|cancel|ipn → SSLCommerz callbacks (credit Bhara on success)
GET  /api/pay/status        → client polls a payment's PENDING/COMPLETED/FAILED state
```

#### Payments (SSLCommerz sandbox — FREE, showcase only)
Clicking a Bhara pack price opens SSLCommerz's hosted payment page in the browser (test card, no real charge).
The flow: client `POST /api/pay/initiate` → backend opens a gateway session (`PaymentController`) → returns the
`GatewayPageURL` → client `Application.OpenURL` + polls `/api/pay/status` → on the success/IPN callback the
backend credits the pack's Bhara and marks the transaction COMPLETED. Config in `application.properties`
(`sslcommerz.*`): the shared sandbox store `testbox`/`qwerty` works out of the box; set `sslcommerz.callback-base`
to a browser-reachable URL of this backend for LAN/cloud demos. New tables auto-create via `ddl-auto=update`
(`payment_transactions`, `player_achievements`) + the `equipped_upgrades` column on `players`.

---

## How it's organized (the folders)

Inside `Backend/backend/src/main/java/com/BamePlastic/backend/`:

| Folder | What's inside | In plain words |
|---|---|---|
| `realtime` | `SessionSocketHandler`, `Room`, `RoomManager`, `ServerStats` | The multiplayer brain — rooms, players, message relay |
| `controller` | `ShiftController`, `AdminController`, `TestController` | The web addresses (REST endpoints) |
| `entity` | `Player`, `Bus`, `Route`, `ShiftResult`… | Java versions of the database tables |
| `repository` | `PlayerRepository`, `ShiftResultRepository`… | The code that reads/writes those tables |
| `config` | `WebSocketConfig`, `SecurityConfig` | Settings — turns on the WebSocket, opens security for dev |

### How a room works (the core)
- `Room` = one game room with **3 slots**: Driver, Conductor 1, Conductor 2.
- `RoomManager` = the list of all rooms; makes a room code + a random "seed".
- The **seed** is a number sent to all 3 players so they all build the **exact same city** locally — so the
  road/traffic never has to be sent over the network. Clever and cheap.
- `SessionSocketHandler` = handles every message: someone joins → seat them; they ready up → tell everyone;
  the host starts → send the seed and everyone loads the game.

---

## How to run it

This machine has **no separate Java installed**, so we use the Java that came with the VS Code Java extension.
A ready-made script handles that:

```
# from a terminal in Backend\backend
.\run.cmd          (or  .\run.ps1  in PowerShell)
```

It starts the server on **http://localhost:8090**. You'll see `Tomcat started on port 8090` and
`Started BackendApplication` when it's ready. Press **Ctrl+C** to stop.

> **Port note:** we use **8090**, not the usual 8080, because pgAdmin/EnterpriseDB already uses 8080 on this PC.

---

## How the pieces connect

```
   Game (Unity, each player)  ─────ws://…8090/ws/session─────►  Spring Boot server
            ▲   │                                                   │        │
            │   └── builds the same city from the seed              │        ▼
            └────────── gets other players' messages ◄──── relay ───┘   PostgreSQL (saves results)

   Dashboard (web page)  ────http://…8090/api/admin/────►  Spring Boot server  (read-only monitoring)
```

---

## Key points to explain
- The server is a **relay + a save-game**, not the game itself → simple, fast, cheap.
- **One WebSocket** does the lobby (text) and the live game (binary).
- The **driver is the authority** → no lag for them, no hard server logic.
- A shared **seed** means the world is identical for everyone without sending it.
- Built with the standard Spring Boot pieces (web, websocket, JPA for the database).

Protocol / message types: `NETWORKING.md`. Running it for a demo: `Backend/backend/DEMO_SETUP.md`.
