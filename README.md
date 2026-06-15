# Bame Plastic — Run Multiplayer (the simple, failsafe path)

A co-op 3-player Dhaka bus-sim. Unity (WebGL) client + Spring Boot + database backend.
**This guide gets multiplayer running entirely from VS Code with zero hassle.**

> One laptop is the **HOST** (runs the backend, which also serves the game). Everyone — including the host —
> plays in a **browser**. They just open one URL. No Postgres install, no Docker required.

---

## 0 · One-time setup in VS Code (≈3 min)

1. Install **VS Code**, then these extensions (Extensions panel `Ctrl+Shift+X` → search → Install):

   | Extension | ID | Why |
   |---|---|---|
   | **Extension Pack for Java** | `vscjava.vscode-java-pack` | Bundles the JDK + Java runtime — **this is all you need to run the backend** (no separate JDK/JAVA_HOME). |
   | **Spring Boot Extension Pack** | `vmware.vscode-boot-dev-pack` | Run/stop the Spring Boot server with one click. |

   *(That's it. The Java pack ships its own JDK, so even a fresh laptop can run the backend.)*

2. Open this folder in VS Code: **File ▸ Open Folder…** → select `Bame Plastic`.

---

## 1 · Start the backend (one click or one command)

The backend runs with a built-in **file database (H2)** by default — **no PostgreSQL to install.**

**Easiest — double-click the launcher:** open `Backend/backend/` and double-click **`run-jar.cmd`**.
It finds the Java from the VS Code extension automatically and starts the server on **http://localhost:8090**.

**Or from the VS Code terminal** (Terminal ▸ New Terminal):
```
cd Backend/backend
./run-jar.cmd        # Windows
```

You're ready when the terminal shows **`Started BackendApplication`** and **`Tomcat started on port 8090`**.

> Want the real PostgreSQL instead of the built-in H2? Set `USE_PG=1` before running and have a local Postgres
> with a `bame_plastic_db` database (see `Backend/backend/db/README.md`). Not needed for a demo.

---

## 2 · Put the game where the backend serves it

After you build the game for **WebGL** in Unity, copy the build output (the folder containing `index.html`,
`Build/`, `TemplateData/`) into **`Backend/backend/webgl/`** (create the `webgl` folder if missing).
The backend then serves the game at the root URL. *(If you only have the Unity editor, you can also just press
Play in the editor — but for 3 laptops, the WebGL build is the way.)*

---

## 3 · Find the host laptop's address

In the VS Code terminal on the host:
```
ipconfig          # Windows — look for "IPv4 Address", e.g. 192.168.0.42
```
All laptops must be on the **same Wi‑Fi**. (Phone hotspot works great. Avoid eduroam/office Wi‑Fi — it often
blocks laptop-to-laptop traffic.)

---

## 4 · Everyone plays (in a browser)

On **each** laptop (including the host) open a browser to:
```
http://<host-ip>:8090/          (on the host itself, http://localhost:8090/ also works)
```
1. **Sign Up** / **Log In** (or **Play as Guest**).
2. **Play Online** → one person **Hosts** a room → the others **Join by code** (shown on screen).
3. Pick roles (Driver + 2 Conductors), everyone **Ready**, the driver presses **START**.

> Use **http://**, not https — a WebGL build over https can't reach the `ws://` backend. Same-origin http just works.

---

## 5 · Watch it live (optional) — the Control Hub dashboard

Open **`Dashboard/index.html`** in a browser. It shows **real** server/DB health, every live lobby, game
analytics (players, shifts, achievements, payments), and a **live WebSocket feed** of everything happening. It
turns green when the backend is up. (To point it at a non-localhost backend, edit `Dashboard/config.js`.)

---

## Troubleshooting (the usual suspects)

| Problem | Fix |
|---|---|
| Browser shows nothing at `:8090/` | The WebGL build isn't in `Backend/backend/webgl/` (with `index.html` directly inside). |
| `run-jar.cmd` says "No Java found" | Install the **Extension Pack for Java** in VS Code (it bundles the JDK), then re-run. |
| Game loads but can't create/join a room | Laptops aren't on the same Wi‑Fi, or the host **firewall** blocks port 8090. Allow it (below). |
| "port 8090 in use" | Another app uses it (e.g. pgAdmin). Close it, or set `PORT` and use that port in the URL. |
| Reset all accounts/data | Delete `Backend/backend/bame_demo_db.mv.db` and restart. |

**Allow the port through the host firewall (run once in an admin terminal):**
```
netsh advfirewall firewall add rule name="BamePlastic 8090" dir=in action=allow protocol=TCP localport=8090
```

---

## More detail

- Full demo-day walkthrough: `Backend/backend/DEMO_SETUP.md`
- Backend / protocol / database: `docs/BACKEND.md`, `docs/NETWORKING.md`, `docs/DATABASE.md`
- The whole project explained: `docs/PROJECT_UNDERSTANDING.md`
