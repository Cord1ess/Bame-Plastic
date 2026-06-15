# Hosting the backend on your laptop, public to anyone

Goal: **run the backend on one laptop, keep it on, and any client anywhere can connect** (create lobbies, see
each other). The backend code is already public-ready (WebSocket origins `*`, CORS open, binds `0.0.0.0`) — the
only missing piece is making the laptop's port reachable from the internet. A **tunnel** does that with zero
router config and gives you a public `wss://` URL (which a WebGL build needs).

---

## The model

```
   Laptop                                  Internet
   ┌─────────────────────┐                 ┌──────────────────────────────┐
   │ Spring Boot :8090   │◄── tunnel ──────│  https://xxxx.trycloudflare…  │◄── Player A (anywhere)
   │ Postgres            │                 │  (public wss:// URL)          │◄── Player B (anywhere)
   └─────────────────────┘                 └──────────────────────────────┘◄── Player C
```
The laptop just runs the backend + the tunnel. Clients pick that public URL in the in-game **Server picker**.

---

## Option A — Cloudflare Tunnel (recommended: free, stable, HTTPS/wss out of the box)

### 1. Start the backend (as usual)
```
cd Backend\backend
run.cmd                 (serves on http://localhost:8090)
```

### 2. Install cloudflared (one time)
- Download `cloudflared.exe` (Windows) from Cloudflare, or `winget install --id Cloudflare.cloudflared`.

### 3. Run the tunnel
```
cloudflared tunnel --url http://localhost:8090
```
It prints a public URL like `https://random-words-1234.trycloudflare.com`. That URL:
- serves your REST (`https://…/api/…`) **and** the WebSocket as `wss://…/ws/session` (Cloudflare upgrades it).
- is HTTPS/`wss://` → works for a WebGL build served over HTTPS.

> For a **permanent named URL** (doesn't change on restart), create a free Cloudflare account + a Named Tunnel
> (`cloudflared tunnel login` → `tunnel create bame` → route a hostname). The quick `--url` form above is
> instant but the address changes each run.

### 4. Players connect via the in-game Server picker
- Play Online → **SERVER** → in the host field paste the **wss URL**:
  `wss://random-words-1234.trycloudflare.com/ws/session`
  (the picker accepts a full `ws://`/`wss://` paste — it keeps it as-is). → **Connect**.
- Now anyone, anywhere, sees the same lobbies.

---

## Option B — ngrok (also fine; free URL rotates)
```
ngrok http 8090
```
Gives `https://xxxx.ngrok-free.app`. Use `wss://xxxx.ngrok-free.app/ws/session` in the picker. Free tier's URL
changes each restart; a paid plan or a reserved domain fixes it.

---

## Option C — port-forward + dynamic DNS (free, no third party, but fiddly)
Only if you don't want a tunnel:
1. Router: forward external **TCP 8090 → laptop's LAN IP : 8090**.
2. Give the laptop a static LAN IP (DHCP reservation) so the forward doesn't break.
3. Your home public IP changes → use a **Dynamic DNS** name (No-IP/DuckDNS) pointing at it.
4. Players connect to `ws://yourname.duckdns.org:8090/ws/session`.
- ⚠️ Caveats: exposes your home network; many ISPs use **CGNAT** (no inbound possible — then you MUST use a
  tunnel); and **WebGL needs `wss://` (TLS)** which raw port-forwarding doesn't give — you'd need a reverse
  proxy with a cert. For WebGL, a tunnel is much simpler.

---

## Notes
- **Postgres stays local** to the laptop (the tunnel only exposes 8090, not the DB — good for security).
- **The laptop must stay awake** (disable sleep) and the backend + tunnel running. That's the whole job.
- **Capacity**: one laptop easily handles a course-project's worth of rooms (each room is ~tiny traffic).
- **Security for real public exposure**: right now security is dev-open (no login). Fine for a demo / friends.
  Before truly public/long-term, add the auth from the schema (`users` + tokens) and tighten CORS to the game's
  origin. Captured in `DATABASE.md` / `BACKEND.md`.
- **Editor/standalone clients** can still use plain `ws://…:8090` on the same LAN; only **WebGL** strictly needs
  the `wss://` tunnel URL.
