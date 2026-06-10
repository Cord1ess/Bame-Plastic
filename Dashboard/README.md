# Bame Plastic — Control Hub (Dashboard)

A standalone observability dashboard for the whole stack: it shows, with **real data**, whether the backend
and database are up, every live lobby/game session, the realtime message feed, the contents of every DB table,
and the leaderboard. Styled to match the in-game pixel-retro UI. **View-only** — it never changes anything.

## Run it

1. **Start the backend** (and Postgres) — see the SETUP & HELP tab inside the dashboard, or `BACKEND_SETUP.md`
   at the repo root. In short: have Postgres running with DB `bame_plastic_db`, then from `Backend/backend`:
   ```
   ./mvnw spring-boot:run        # Windows: .\mvnw.cmd spring-boot:run
   ```
2. **Open the dashboard**: just open `Dashboard/index.html` in any browser (double-click, or serve the folder
   with any static server). No build step.
3. The top bar turns **green** when it reaches the backend. Host a room in the game to watch a lobby appear live.

## Configure

Edit `config.js` if your backend isn't on `localhost:8080`:
```js
window.HUB_CONFIG = {
  BACKEND_HTTP: "http://localhost:8080",
  BACKEND_WS:   "ws://localhost:8080/ws/session",
  POLL_MS: 2000,
};
```

## How it gets real data

It polls the backend's read-only admin API every ~2s:
- `GET /api/admin/health` — server + DB health, uptime, realtime totals
- `GET /api/admin/rooms` — every live room (lobby) with full slot detail
- `GET /api/admin/feed` — recent realtime events ("what's being sent")
- `GET /api/admin/db` — every table's row count + latest rows
- `GET /api/leaderboard` — top shift results

These live in the backend (`AdminController` + `ServerStats`). Nothing here is faked — empty sections mean
there's genuinely nothing yet (host a room, finish a shift, etc.).

## Files
- `index.html` — structure
- `style.css` — the pixel-retro theme (mirrors the game's `PixelUI` palette + VCR font)
- `config.js` — backend URL + poll interval
- `app.js` — polling + rendering engine, offline-graceful
- `assets/VCR_OSD_MONO.ttf` — the same font the game UI uses
