# Baame Plastic — CSE Project Show Demo Video Script

> **Project:** Baame Plastic — a 3-player co-op Dhaka bus-sim (Unity client + Spring Boot/PostgreSQL backend).
> **Course:** CSE 2118 — Advanced Object-Oriented Programming Laboratory.
> **Format:** You screen-record the marked segments and narrate over them with your own voice. This script gives
> you the **shot list, on-screen action, and word-for-word narration** for each part.
> **Target length:** ~4–5 minutes (judges watch many videos — keep it tight and punchy).

---

## Before you record — checklist
- Backend running: `Backend/backend/run.cmd` (port 8090). Confirm the **Control Hub dashboard** is green.
- Have the **Dashboard/** website open in a browser tab (the live monitoring page).
- Have **3 game instances** ready for the co-op part (or 1 + describe — see Part 2 notes).
- Run once if not already: the vehicle/character/road/bus-stop setup menus so the world looks finished.
- Pick a quiet room; record at 1080p, 30/60fps. Game audio low under your voice.

---

## STRUCTURE (timestamps are targets)

| # | Segment | ~Time | What it proves |
|---|---------|-------|----------------|
| 0 | Hook + title | 0:00–0:20 | What it is, in one breath |
| 1 | The co-op concept | 0:20–0:45 | The 3-role design (OOP roles) |
| 2 | **3-player co-op LIVE** | 0:45–1:45 | Real multiplayer, driver-authoritative sync |
| 3 | **Procedural generation** | 1:45–2:45 | Endless world, traffic, crowds, no two runs alike |
| 4 | **Backend (heavy)** | 2:45–4:00 | Accounts, economy, leaderboard, live dashboard, DB |
| 5 | Polish + tech wrap | 4:00–4:40 | Sprites, audio, settings, web build |
| 6 | Close | 4:40–5:00 | One-line impact + team/links |

---

## PART 0 — HOOK + TITLE (0:00–0:20)

**ON SCREEN:** Main menu — the "BAAME PLASTIC" title, bus parked, traffic flowing behind, sun at sunrise.
Slowly pan / let the menu intro play.

**NARRATION:**
> "This is **Baame Plastic** — a three-player co-operative bus simulator set on the chaotic streets of Dhaka.
> One player drives, two work as bus conductors, and together you run a single shift, share one money pot, and
> survive the traffic. It's built in Unity with a full Spring Boot and PostgreSQL backend — and almost
> everything you'll see is generated procedurally, in real time."

---

## PART 1 — THE CO-OP CONCEPT (0:20–0:45)

**ON SCREEN:** The **lobby / crew-pick** screen — show the 3 crew characters in the lineup; click between roles
to show Driver, Conductor 1, Conductor 2 can each be claimed.

**NARRATION:**
> "The game is designed around three distinct roles — and in object-oriented terms, each is its own class with
> its own behaviour. The **Driver** controls the bus. **Conductor One** stands at the door, runs out to pull
> passengers aboard. **Conductor Two** works the aisle inside, collecting fares. The bus never stops moving —
> so the three of you have to co-ordinate under pressure."

*(Tip: while saying "each is its own class," it reads as genuine OOP — BusController, Conductor, InsideConductor
are separate classes with a shared RoleController. You can say that explicitly if you want the OOP emphasis.)*

---

## PART 2 — 3-PLAYER CO-OP, LIVE (0:45–1:45)  ← **showcase #1**

**ON SCREEN (ideal):** Split-screen or picture-in-picture of **three game windows** in one room/lobby:
1. Create a room on the **Driver** window (show the room code).
2. Join from the **Conductor 1** and **Conductor 2** windows using that code.
3. All ready → Driver starts the shift.
4. Show all three playing the SAME shift: the driver drives, you see the bus move identically on all screens;
   Conductor 1 runs out and grabs a passenger; Conductor 2 collects a fare inside; the shared money goes up on
   every screen.

**NARRATION:**
> "Here's the co-op in action across three separate clients. The driver hosts a room, the two conductors join
> by code. Once the shift starts, they're all in the **same world** — and this is the hard part: the driver's
> client is **authoritative** for the bus and the passengers. The conductors run a smooth proxy of the bus,
> send their actions as **intents** over a WebSocket, and the driver applies them and broadcasts the result.
> So when Conductor Two collects a fare here, the earnings update on **all three screens** at once — no
> desync, no cheating."
>
> *(As you collect a fare:)* "Fares grow the longer a passenger rides — a tiered system — and the conductor has
> to physically reach them and collect before they get off. Nothing is automatic."
>
> *(Optional, if you trigger it:)* "If the driver disconnects mid-shift, the game **fails over** — Conductor Two
> automatically becomes the new driver, and the shift carries on."

**IF YOU CAN'T RUN 3 WINDOWS:** record one client + the lobby, and narrate the architecture over it. Say:
> "The full three-player sync runs over the same WebSocket relay; here I'm showing one client, but the driver
> -authoritative model means every conductor sees the identical, server-consistent shift."

*(Real systems to name-drop if asked: GameNet binary protocol, intent→authority→result, seed-deterministic
world so all clients generate the identical map.)*

---

## PART 3 — PROCEDURAL GENERATION (1:45–2:45)  ← **showcase #2**

**ON SCREEN:** Solo drive. Drive forward for a while so the judge SEES the world building ahead:
- The **road** streaming endlessly (pooled tiles), curving, with real asphalt texture + lane markings.
- **City buildings** lining both sides, appearing out of the smog — never popping in.
- **Traffic**: rickshaws, CNGs, cars, buses, trucks weaving chaotically; oncoming lane busy; a rival bus
  cutting you off and honking.
- **Crowds** of pedestrians on the footpath; passengers waiting at a **bus stop**.
- The **day/night cycle** if you can speed through a shift (sunrise → day → dusk).

**NARRATION:**
> "Now the world. None of this is a pre-built level — it's **generated as you drive**. The road is an endless
> chain of pooled tiles that streams around the bus with a floating-origin system, so you can drive forever
> without precision loss. It curves, it has corners and U-turns, all from a seeded procedural walk."
>
> *(as buildings stream in:)* "Buildings line both sides on a serial wall, fitted to the road's curve. Bus
> stops, waiting crowds, and footpath pedestrians all stream in too — and because the world is **seeded**, all
> three players in a co-op game generate the **exact same map** independently. No map data is sent over the
> network — just the seed."
>
> *(as traffic swarms:)* "The traffic is modelled on real Dhaka chaos — rickshaws, CNGs, cars, buses and trucks,
> each with different mass and behaviour. They weave, cut across lanes, and the **rival buses** actively
> compete with you — racing to the stops to steal your passengers, blocking your lane, honking. Every shift is
> different because every system is procedural and seeded."

*(Real systems: TiledRoadStreamer + floating origin, BuildingSpawner marching the road centreline, TrafficSystem
with kind-based agents + RivalBrain, SplineStopSpawner + FootpathPedestrians, DayNightController.)*

---

## PART 4 — THE BACKEND (2:45–4:00)  ← **showcase #3 (heavy)**

This is where you prove it's a real full-stack system, not just a game. Spend the most time here.

### 4a — Accounts + economy (in-game)
**ON SCREEN:** From the menu: **Sign up** a new account (username/email/password) → it logs in. Show the
**Bhara currency** counter top-right. Open **SHOP & CUSTOMIZE** → buy a Bhara pack, buy a bus colour, **equip**
it → cut to the bus showing the new colour.

**NARRATION:**
> "Everything a player does is backed by a real database. Here I'm creating an account — that hits a Spring
> Boot REST endpoint, the password is BCrypt-hashed, and the profile is stored in PostgreSQL. The in-game
> currency, **Bhara**, the bus colours and upgrades I buy, what I have equipped — all of it is persisted
> server-side. I buy this bus skin, equip it, and it's saved to my account and applied in the game."

### 4b — Leaderboard
**ON SCREEN:** Open the **LEADERBOARD** screen — the global career standings.

**NARRATION:**
> "At the end of every shift, the earnings are posted to the backend and converted to currency, and the
> **global leaderboard** ranks every player by lifetime earnings — pulled live from the database."

### 4c — The live monitoring dashboard (the showpiece)
**ON SCREEN:** Switch to the browser — the **Control Hub dashboard** website. Show:
- Server health + uptime, **PostgreSQL connection** status (green).
- **Live rooms / lobbies** with their slots and players.
- The **realtime message feed** (WebSocket traffic counters ticking).
- The **database browser** — tables (players, shift_results, etc.) with real row counts + latest rows.
- *(Powerful move:)* do a quick action in the game (buy something / finish a shift) and **show the row appear /
  the counter tick on the dashboard live.**

**NARRATION:**
> "And to prove it's all real, this is our **Control Hub** — a separate monitoring dashboard reading a
> read-only admin API on the backend. It shows live server health, the PostgreSQL connection, every active
> lobby with its players, a real-time feed of the WebSocket messages, and a browser of every database table.
> Watch — when I do this in the game *(do an action)*, the data shows up here, live. The backend handles the
> realtime lobby and game relay over WebSockets, and the persistent data — accounts, economy, leaderboard —
> over REST, against a PostgreSQL database."

*(Real systems: AccountController /api/auth/* (signup/login/store/leaderboard/daily-bonus), ShiftController
leaderboard, AdminController read-only observability, SessionSocketHandler WebSocket relay, Player/ShiftResult
entities, the Dashboard/ site.)*

---

## PART 5 — POLISH + TECH WRAP (4:00–4:40)

**ON SCREEN:** Quick montage (2–3 seconds each):
- The hand-drawn **character sprites** walking (pedestrians flipping by direction, conductors animating).
- The **pixel-art UI** (menu, shop, settings).
- The **settings menu** — graphics options, the conductor-mic toggle, auto-conductors toggle.
- *(Optional flex:)* the conductor **microphone** mechanic — shout to call passengers / boost fares, with the
  on-screen meter.

**NARRATION:**
> "On top of the systems, it's a finished, playable game — animated 2.5D characters, a full pixel-art interface,
> a settings menu, day-night atmosphere, and procedural and recorded audio. There's even an experimental
> **microphone mechanic** — conductors can literally shout to call passengers aboard. The whole thing is
> optimised and exports to **WebGL**, so it runs in a browser."

---

## PART 6 — CLOSE (4:40–5:00)

**ON SCREEN:** Back to a clean gameplay shot or the title. Put your **team name + GitHub link** on screen as text.

**NARRATION:**
> "Baame Plastic — three players, one bus, one chaotic shift through Dhaka, backed by a real full-stack system.
> Thanks for watching."

**ON-SCREEN TEXT (last frame, hold 4–5s):**
```
BAAME PLASTIC
3-Player Co-op Dhaka Bus Sim  ·  Unity + Spring Boot + PostgreSQL
Team: [your team name]
GitHub: [your repo link]
CSE 2118 — Advanced OOP Laboratory  ·  Spring 2026
```

---

## RECORDING ORDER (easiest workflow)
You don't have to record in script order. Suggested capture order:
1. **Backend first** — start it, record the dashboard + sign-up/shop/leaderboard while the DB is fresh and clean.
2. **Solo drive** for the procedural-generation + polish footage (longest, most B-roll — drive 1–2 min, you'll
   trim).
3. **3-window co-op** last (most setup). If it's flaky, capture the lobby + one client and narrate the rest.
4. Record **narration separately** after editing the visuals (easier to pace), or talk over a rough cut.

## EDITING NOTES
- Lead with motion — open on the bus driving, not a static menu, if the hook feels slow.
- For the backend part, **side-by-side game + dashboard** is the single most convincing shot — judges love
  seeing data update live.
- Keep each system to its punchline; don't explain code. Name the tech (WebSocket, PostgreSQL, Spring Boot,
  procedural, driver-authoritative) — judges scan for those terms.
- Captions/labels on screen ("PROCEDURAL ROAD", "LIVE DATABASE", "3-PLAYER SYNC") help if your audio is quiet.
- Music: low, under the voice. The game's own ambience works.

## ONE-LINE PITCH (if you need a 30-second cut or a verbal intro)
> "Baame Plastic is a three-player co-op Dhaka bus sim where one drives and two conduct — built in Unity with a
> Spring Boot and PostgreSQL backend, an endlessly procedurally-generated city, real-time multiplayer, a
> persistent economy and leaderboard, and a live monitoring dashboard."

## SUPPORTING MATERIALS TO SUBMIT (per the email)
- **GitHub repository link** (push the latest before the deadline).
- **This video.**
- **Documentation**: `PROJECT_UNDERSTANDING.md` + the per-system docs (ROAD_SYSTEM, TRAFFIC_SYSTEM, NETWORKING,
  BACKEND, DATABASE, MENU_SYSTEM, DEPLOY) are all in the repo root — link them.
- If asked for a presentation file, the table in this script's STRUCTURE section maps cleanly to slides.
- ⚠️ Deadline: **Wednesday, June 10, 2026, 11:59 PM**. Submit early — late = automatic elimination from the show.
