# Dashboard (Control Hub) — Bame Plastic

Simple explanation of what the dashboard shows and **how** it gets its data.

---

## What it is

A small **website** (just HTML/CSS/JavaScript) that shows, at a glance, everything happening in the system:
is the server up? is the database connected? what rooms exist? what's being sent? what's in the database?

It lives in the **`Dashboard/`** folder. You open it by **double-clicking `Dashboard/index.html`** — no install,
no build. It's styled to look exactly like the in-game menus (same colors, same pixel font) so it feels part
of the game.

It is **view-only** — it only *shows* things, it never changes anything.

---

## How it gets its data (the important part)

The dashboard does **not** make up any numbers. It asks the backend server for real data, **every 2 seconds**,
and shows whatever comes back.

It works like this:

```
Dashboard  ──(every 2s)── asks ──►  Backend server (http://localhost:8090/api/admin/...)
           ◄── real data ──────────  reads from RAM (rooms) + PostgreSQL (tables)
```

So the chain is: **Dashboard → Backend → (live memory + database) → back to Dashboard → on screen.**

The backend has special **read-only** addresses just for the dashboard:

| The dashboard asks… | …and gets back |
|---|---|
| `GET /api/admin/health` | Is the server up? uptime, is the DB connected, live counts |
| `GET /api/admin/rooms` | Every live room, with all 3 player slots |
| `GET /api/admin/feed` | The latest things that happened on the server |
| `GET /api/admin/db` | Every database table's row count + newest rows |
| `GET /api/leaderboard` | The top scores |

If the server is **off**, the dashboard can't reach those addresses, so it shows a **red "BACKEND OFFLINE"**
banner instead of fake data.

---

## What each tab shows

| Tab | What you see |
|---|---|
| **Overview** | Green/red lights for server + database, uptime, how many players/rooms are live, message totals, recent activity |
| **Lobbies** | Every room right now — its code, who's the host, each role slot (player / AI / ready / connected), and whether it's in a game |
| **Live Feed** | A running list of everything sent through the server (joins, role swaps, ready, starts…) as it happens |
| **Database** | Click any table to see its live count and newest rows, straight from PostgreSQL |
| **Leaderboard** | Top shift scores |
| **Setup & Help** | How to start the servers + a troubleshooting guide, built right into the page |

---

## Where the data really comes from

Two sources, combined by the backend:
- **Live memory (RAM):** rooms, players, connections, the message feed — this is the *current* state, gone if
  the server restarts. (That's why empty = nobody's playing right now.)
- **The database:** the table contents and leaderboard — this is *saved* data that survives restarts.

The dashboard doesn't know or care which is which — it just shows what the backend reports.

---

## How to use it

1. Start the backend (see `BACKEND.md` — `run.cmd`).
2. Double-click `Dashboard/index.html`.
3. The top bar turns **green** when it reaches the server.
4. Host a room in the game → watch it appear live under **Lobbies**, and the events tick by under **Live Feed**.

To point it at a different server, edit one line in **`Dashboard/config.js`** (the `BACKEND_HTTP` address).

---

## Key points to explain
- It's a plain web page that **polls the backend every 2 seconds** — no magic, no fake data.
- It reads **read-only** addresses, so it can never break anything.
- **Empty sections are real** — they mean nothing has happened yet (host a room, finish a shift).
- Same look as the game so it feels like one product.
