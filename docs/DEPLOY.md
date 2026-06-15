# Deploying the backend to the cloud (free) — so anyone can play

Goal: host the Spring Boot backend **+ Postgres** on a free cloud host so clients anywhere connect — no laptop
involved. Recommended for a course demo: **Render** (easiest free full-stack). The backend is already
container-ready (`Dockerfile`) and fully **env-var driven** (PORT, DB_URL/USER/PASS, DDL_AUTO) — no code change.

> Free-tier caveat (fine for a demo): Render's free web service **sleeps after ~15 min idle** (first connect
> after that cold-starts ~30s), and the free Postgres is time-limited. For always-on, see "Alternatives".

---

## One-time: get the code on GitHub
Render deploys from a Git repo. Push at least the `Backend/backend` folder (with `Dockerfile` + `render.yaml`).
If the repo root is the game, that's fine — point Render at the `Backend/backend` subdirectory (below).

---

## Deploy on Render (Blueprint — the easy path)

1. Create a free account at render.com → **New → Blueprint**.
2. Connect your GitHub repo. Render finds `Backend/backend/render.yaml`.
   - If it doesn't auto-find it (repo root ≠ backend), set the service **Root Directory** to `Backend/backend`.
3. Render shows the plan: a **web service** (Docker) + a **free Postgres**, with the DB env vars wired in
   automatically (see `render.yaml`). Click **Apply**.
4. First build takes a few minutes (Maven build inside Docker). When it's live you get a URL like
   `https://bame-plastic-backend.onrender.com`.
5. **First boot creates the tables**: `render.yaml` sets `DDL_AUTO=update`, so Hibernate builds the schema on
   the fresh DB. Once it's up, go to the service → **Environment** → change `DDL_AUTO` to `validate` and
   redeploy (so the schema is locked). *(Or run `Backend/backend/db/schema.sql` against the Render DB via its
   `psql` connection string, then leave `validate`.)*
6. Verify: open `https://<your-service>.onrender.com/api/admin/health` → JSON with `"db":{"connected":true}`.
   The Control Hub dashboard (point `Dashboard/config.js` at the same URL) should go green.

### Manual (without the Blueprint)
- **New → PostgreSQL** (free) → copy its **Internal/External** connection info.
- **New → Web Service** → your repo, Root Directory `Backend/backend`, Runtime **Docker**.
- Add env vars: `DB_URL=jdbc:postgresql://<host>:<port>/<db>`, `DB_USER`, `DB_PASS`, `DDL_AUTO=update` (then
  `validate`). `PORT` is injected by Render automatically.

---

## Point the GAME at it

The cloud URL is HTTPS, so the WebSocket is `wss://` (WebGL requires this):

- In-game: **Play Online → SERVER → paste** `wss://<your-service>.onrender.com/ws/session` → **Connect**.
- The server picker (`ServerConfig`) accepts a full `wss://…` and uses it as-is.
- Now any player anywhere connects to the same server → shared lobbies. Nothing local.

(Optional) bake it as a preset so players don't type it: add an entry to `ServerConfig.Presets` with the host
`wss://<your-service>.onrender.com` — then it's one click in the picker.

---

## The database, end to end
- The backend connects to the **managed cloud Postgres** via the env vars (the same `DB_URL/USER/PASS` it uses
  locally — just pointed at the cloud DB). The app code is unchanged.
- Shift results, leaderboard, (future) accounts all persist there. The Control Hub dashboard reads it live via
  `/api/admin/*` against the cloud URL.
- Rooms/live game state stay in the server's memory (the relay design); only results/leaderboard hit the DB.

---

## Alternatives (if you outgrow Render-free)
- **Always-on free (no sleep):** backend on **Fly.io** or **Koyeb** + Postgres on **Neon** (always-on, free-
  forever). Same Dockerfile; set the four `DB_*` env vars to the Neon connection string. Better for live
  sessions (no cold start, WebSockets stay up).
- **Most powerful free:** an **Oracle Cloud always-free VM** running both the backend (this Docker image) and
  Postgres — like a real server, no sleep/expiry, but you manage the Linux box + firewall + TLS.
- **Just a demo from your laptop:** see `HOSTING_LAPTOP_PUBLIC.md` (Cloudflare Tunnel) — no cloud account.

## Files
- `Backend/backend/Dockerfile` — container build (Maven → JRE).
- `Backend/backend/render.yaml` — Render blueprint (web service + free Postgres, DB auto-wired).
- `Backend/backend/.dockerignore` — keeps the image lean.
- Config is env-driven in `application.properties` (`PORT`, `DB_URL`, `DB_USER`, `DB_PASS`, `DDL_AUTO`).
