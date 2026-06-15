# Bame Plastic — Project Understanding & Guide

> **Purpose:** Single source of truth for what this project *is*, what's *actually built*, and what *still needs building*. Written to onboard any developer (or future AI session) fast. §1 is design intent; everything after is implemented fact verified against the repo.
>
> **Game name:** **Bame Plastic** (the title; `productName` = "Bame Plastic"). The original brief called the *concept* "Dhaka Local" — that's the setting/genre, but the game is named **Bame Plastic**.
>
> **Last verified:** 2026-06-05, after a driving/camera/generation-performance work pass (roads fixed via ProBuilder, smooth chase camera, treadmill chunk pooling, a road-bake tool, traffic scripts written). See §10 Change Log.
>
> **Design note:** §1 was substantially **expanded on 2026-06-05** — the game is now framed as a **shift-based money competition** (out-earn rival company buses across a ~10-min day→night shift), with three refined crew roles and a passenger "fare clock." The earlier "complete a route before time runs out" framing is superseded.

---

## 1. The Vision (Design Intent)

**Bame Plastic** is a **co-operative multiplayer 2.5D simulation game** set on the chaotic streets of Dhaka, Bangladesh. A crew of up to **three players runs one local bus through a single shift**, sharing one goal: **earn the most money.**

It is also an **Advanced Object-Oriented Programming (AOOP) course project**. Intended architecture mirrors commercial multiplayer games:
- **Backend:** Java + **Spring Boot** (server-authoritative systems, OOP design).
- **Client:** **Unity (C#)** — the playable game.
- **Transport:** **HTTP REST + WebSocket** between client and backend.

### The shift = the run
A full **day→night cycle is one shift (~10 minutes of play)**. There is **no finish line on the road** — the **shift timer ending is the only "end."** Your bus is **one of many the company runs**, and the other buses are also out earning. The **UI shows live standings — what each rival bus has earned — and you compete to top the board.** Out-earning the other drivers is the win condition. (Rivals can begin as *simulated* earners and later become AI or real players.)

### How you earn (the Dhaka bus economy)
Income is **fares**, and Dhaka bus culture *is* the mechanic:
- **Pick up as many passengers as possible** and **haggle each fare upward** — fares aren't fixed; you argue for more.
- **Overtake rival buses and park in front of them to poach their waiting passengers.**
- **Keep the bus healthy** — potholes, traffic, and crashes damage it; damage drains time/money and earning ability.
- **Dodge the traffic police** — there are **no traffic lights**; the road hazard is *police*, not signals.

### The three roles (one crew, one shift, one money pot)
| Role | View / camera | Job |
|------|---------------|-----|
| **Driver** | ~30° chase cam *(built)* | Drive the whole shift. Avoid potholes & police, **overtake rival buses**, dodge vehicles to **maintain bus health**. **Stop on demand:** when *most* passengers want out a stop happens, but a *single* passenger can call to get out **anywhere** → must stop then and there. *(More such rules to come.)* |
| **Conductor 1 — the door** | Follow-cam on him; **WASD** | Hangs off the open door; **jumps in/out while the bus rolls at ~5–10 mph** (never needs a full stop — he's hyperactive). **Calls, grabs, carries, and physically throws/pushes passengers through the door** — gathering crowds as chaotic physical play. |
| **Conductor 2 — inside** | Roof **hidden / ~5–10% opacity** to see the cabin | **Pushes passengers** to make space (they take a seat when shoved). **Haggles fares** as a back-and-forth mini-game — argue the passenger up, push their limit, and extract the most taka **before they leave**. |

### Passenger lifecycle (the fare clock)
Each passenger is an object on a clock:
1. **Boards** (driver stopped, or the door conductor shoved them in).
2. **Stays a minimum ~1 minute** — the window for **Conductor 2 to reach them and haggle the fare**.
3. **Leaves** at the next stop or on a **random get-out event** — and **if the fare wasn't collected before they leave, they walk free: that income is lost.**

So the crew races the fare clock: board fast (door) → seat & haggle before the minute's up (inside) → the driver's stops decide when paid passengers leave and new ones board.

### Stops & get-outs (no traffic lights)
Stops are **demand-driven**, not signal-driven: a **majority-want-out** triggers a normal stop, and **any single passenger's random get-out** forces an immediate stop wherever the bus is.

### Cultural hook
Dhaka's bus ecosystem is informal and verbal: haggled fares, fare dodgers, poaching rivals' passengers, police on the take, potholes/waterlogging, CNGs & rickshaws, the "Boro Bhai" of the road. That specificity is the differentiator.

### Intended rendering (2.5D)
- **Environment 3D** (URP); **characters/vehicles** may be **billboard sprites** facing the camera.
- **Driver cam:** perspective ~30° forward-down *(built)*. **Door conductor:** follow cam. **Inside conductor:** roof hidden/transparent for a cabin view.
- **Parallax layers (back→front):** sky/buildings → sidewalk + crowd → far-lane vehicles → main lane w/ bus → near-lane vehicles → road-edge drains.

> ⚠️ §1 is the *target*, not the current state — and it's evolving fast. Everything from §2 on is implemented fact.

---

## 2. Reality Check — What Actually Exists Today

> ⚠️ This section was a "greenfield prototype" snapshot from early June 2026. It is now SUPERSEDED — most of the
> "does not exist yet" items below were built across June 2026. The authoritative, up-to-date system notes live
> in the agent MEMORY (`.claude/.../memory/*.md`); the per-system docs (ROAD_SYSTEM, TRAFFIC_SYSTEM, NETWORKING,
> MENU_SYSTEM, BACKEND/DATABASE/DEPLOY) are accurate. The summary below reflects reality as of **June 2026**.

**A co-op 3-player Dhaka bus-sim — Unity client + Spring Boot/Postgres backend, both real and working.**

### What works today ✅
- **Driving + world:** textured bus, endless **pooled tiled road** (`TiledRoadStreamer`) with floating origin,
  real **road-surface textures** (Road012A asphalt), procedural lane markings, smog/fog, day/night cycle.
- **Roadside city wall:** `BuildingSpawner` streams `Bld_*` city prefabs both sides, gapless, off the footpath.
- **Traffic:** chaotic Dhaka traffic — rickshaw/car/bus/**truck** kinds (real 3D models from
  `Resources/Vehicles/<category>/`), lane-spread density, mass-based pushing, lane-crossing, **aggressive rival
  buses** that queue at stops; the **driver guide line** (eagle-vision optimal path).
- **Shift economy:** shift timer, earnings, bus health, end-of-shift summary + **leaderboard** (career standings).
- **Passengers/fares:** full lifecycle (wait → board with chaos + vehicle-avoidance → tiered fare clock → collect
  → alight → pedestrian). **Overhead state dots** (not body recolour, ready for real sprites) + **selection
  outline**. Footpath pedestrians.
- **Three roles:** driver + door conductor (C1) + inside conductor (C2). **Solo auto-conductors** (switchable).
  C1 boarding run-up with the slow-bus speed gate.
- **2.5D billboard characters** (`BillboardCharacter`) for passengers/crew/crowd (sprite swap-in ready).
- **Menus/front-end:** pixel-UI Main Menu / Lobby (crew-pick) / Settings / **Shop & Customize** / **Leaderboard**,
  **login/signup accounts**, **Bhara currency** + purchase packs, bus-colour/conductor/upgrade store.
- **Backend (REAL):** Spring Boot + Postgres `bame_plastic_db` — `/ws/session` WebSocket relay (lobby JSON +
  binary game hot-path), REST auth/store/leaderboard, admin API + a Control Hub dashboard.
- **Multiplayer in-game:** `GameNet` driver-authoritative sync (bus pose proxy, conductor intents → results,
  passenger NetIds, remote avatars, **server-synced pause**, **driver-drop role failover**).

### What's still thin / deferred ❌
- **Real character art** — still placeholder billboard silhouettes (sprite import in progress).
- **Audio** — clips exist + play (engine/horn/ambient/board via `Sfx`/`BusAudio`/`WorldAmbience`), but there's
  **no AudioMixer asset and no music track**, so Master/Music/SFX can't truly separate (falls back to
  `AudioListener.volume`).
- ~~Control rebinding~~ — **DONE** (2026-06-14): interactive rebinding in Settings ▸ CONTROLS + full
  gamepad/keyboard UI navigation.
- ~~Achievements~~ — **DONE** (2026-06-14): backend catalog + `player_achievements` + client panel + unlock toast.
  (**Friends** still unwired — schema exists, intentionally skipped.)
- **Some polish** — LOD on dense traffic, building variety filters.

> **Bottom line:** the full vertical slice exists end-to-end (drive + conduct + earn + shop + co-op MP + backend).
> Remaining work is ART (sprites), AUDIO, and polish — not greenfield systems.
> (The June 2026 polish passes — fare model, police hazard, rebinding/gamepad UI, achievements, adaptive rivals,
> SSLCommerz payments, WebGL-served-by-backend demo — are DONE; see the Change Log below.)

---

## 3. Repository Layout (flat, post-cleanup)

All game content lives **flat under `Assets/`** (no wrapper folder).

```
g:\Bame Plastic\
├── Assets/
│   ├── Scenes/        BamePlastic.unity        ← the one playable scene
│   ├── Scripts/
│   │   ├── Core/         BusController.cs, GameInput.cs, BusTag.cs, ExtensionMethods.cs
│   │   ├── Generation/   LevelLayoutGenerator.cs, LevelChunkData.cs, TriggerExit.cs, PooledChunk.cs
│   │   ├── Environment/  DayNightController.cs, FloatingOrigin.cs, BusCameraFollow.cs
│   │   ├── Gameplay/      ShiftManager, RivalBus, ChunkContent (bus-stop host), Passenger, PassengerPool, BusPassengers, Conductor, InsideConductor, RoleController, Obstacle, TrafficSpawner
│   │   ├── Visual/        Billboard.cs, BillboardCharacter.cs, BillboardDemo.cs (placeholder sprite people)
│   │   └── UI/            ShiftHud.cs
│   ├── Editor/        BakeRoadMeshes.cs, ReportWorldScale.cs, ApplyWorldScale.cs   ← Tools ▸ Bame Plastic ▸ …
│   ├── Prefabs/
│   │   ├── Player/      Player.prefab           ← the player bus
│   │   ├── Level/       LevelBlock ×5, ExitTrigger, Stars, TrackCurved (curved road, ProBuilder)
│   │   └── Props/       KenneyNature/ (roadside trees), palm_large
│   ├── Art/
│   │   ├── Bus/         Bus.fbx (Volvo B10M) + materials + textures   (the live bus)
│   │   ├── Models/      "FBX format"/, Kart/ (Kart.FBX — see note), (BakedRoads/ appears after baking)
│   │   ├── Materials/   Ground, TrackMaterial (the road material — now grey), ProceduralSky, ExitMaterial, Particles/
│   │   └── Images/      spark/flare sprites
│   ├── Settings/        LevelChunkData/ (5 SOs) + live URP assets (Mobile/PC RP + renderers)
│   ├── Resources/       DOTweenSettings.asset, BillingMode.json   (loaded by name — keep folder named "Resources")
│   ├── ThirdParty/DOTween/   (single copy; used by BusController boost)
│   ├── DefaultVolumeProfile.asset, UniversalRenderPipelineGlobalSettings.asset   (URP config, referenced by ProjectSettings)
│   └── PlayerInputActions.inputactions   (registered project-wide; unused by code — see §6)
├── Packages/manifest.json     (see §6 — note: ProBuilder is now required)
├── ProjectSettings/
├── PROJECT_UNDERSTANDING.md
└── Library/, Temp/, Logs/, obj/, *.csproj, *.slnx   generated — IGNORE
```

**Note on `Art/Models/Kart`:** `Player.prefab` still references `Kart.FBX` (the bus's wheels / particle mounts carried over from the original kart). It's *live*, not dead. Fully decoupling the bus from it is a future tidy-up.

---

## 4. The Code (every script explained)

20 first-party scripts (everything under `ThirdParty/DOTween` is third-party).

### Core/
- **`BusController.cs`** — ★ the player vehicle. **Sphere-physics arcade controller**: a detached `Rigidbody` sphere drives the physics (forces in `FixedUpdate`); the visible bus follows it each `Update`. **Legacy Input Manager** (`W` accel, `S` brake, `Horizontal` steer, `Jump`/Space hold = drift → release = boost). Drift tiers swap spark colors; `Boost()` uses **DOTween**. Ground-normal alignment via raycast. **`ApplyImpact(speedMultiplier)`** — public; obstacles/hazards call it to shed speed + cancel drift on contact. → This is the Driver-role movement prototype; lane constraints not yet added (intentionally kept as free-roam track for now).
  - **Setup note:** the **sphere Rigidbody has Interpolate ON** (needed for the smooth camera to not jitter).
- **`GameInput.cs`** — ★ central input on the **new Input System**, code-defined (no asset). Singleton, auto-creates. Action sets **Driving** (accelerate/brake/steer/drift) + **OnFoot** (move/action/altAction) + global **toggleRole**; `EnableDriving()`/`EnableOnFoot()` switch sets. Each binding has keyboard + gamepad. The bus + both conductors read from here; `RoleController` flips the active set.
- **`BusTag.cs`** — marker component identifying the player bus so chunk triggers / obstacles detect it.
- **`ExtensionMethods.cs`** — `Remap(...)` helper used by drift math.

### Generation/ — *infinite road, now a treadmill object pool*
- **`LevelChunkData.cs`** — `ScriptableObject` per chunk-type: `Direction {N,E,S,W}`, `chunkSize` (assets use **1600×1600**), `GameObject[] levelChunks` (variants), `entryDirection`, `exitDirection`. Five SOs in `Settings/LevelChunkData/`.
- **`LevelLayoutGenerator.cs`** — picks the next chunk by matching the previous chunk's `exitDirection`, advances `spawnPosition` by chunk size, random-picks a matching variant. **Now pools chunks (treadmill):** `Prewarm()` builds all instances at load (parked far away); `GetFromPool` reuses one by **repositioning** it (never `Instantiate`/`SetActive` during play → no spawn stutter); `RecycleChunk` parks a passed chunk (keeps it active). Tunable **`prewarmPerVariant`** (~12 to avoid mid-game builds). Subscribes to `OnChunkExited` (spawn next) + `OnChunkRecycle` (park). `UpdateSpawnOrigin()` is the hook for a floating-origin shift. Press `T` = debug spawn.
- **`TriggerExit.cs`** — on the invisible `ExitTrigger` at each chunk's end. On `BusTag` enter (once, `exited` guard): fires `OnChunkExited`, then after `delay` (5 s) fires **`OnChunkRecycle`** (pool parks it). **`ReArm()`** re-arms the trigger on reuse (since pooled chunks are never toggled off/on). `OnEnable` also resets the guard.
- **`PooledChunk.cs`** — tiny marker added at runtime recording each instance's `sourcePrefab` so the pool returns it to the right bucket.

### Environment/
- **`DayNightController.cs`** — self-contained day/night cycle (`[ExecuteAlways]`); sun rotation + sun/ambient/fog gradients + fog density. Now exposes `externalTimeControl` + `SetTimeOfDay()` so `ShiftManager` can lock the cycle to the shift clock (defaults off = standalone auto-cycle).
- **`FloatingOrigin.cs`** — keeps the world near origin for float precision by teleporting all roots back when the camera passes `threshold`, and tells the generator (`UpdateSpawnOrigin`). **CURRENTLY DISABLED** (component off / threshold set huge) — its teleport destabilised the sphere-physics bus (flung it off the road) and isn't needed for dev-length sessions. It's been patched to flush Rigidbody interpolation on recenter. **To be re-implemented properly** (bus-anchored, physics-safe, pooling-aware) before any very-long-run mode. See §7.
- **`BusCameraFollow.cs`** — the gameplay camera. Smooth chase cam: sits behind+above the bus at a fixed ~30° pitch, follows position (SmoothDamp) and heading with damping. **Snaps to the bus on the first frame** (so it never starts far from origin and trips FloatingOrigin). Lives on a standalone `Main Camera` (not a child of the bus) which also carries `FloatingOrigin` (currently disabled) + URP camera data. Tunables: `distance`, `height`, `pitch`, `positionSmoothTime`, `yawLerpSpeed`.

### Gameplay/ — *shift economy + traffic*
- **`ShiftManager.cs`** — ★ the run/clock/score spine. **Singleton, single source of truth** for the **shift timer (~10-min day→night), your taka, bus health, and the rival leaderboard.** Public API `AddEarnings()` / `Damage()` / `Repair()` is what gameplay systems (and later the backend) push into. Drives `DayNightController` so **dusk = shift end**, ends the run + shows the summary, `R` restarts. **One-component setup:** auto-finds the day/night controller, auto-generates rivals, spawns the HUD. Contains a clearly-marked **TEMP placeholder income** (passive trickle + `E` debug fare, `H` debug damage) to be deleted once passenger fares feed `AddEarnings()`.
- **`RivalBus.cs`** — plain serializable class: a competing company bus that *simulates* rising earnings (`earnRate` + `burstiness`). The HUD reads only `Taka`, so a real AI / networked bus can replace it later with **no UI change**.
- **`ChunkContent.cs`** — ★ the chunk's **bus-stop host**, riding the treadmill pool. The generator auto-adds it and calls `OnActivated(isStop)` each time a chunk goes live; **every 3–4 straight chunks is a stop**. A stop places an indicator on the LEFT of the road and arranges waiting passengers in **clumps + scattered randoms**; when the bus pulls up slow & near, ~half walk to the door and board. Built lazily (first time it's a stop) then only reset on reuse — never re-Instantiated. *(This is the pattern ALL world entities follow — see §7.)*
- **`Passenger.cs`** — placeholder passenger, transform-driven (kinematic — safe on pooled/teleporting chunks). Lifecycle: Waiting → **Gathering** at the curb (when the bus approaches `gatherRange`) → walk to the moving door → board (re-parent into the cabin, pay) → **Aboard** (rides the bus) → leaves after a dwell, returns to the pool. Colour = state (clothing waiting, yellow heading, green aboard).
- **`BusPassengers.cs`** — on the bus: door position + bus speed, **1-by-1** seat assignment (staggered by `boardInterval`), a **Cabin** transform + grid of seat slots that boarded passengers re-parent into so they **ride the bus** (position, turns, pothole bumps), and banks fares into `ShiftManager`. Auto-creates DoorAnchor + Cabin + slots; tune `cabinLocalCenter`/`cabinLocalSize` to your bus interior. (Board→pay replaces placeholder income — turn off `ShiftManager.enablePlaceholderIncome`.)
- **`PassengerPool.cs`** — global pool of passengers, **pre-built at load** (no mid-game Instantiate). Stops borrow waiting passengers; they're returned when un-boarded or when an aboard passenger leaves. Auto-created by the generator at startup.
- **`Conductor.cs`** — Conductor 1, the door conductor you control: a billboard moved with WASD (camera-relative). `Grab` (E) scoops the nearest waiting passenger and carries them; pressing again / `Throw` (Q) **throws them in an arc** to the door (they board on landing). Rides at the bus door when not controlled. Transform-driven.
- **`InsideConductor.cs`** — Conductor 2, the inside conductor: a billboard that rides the **Cabin** and, when controlled, walks the cabin (WASD, clamped to the cabin footprint). **Interactive haggle:** `E` near an aboard passenger starts arguing — the demand climbs (shown on a screen prompt); `E` again locks it in for that bonus, but each passenger has a **hidden patience** and pushing past it = refused (nothing). Builds its own small screen-space prompt.
- **`RoleController.cs`** — single-player convenience that cycles control + camera across the three roles with `C`: **Driver → Conductor 1 → Conductor 2 → …** (the real game has one player per role, simultaneous). Conducting sets `BusController.controlEnabled=false`, so the bus simply **coasts** (no forced stop/freeze, no auto-drive) — Conductor 2 works whether the bus is rolling or stopped, and a bulletproof **ground-clamp** in BusController keeps it from ever sinking through the road. Both conductors use a close ~30° chase cam on the conductor; Conductor 2 also gets a **roof cutaway** — an oblique camera **clip plane** slices off the top `roofCut` (~30%) of the bus so you see into the cabin (single-mesh-safe; `flipCut` if the wrong half clips). Auto-finds bus + camera, auto-creates both conductors. Uses `BusCameraFollow.Retarget()` (heading from the bus so billboard conductors don't feed the chase yaw). Add this to a manager object.
- **`Obstacle.cs`** — traffic that penalises the bus on trigger contact: `ApplyImpact` (sheds speed) + `ShiftManager.Damage` (bus health). Robust detection (matches the bus's physics sphere by Rigidbody, or BusController/BusTag). Tunables `speedAfterHit`, `damageOnHit`, `hideOnHit`.
- **`TrafficSpawner.cs`** — bus-relative traffic: builds a small **pool** of code-generated placeholder vehicles (coloured boxes + trigger + `Obstacle`, no prefabs) and keeps them ahead of the bus, recycling each ahead once passed (no mid-game Instantiate). Auto-finds the bus; **auto-created by the generator** (or add one to tune `count`/`lateralSpread`/`damageOnHit`/`groundMask`). ⚠️ Self-managed pool (not chunk/bus-parented) — needs origin-shift handling when FloatingOrigin returns.

### Visual/ — *placeholder 2.5D characters*
- **`Billboard.cs`** — rotates a sprite to face the camera (upright/yaw by default). The 2.5D billboard trick; goes on every character/prop sprite.
- **`BillboardCharacter.cs`** — generates a tinted **head+body silhouette** sprite in code (no art) + a `Create(name,color,height,pos)` factory + `SetColor()`. The single placeholder reused for passengers, conductors, and crowd — swap the sprite for real art later. State is shown by **colour**.
- **`BillboardDemo.cs`** — *(throwaway test)* scatters a static crowd near an empty GameObject. Superseded by `ChunkContent` (procedural per-chunk crowd) — remove it when using that.

### UI/
- **`ShiftHud.cs`** — builds the entire HUD **in code** (no manual Canvas): taka + bus-health bar (top-left), shift timer (top-centre), live rival **standings** board (top-right), and the full-screen end-of-shift **summary** panel. Polls `ShiftManager` each frame. Legacy uGUI `Text` + `RawImage` on the builtin `LegacyRuntime.ttf` font, so it needs zero scene/asset setup.

### Editor/
- **`BakeRoadMeshes.cs`** — `Tools ▸ Bame Plastic ▸ Bake Selected Road Meshes`. **Final-optimization tool** (not for daily use). Saves a ProBuilder road's built mesh as an asset, points MeshFilter/MeshCollider at it, and strips the ProBuilder components so it never rebuilds at runtime. Re-bakeable (overwrites same asset GUID). Run in Prefab Mode on `TrackCurved` + `RevisedTrack` once geometry is final; keep a ProBuilder source (Plastic revert) to edit shape again. Textures/materials are always editable regardless.

> Removed earlier: `EventManager.cs` (empty stub), `SimpleCameraController.cs` (URP debug fly-cam — replaced by `BusCameraFollow`).

---

## 5. Runtime Flow (current prototype)

1. `Assets/Scenes/BamePlastic.unity` loads.
2. The `LevelGenerator` (`LevelLayoutGenerator`) **prewarms** the chunk pool, then spawns the starting strip by repositioning pooled chunks (stitched by entry/exit direction). A pre-placed first chunk sits at origin under the bus.
3. The **bus** (`Player.prefab` → `BusController` + `BusTag`, with the sphere physics rig) drives via legacy WASD/Space.
4. The standalone **`Main Camera`** (`BusCameraFollow`) smooth-follows the bus at ~30°.
5. Crossing a chunk's `ExitTrigger` spawns the next chunk (pool reuse) and parks the passed one ~5 s later.
6. `DayNightController` cycles lighting. `FloatingOrigin` is **off** (bus drives in absolute world coords).

---

## 6. Tech Stack & Configuration

- **Engine:** Unity **6000.4.6f1**, URP `17.4.0`, Cinemachine `3.1.6` (installed, unused — `BusCameraFollow` is a custom script instead).
- **⚠️ ProBuilder (`com.unity.probuilder`) is REQUIRED.** The road pieces (`TrackCurved`, `RevisedTrack`) are ProBuilder meshes (`PolyShape` + `ProBuilderMesh`). If the package is missing, the roads don't render (they show as "missing script" + empty MeshFilters — exactly what happened when the project was first combined). Don't remove it. (See memory note `probuilder-roads`.) The bake tool (§4 Editor) can later convert them to static meshes to drop the runtime dependency.
- **Input:** the **new Input System** drives gameplay via **`GameInput`** (code-defined actions — no `.inputactions` asset; keyboard + gamepad work out of the box). Two action sets — **Driving** (bus) and **OnFoot** (conductors); `RoleController` enables exactly one, which is what stops input leaking to the bus while you're on foot. **`activeInputHandler` must be "Both" or "Input System Package (New)"** (Project Settings ▸ Player). A few **debug keys** still use legacy `Input.*` (generator `T`, ShiftManager `E/H/R`) — fine under "Both".
- **Other packages:** AI Navigation (NavMesh), Multiplayer Center (tooling only — no netcode yet), Post-processing, Timeline, uGUI, Visual Scripting, Input System.
- **Build settings:** scene list → `Assets/Scenes/BamePlastic.unity`.
- **URP config:** active = `Settings/Mobile_RPAsset` + `PC_RPAsset` (+ renderers) + root `UniversalRenderPipelineGlobalSettings.asset`. Don't move/delete without updating ProjectSettings.
- **Third-party in-tree:** DOTween, Kenney Nature Kit.

### Backend (planned, not present)
Java + Spring Boot over REST + WebSocket — the largest remaining AOOP piece (entities for Bus/Passenger/Fare/Route/Player/Session, services, controllers, WebSocket for crew sync + shared taka).

---

## 7. Known Gotchas / Loose Ends

- **ProBuilder dependency** (above) — the #1 thing that breaks roads if dropped.
- **FloatingOrigin is currently OFF.** Bus drives in absolute world coords; fine for dev, but float precision degrades after tens of thousands of units (~minutes of continuous driving). **Re-implement bus-anchored:** shift by `-bus.position` (not the camera), only past a large threshold, `Physics.SyncTransforms()` + flush Rigidbody interpolation, bump generator `spawnOrigin`, shift only active chunks, and a startup guard. Pooling-aware (idle/parked chunks don't need shifting).
- **★ Scale: 1 unit = 1 metre.** The **bus is the reference** — its body is already correct (3.05 × 3.25 × **12.19 m**, scale 1) with real gravity (`gravity=10`) and ground-raycasts (1.1/2.0). The **chunks** were the only thing oversized (root scale **80**, `chunkSize` **1600**, road ribbon ~436 m wide). `Tools ▸ Report World Scale` measures everything; `Tools ▸ Apply World Scale` shrinks chunks + `chunkSize` together by a factor (0.05 → road ~22 m, turn radius ~21 m, tile ~84 m, `chunkSize` 80) and leaves the bus/camera/physics alone. Road width and turn radius are **locked together** by the mesh, so the factor trades them off. After rescaling, lower `BusController.acceleration` (30→~12) for sane pacing on the smaller chunks. *(Apply pending user run + playtest.)*
- **★ World entities must ride the pool.** Every world object (passengers, bus stops, crowd, hazards) must parent to **a chunk** (rides the treadmill; built at load, reset — not re-Instantiated — on reuse via `ChunkContent.OnActivated()`) or **the bus** (aboard passengers, conductors, bus-relative traffic). **Never a static world root** — it'd be orphaned by the treadmill and would break the planned FloatingOrigin shift. (Memory: `world-entities-ride-the-pool`.)
- **Spawn performance:** generation is a treadmill pool — chunks built once at load (prewarm), then reused by repositioning. Set `prewarmPerVariant` high enough (~12) that nothing builds mid-game. The heavy per-build cost is the **ProBuilder road mesh rebuild**; the `BakeRoadMeshes` tool removes it for good once geometry is final.
- **VCS:** only ~66 template files are tracked in **git**; the real game assets are **untracked in git**. The team uses **Plastic SCM** (`.plastic/`). Before destructive ops, ensure a Plastic checkpoint. (Memory: `vcs-plastic-not-git`.)
- **Windows file ops:** use **PowerShell `Move-Item`**, not Git-Bash `mv`, for Unity asset moves; Unity editor must be **closed** during shell moves. (Memory: `windows-unity-file-moves`.)
- **Bus depends on `Kart.FBX`** (wheels/mounts) — works, not fully decoupled.
- **Single scene only.** Conductor modes will be separate scenes/additive loads sharing a networked session.
- **No backend, networking, HUD, or game loop** — see §2.

---

## 8. Suggested Path: Prototype → Vision

1. **The shift spine (next):** a `ShiftManager` (10-min timer + money + run state), the day/night cycle tied to that timer, a HUD (your taka + the **rival-bus standings** + timer + bus health), and an end-of-shift summary. This converts the driving toy into a **timed, scored, competitive shift** — the container every later system plugs into, and the object the backend will mirror. Rivals start as *simulated* earners.
2. **Passenger economy:** `Passenger` objects with the board→≥1min→pay→leave **fare clock**, demand-driven stops (majority-out + random-out), fares, and haggling. In the single-player driver build, fares can auto/stub-resolve until Conductor 2 exists.
3. **Driver stakes & Dhaka mechanics:** wire traffic + bus health, potholes/waterlogging, traffic police, and **overtake / park-in-front-to-poach**.
4. **The two conductor roles:** door conductor (follow-cam; jump/grab/throw at ~5–10 mph) and inside conductor (roof hidden; push-to-seat + haggle mini-game) — as modes/scenes sharing the shift state.
5. **Backend & multiplayer:** Spring Boot OOP domain (Player, Crew/Session, Bus, Passenger, Fare, Stop, Event, **RivalBus standings**); REST for lobby/session; WebSocket for the shared money pot + real-time crew sync (turning rival buses into real players).

---

## 9. Quick Reference — Where Things Live

| I want to… | Look at |
|------------|---------|
| Bus driving/drift/boost/impact | `Assets/Scripts/Core/BusController.cs` |
| Camera follow / 30° feel | `Assets/Scripts/Environment/BusCameraFollow.cs` (on `Main Camera`) |
| Endless-road generation / pooling / `prewarmPerVariant` | `Assets/Scripts/Generation/LevelLayoutGenerator.cs` |
| Chunk recycle / exit trigger | `Assets/Scripts/Generation/TriggerExit.cs` |
| New road-chunk type | `LevelChunkData.cs` + new SO in `Assets/Settings/LevelChunkData/` |
| Day/night/fog | `Assets/Scripts/Environment/DayNightController.cs` |
| Floating origin (currently off) | `Assets/Scripts/Environment/FloatingOrigin.cs` |
| Traffic / obstacles (to wire) | `Assets/Scripts/Gameplay/TrafficSpawner.cs`, `Obstacle.cs` |
| Bake roads to static (final perf) | `Assets/Editor/BakeRoadMeshes.cs` (Tools ▸ Bame Plastic) |
| Road look / lane lines | material `Assets/Art/Materials/TrackMaterial.mat` (shared by all roads) |
| Playable scene / player bus | `Assets/Scenes/BamePlastic.unity` · `Assets/Prefabs/Player/Player.prefab` |
| The (nonexistent) backend | — Spring Boot project to be scaffolded |

---

## 10. Change Log

### 2026-06-04 — Cleanup & rename
- Removed ~435 MB of duplicate/dead assets (two unused PAZ672 buses dominated); deduped DOTween, Kenney, materials/models; removed `EventManager`, `SimpleCameraController`, redundant input/URP-template leftovers.
- Reorganized 4 merged source folders into the flat `Assets/{Scenes,Scripts,Prefabs,Art,Settings,Resources,ThirdParty,Documentation}`. Renamed scene `CombinedGame.unity` → `BamePlastic.unity`; fixed `EditorBuildSettings`.
- Renamed kart/car branding → bus: `KartController`→`BusController`, `CarTag`→`BusTag`, fields `kartModel`/`kartNormal`→`busModel`/`busNormal` (`[FormerlySerializedAs]`), GameObjects `Kart`/`KartModel`→`Bus`/`BusModel`. GUIDs preserved throughout; verified 0 broken references + clean recompile.

### 2026-06-05 — Driving, camera & generation performance
- **Roads fixed:** diagnosed that the road pieces are **ProBuilder** meshes and the `com.unity.probuilder` package had been dropped in the original combine (→ missing scripts, invisible roads). Installing ProBuilder restored them. `TrackMaterial` recolored magenta→grey; lane-line/asphalt texturing is material work (ongoing).
- **Camera:** replaced the rigid child camera with a standalone smooth ~30° chase cam (`BusCameraFollow`), which snaps to the bus on frame 1. Enabled **Interpolate** on the sphere Rigidbody to kill follow jitter.
- **FloatingOrigin:** its recenter teleport destabilised the physics bus → **disabled** for now; patched to flush Rigidbody interpolation on recenter for when it's re-implemented (bus-anchored — see §7).
- **Generation perf:** converted to a **treadmill object pool** (`PooledChunk` + pooling in `LevelLayoutGenerator` + recycle/`ReArm` in `TriggerExit`) — chunks are built once at load (`prewarmPerVariant`) and reused by repositioning, never instantiated/toggled mid-game.
- **Tools/scripts added:** `BakeRoadMeshes` editor tool (static-mesh bake), and traffic scripts `TrafficSpawner` + `Obstacle` + `BusController.ApplyImpact()` (written, not yet wired).

### 2026-06-05 — Design expanded (shift / competition / roles)
- Reframed the game (see §1): the run is a **~10-min day→night shift**; the goal is to **out-earn rival company buses** shown on a live UI standings board (no road finish line — the timer is the end).
- Locked in the **Dhaka earning mechanics** (haggle fares, overtake + park-in-front to poach passengers, keep bus health) and the **three refined roles** (driver; door conductor who jumps/throws passengers at 5–10 mph; inside conductor who pushes-to-seat + haggles).
- Defined the **passenger fare clock** (board → ≥1 min dwell for the inside conductor to collect → leaves at a stop or random event; uncollected = lost income) and **demand-driven stops** (majority-out + single random-out forces an immediate stop).

### 2026-06-05 — Shift spine built (run / score / HUD)
- Added **`ShiftManager`** (singleton run-state: ~10-min shift timer, taka, bus health, rival leaderboard; `AddEarnings`/`Damage`/`Repair` API; drives day→night so dusk = shift end; end-of-shift summary; `R` restart), **`RivalBus`** (simulated competing-bus earner), and **`ShiftHud`** (code-built HUD: taka + health bar + timer + live standings + summary). `DayNightController` got `externalTimeControl`/`SetTimeOfDay`. One-component setup; income is a clearly-marked placeholder until passengers exist. *(Pending in-editor verification.)*

### 2026-06-05 — Placeholder billboards + procedural crowd
- Added the **billboard placeholder pipeline** (`Billboard`, `BillboardCharacter` — code-generated tinted silhouette sprites; `BillboardDemo` test) — the real 2.5D character path, reused for all people.
- Added **`ChunkContent`**: roadside crowd now **rides the treadmill chunk pool** (built at load as chunk children, reset on reuse, never re-Instantiated). Generator auto-adds it + calls `OnActivated()` on activation. Established the rule that **all world entities parent to a chunk or the bus** (see §7 / memory).

### 2026-06-05 — World scale fix (1 unit = 1 m)
- Measured (via a new `ReportWorldScale` editor tool): the **bus is already correct** (12.19 m body, scale 1, real gravity/raycasts) — only the **chunks** were 80× (root scale 80, `chunkSize` 1600, road ribbon ~436 m wide). The earlier "scale everything incl. bus" assumption was wrong.
- Added `ApplyWorldScale` (Tools menu): shrinks chunk prefab scale + the 5 `chunkSize` SOs together by a factor (default 0.05 → road ~22 m, turn radius ~21 m, tile ~84 m), leaving bus/camera/physics untouched. Idempotent + retunable.
- Made placeholder characters **scale-independent**: `BillboardCharacter` now divides by parent lossyScale (always ~1.8 m even parented to a scaled chunk), and `ChunkContent` places crowd by **world-metre offsets** (no longer inheriting the 144 m-giant / clustering bugs).

### 2026-06-05 — Bus stops + boarding loop
- `ChunkContent` rewritten from ambient crowd into a **bus-stop host**: generator marks every 3–4 **straight** chunks as a stop; stop spawns an indicator on the LEFT + waiting passengers in **clumps + randoms** (kinematic, ride the chunk). Added `Passenger` (walk→board→pay→recycle state machine) and `BusPassengers` (on the bus: door anchor, speed gate, 1-by-1 boarding, banks fares to `ShiftManager`). Bus pulls up slow & near → ~45% board → real taka. Setup: add `BusPassengers` to the bus, turn off placeholder income. *(Pending playtest/tuning: door position, stop side offset, board speed.)*

### 2026-06-05 — Curb gather-up + cabin persistence
- Two-phase pickup: passengers **gather at the curb** when the bus is within `gatherRange` (~45 m), then **board** within `boardRange` (~22 m) + slow. Fixes "they only react when I'm right on top of them."
- Added **`PassengerPool`** (global, pre-built at load); `ChunkContent` now borrows from it (returns un-boarded ones on reset) instead of a per-chunk pool. Boarded passengers **re-parent into the bus `Cabin`** and ride it, then **leave after a dwell** and return to the pool (sustainable, no depletion). FloatingOrigin recenter hard-disabled (`recenter=false`) — was teleporting the bus at threshold 100.

### 2026-06-05 — Conductor 1 + crowd tuning
- Bumped crowd (~30/stop) + stops every 2–3 chunks (set `minStopGap`/`maxStopGap` on the LevelGenerator instance), pool → 250.
- Added **Conductor 1**: `Conductor` (WASD billboard, grab/throw) + `RoleController` (`C` toggles driver↔conductor; bus coasts while conducting via `BusController.controlEnabled`) + `BusCameraFollow.Retarget` (target/heading swap). Throw = passenger flies to door + boards; physics-arc throw is later polish.

### 2026-06-05 — Conductor 2
- Added **`InsideConductor`** (Conductor 2): rides + walks the cabin, `E` haggles aboard passengers for a bonus fare (instant in v1). `RoleController` now cycles **Driver→C1→C2** with `C`, hides the roof + uses a top-down camera for C2 (`BusCameraFollow.Retarget` gained a pitch param). Base fare is still collected on board (so solo driving earns); haggle is a bonus on top — the strict "unpaid-until-conductor / lost if missed" model is a future toggle.

### 2026-06-05 — New Input System migration
- Gameplay input moved off legacy `Input.*` to the **new Input System** via code-defined **`GameInput`** (no `.inputactions` asset; keyboard + gamepad). Driving and OnFoot are **separate action sets**; `RoleController` enables one at a time — which fixes "the bus moves while I'm controlling a conductor" (the bus's actions are simply disabled on foot). Migrated `BusController`, `Conductor`, `InsideConductor`, `RoleController`. Debug keys (`T`, `E/H/R`) stay legacy under "Both". Requires `activeInputHandler` = Both or New.

### 2026-06-05 — Driver stakes v1 (traffic + bus health)
- Wired traffic: `TrafficSpawner` (pooled, code-gen placeholder vehicles, bus-relative, auto-created) + `Obstacle` (trigger → shed speed + `ShiftManager.Damage`). Added `BusController.Instance` and **health-scaled acceleration** (`HealthFactor` 0.4–1.0) so a battered bus is sluggish — dodging traffic now matters. Hits are pass-through triggers (physical bump is later polish).

### 2026-06-05 — Conductor polish
- **Interactive haggle** (C2): `E` starts arguing, the demand climbs on-screen, `E` locks it in; each passenger has a hidden patience — overshoot = refused. **Throw arc** (C1): thrown passengers fly to the door on a parabola, then board. (Further driver stakes — potholes/police/poaching — dropped from the plan per direction.)

### 2026-06-05 — Conductors are motion-independent (no freeze, no auto-drive)
- The 3 roles are **simultaneous players**, so we don't force the bus for conducting. Tried freeze (rejected) and auto-drive (rejected); settled on: when no human drives it the bus just **coasts**, and a **bulletproof ground-clamp** (`GroundClamp()` — raycast from above the sphere, keep it on the highest non-bus surface, layer/thin-collider-independent) stops it ever falling through — so Conductor 2 works whether the bus is rolling or stopped. (Memory: `three-players-bus-always-moving`.)

### 2026-06-07 — Clean bus rig + bus-tuned controller + feel/juice
- **Rebuilt the player bus** on the Volvo B10M with a clean rig (Player → Sphere[+BusTag] / Normal → BusModel → Front_/Back_Wheels; root sits at the ground-contact point = sphere − radius). Editor tools: `Bame ▸ Build Clean Bus Rig`, `Fit Bus To Ground` (scale/centre/ground, snaps scale to 1), `Wheels ▸ Assign Selection as Front/Back Wheels` (pivot at axle).
- **Rewrote BusController** for the bus: heavy pickup + strong brakes, speed-gated steering (no spin-in-place), automatic gearbox (`Gear`/`Rpm01`/`SpeedKmh`/`SpeedNormalized`/`Shifting` exposed), grip-vs-slide drift (small `driftAngle`, big `driftLean`), wheels roll from actual velocity + auto-measured radius, velocity-based ground hold (no jitter). Dropped all kart cruft (tiers/turbo/DOTween/particles).
- **Feel & juice:** `SpeedometerHud` (km/h + gear + RPM bar), `BusAudio` (procedural two-tone horn — zero clips; engine synth tried then removed as too harsh), camera FOV-by-speed + impact shake via `BusController.Impacted`. Added always-on `horn` input (H / gamepad north). (Drift shake + bus lights were tried and removed per direction.)
- **Cabin sim (push-to-seat):** riders **sit first**, filling seats **front→back**; at ≈70% (`seatStandThreshold`) they **stand at the front of the centre aisle**. Conductor 2 is **confined to the aisle lane** (`aisleHalfWidth`), **slows** in the crowd, and **shoves** (Q) a standing rider, who **walks** back to a random free seat or the backmost free standing spot. `BusPassengers.CabinSpot` + front-ordered `_seats`/`_stands`; `Passenger` holds a `CabinSpot` (`PushTo` to walk). Randomness via random seat pick + `standJitter`.

### 2026-06-08…12 — Vertical slice completed (consolidated; details in agent MEMORY)
- **Road system v2:** retired ProBuilder roads for the pooled `TiledRoadStreamer` (floating-origin-safe, endless,
  edit-mode bus spawn) + `RoadZone` cross-section + `SplineStopSpawner` + `FootpathPedestrians`. Real **asphalt
  texture** (Road012A, single clean material, scaled). Docs: ROAD_SYSTEM.md.
- **Traffic system:** logical/physical road-relative agents, deterministic MP RNG; **rickshaw/car/bus/truck**
  kinds; lane-spread density (fixed the "raising maxOncoming did nothing" lane-packing bug); mass-based pushing;
  lane-crossing; **aggressive rival buses** (queue at stops, block/overtake); driver **guide line**. Docs:
  TRAFFIC_SYSTEM.md.
- **Real vehicle models:** `Resources/Vehicles/{Cars,Buses,Trucks,Rickshaws,Small,Props}` prefabs; runtime
  `VehicleModelLibrary` (auto-loads each category, fits model to the gameplay collision box); replaced the cubes.
  `VehicleAssetOptimizer` editor tool (512-cap textures, pack material re-texture). Roadside **city buildings**
  (`BuildingSpawner` + `Bld_*` prefabs).
- **Passengers/conductors:** full fare lifecycle; **overhead state dots** (not body recolour — sprite-ready) +
  **selection outline**; boarding **chaos + vehicle avoidance**; **solo auto-conductors** (switchable); C1
  boarding run-up speed gate.
- **Menu + economy:** pixel-UI front-end; **login/signup accounts**, **Bhara currency** + purchase packs,
  **Shop/Customize** (bus colour/conductor/upgrades), **global leaderboard**. Expanded Settings (+ fixed the
  WebGL apply-graphics crash). Docs: MENU_SYSTEM.md.
- **Backend (REAL, running):** Spring Boot + Postgres; `/ws/session` WS relay, REST auth/store/leaderboard,
  admin API + Control Hub dashboard. Docs: BACKEND.md / DATABASE.md / DEPLOY.md / HOSTING_LAPTOP_PUBLIC.md /
  DASHBOARD.md.
- **Multiplayer in-game:** `GameNet` driver-authoritative sync (bus pose proxy, conductor intents → results,
  passenger NetIds, remote avatars, **server-synced pause**, **driver-drop role failover**). Docs: NETWORKING.md.

### 2026-06-15 — Follow-up #2 (selection marker · standings warning · rival tiers · Bhara)
- **Selection marker:** the silhouette-rim outline didn't read (hid behind the opaque body) → replaced with a
  big, bright, BOBBING DOWN-ARROW floating above the selected rider's head, drawn in front of everything
  (`BillboardCharacter.SetSelected` / `EnsureSelectArrow`). Shape-independent, unmistakable.
- **Standings warning woven in:** removed the separate warning line below the board; now the leading rival's row
  itself blinks red with a "▲ … LEADING" tag and the player's row shows "BEAT <name>!" (`ShiftHud.BuildStandings`,
  rebuilt ~16Hz for a smooth blink).
- **Rival earnings in tiers:** `RivalBus` no longer trickles linear taka — the adaptive rubber-band now sets the
  INTERVAL between fares, and each fare is a real `Passenger.FareTiers` jump (10/20/30/50), so the board climbs in
  believable chunks.
- **Currency label:** the in-shift earnings HUD (earnings panel, standings, collect popups, toasts, summary) now
  reads **Bhara ("B")** instead of "Tk". ⚠️ NOTE: in the data model shift earnings are still *taka* that convert to
  Bhara at shift end (100 tk → 10 Bhara) — only the on-screen LABEL changed per request; revisit if the
  conversion needs to follow.

### 2026-06-15 — Follow-up polish (signs · leave-menu · selection/HUD)
- **World signs (#1):** `WorldSign` reverted to an ICON badge (▼ alight / "!" police) with a SMALL text label
  above it (was full-text). Still build-safe (generated sprite + TextMesh, no URP shader → no pink-in-build).
- **Leave-shift (#2):** `MenuMode.EnterMenu()` is now idempotent and called DIRECTLY by `ReturnToMenu` (no
  reliance on the coroutine `Start`), so leaving a solo shift reliably rebuilds the living menu in place + resets
  timeScale/pause.
- **Standings names (#10 fix):** the SCENE had old serialized rival names (`RivalManager` = Sonar Bangla/Dhaka
  Express) → updated to the new set; `ShiftManager.BeginShift` now ALWAYS rebuilds the canonical 5 and
  `RivalBrain` links-only (never adds a row) so stale names can't reappear.
- **Selection/HUD (#3):** `BillboardCharacter` selection is now a real bright RIM outline (4 offset silhouette
  copies, pulsing) instead of a scaled ghost sprite; `InsideConductor.reachRange` raised so you can collect from
  riders SEATED across the aisle; fare feedback rebuilt in PixelUI (rising "+Tk" popup + "press [E] to collect"
  prompt); mic HUD reads "SHOUT LOCATIONS TO GET MORE PASSENGERS" (C1) / "ARGUE LOUDER TO GET MORE BHARA" (C2).
- **Danger glow:** toned down (narrower strips, `maxAlpha` cap, gentler breathe).

### 2026-06-15 — Polish pass DONE (10 fixes: alight flow · stops · peds · rivals · curve bug · juice)
- **Curve rubber-band bug (#7):** the bus's tracked position (`_busTileF`, which ALL road-relative agents sample
  against) projected onto the straight tile CHORD, diverging from the curved arc on bends → every car/ped slid
  back-and-forth. Fixed: `TiledRoadStreamer.ProjectOntoSpan` projects onto the curved `pts[]` polyline.
- **Alight flow (#1):** riders now file OUT the door one-at-a-time and OFF-BEFORE-ON (boarders wait via
  `BusPassengers.AnyAlighting`/`ReleaseOneAlighter`). Roof sign is readable TEXT ("PASSENGERS WANT OFF") via a new
  build-safe `WorldSign` (TextMesh, no URP-Unlit lookup → fixes the "turns purple in build"); police marker uses
  it too.
- **Stops & crowds (#5,#6):** crowd capped 14–30, only `autoBoardCount` (~10) auto-board (rest wait for C1
  shout/grab); shelters sit half-on-ground at the footpath/ground seam, NOT on curves (`IsStraightHere`), crowd
  waits on the footpath.
- **Pedestrians (#3,#4):** road-crossings only at DESIGNATED points (`crossingPointSpacing`, small groups), not
  random everywhere; walker→fare conversion gated by `convertChance` (~0.2, mic raises it) so most people just
  stroll past.
- **Character rendering (#2):** `BillboardCharacter` fades toward `RenderSettings.fog` by distance + dims by
  `DayNightController.Darkness` each frame → far figures melt into the smog instead of staying sharp/bright.
- **Danger glow (#8):** new `DangerVignette` HUD — directional red edge breathing when near another vehicle.
- **Adaptive rivals (#10):** `RivalBus` rewritten as a rubber-band earner targeting `aggression × player
  earnings` (surges when behind, eases when ahead); 5 named buses (Balaka, Victor Classic, Raida, Mirpur Link,
  Osim); HUD "▲ X leads" warning. Physical `RivalManager` buses renamed to link to standings entries (no 6th row).
- **Leave-shift (#9):** SOLO now returns to the menu IN PLACE (`MenuMode.ReturnToMenu` + `ShiftManager.EndToMenu`)
  — bus parks where it is, world keeps streaming (no scene reload → no missing road / falling bus). MP still
  reloads.

### 2026-06-14 — Work pass DONE (fare model · police · rebinding/gamepad · achievements · cleanup)
Implemented:
- **Collect-or-lose-it fares:** confirmed the code already banks NO base fare on board (`Passenger.Collect()` is
  the only income); turned `enablePlaceholderIncome` **off** by default + added a `ShiftManager.FareLost` signal
  and a HUD toast when an unpaid rider alights.
- **Traffic Police hazard:** new `PoliceHazard.cs` (road-relative, ~60s cadence, big overhead "!" marker). Cross
  it over 45 km/h → `BusController.HardStop()` + **−500 Tk** (`ShiftManager.AddEarnings` now allows **negative**).
  Driver-authoritative gate (MP-safe). Police art = CharacterSheet2 cells 0/1 → `CharacterSprites.PoliceMale/Female`.
  Wired into `CreateTiledRoad` + `TransientUICleaner`.
- **Control rebinding + gamepad UI:** `GameInput.StartRebind/ResetBindings` (+ persisted overrides via
  `SettingsStore`); real Settings ▸ CONTROLS rebind UI; `PixelButton` is now EventSystem-navigable (`Selectable` +
  select/submit handlers) with default focus per screen → full controller navigation.
- **Achievements:** code-defined `AchievementCatalog` (9), `PlayerAchievement` table, `AchievementController`
  evaluated at shift award (career stats on `Player`); client `AchievementsScreen` + main-menu button + unlock
  toast. Backend auto-migrates via `ddl-auto=update`. (Friends intentionally skipped.)
- **Cleanup:** deleted obsolete `TrafficSpawner.cs` + `Obstacle.cs`.
- **Cancelled:** §1 "stop on demand".

### 2026-06-15 — Demo backend + payments + DB cleanup + BUILD HARDENING (final pass)
- **SSLCommerz (sandbox) payments:** clicking a Bhara pack opens the hosted gateway; `PaymentController` +
  `payment_transactions` table; client polls to completion then credits Bhara. Free sandbox creds in
  `application.properties`. (BACKEND.md / DEMO_SETUP.md.)
- **Demo backend, robust:** `Backend/backend/DEMO_SETUP.md` + `start-demo.cmd`/`run-jar.cmd`/`docker-compose.yml`.
  Embedded **H2** profile → backend runs with ONLY Java (no Postgres/Docker). For a **WebGL build** the backend
  also SERVES the game (`WebGlConfig` at `/`), so the game + API + WS share one origin (no CORS / no
  mixed-content / no IP to type — client auto-detects via `ServerConfig.SameOriginWebGL`). Verified end-to-end.
- **Database slimmed to 4 real tables** (`players`, `shift_results`, `player_achievements`,
  `payment_transactions`) — deleted 5 dead scaffolding entities/repos + 24 unused "future" tables;
  `schema.sql` rewritten clean (no comments) + `populate.sql` (3 demo accounts: jonayed/akib/toufiq). Schema
  `validate`-clean against the entities (verified on Postgres 18). DATABASE.md + db/README rewritten.
- **BUILD-SAFETY fixes (the "purple sprites" + "inside camera broke in build" bugs):** the 4 custom shaders
  (`BillboardShadowSprite`, `BusRoofClip`, `FogRingHaze`, `SkyboxCubemapBlend`) are referenced ONLY via
  `Shader.Find` (no material asset), so Unity **stripped them from builds** → magenta sprites + the C2 roof never
  clipped (couldn't see inside). Fixed by adding all 4 to **Always Included Shaders** (`GraphicsSettings.asset`)
  PLUS code fallbacks: `BillboardCharacter` → `Sprites/Default` if its shader is null; `RoleController.SetCutaway`
  → hides bus renderers if the clip material is null. **Both assemblies compile with 0 errors** (`dotnet build`).
- **Docs cleanup:** removed 7 redundant/historical MD files (WORK_PLAN, BUILDINGS_HANDOFF,
  CONDUCTOR_NETWORKING_TODO, INTEGRATION_GUIDE, BACKEND_SETUP, Backend/HELP, Backend/README-STOMP-stale) and fixed
  all dangling references. 14 docs remain, each covering a distinct system.

### Next up (not done)
Real character **sprites** (in progress) · audio **music track + AudioMixer** · traffic LOD + building variety.

*Future session: §1 is design intent. The agent MEMORY (`.claude/.../memory/*.md`) is the most current
system-by-system reference; verify any file/symbol named in the OLDER parts of this doc still exists before
relying on it.*
