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

**Unity client only, at an early single-player driving-prototype stage.** It began as a fusion of two imported asset packs (an arcade kart controller + a procedural infinite-road chunk generator), since cleaned up, consolidated, renamed to bus terms, and hardened for performance.

### What works today ✅
Drive a **textured bus** down a randomly-stitched, **infinitely-spawning road with real road meshes**, with drift/boost, a **smooth ~30° chase camera**, a day/night cycle, and roadside trees. Generation uses a **treadmill object pool** (build chunks once at load, then reuse by repositioning — no spawn stutter after warm-up). Runs clean in the editor.

### What does NOT exist yet ❌
- **No shift framing** — no shift timer, money/score, or **rival-bus competition** (the core goal).
- **No passenger economy** — no passengers, fares, haggling, or the board→dwell→pay→leave lifecycle.
- **No conductor roles** — neither the door conductor nor the inside-cabin conductor.
- **No driver stakes wired** — no traffic police, pothole/crash consequences to bus health, or overtake/poach mechanics. *(Traffic scripts exist but are NOT wired into the scene — see §4 Gameplay.)*
- **No HUD/UI, game-state loop, or end-of-shift resolution.**
- **No 2.5D billboard-sprite characters** — everything is full 3D.
- **No Java / Spring Boot backend** (no `.java`/`pom.xml`/`build.gradle`) and **no networking/multiplayer** (`multiplayer.center` is tooling, not netcode).

> **Bottom line:** a solid drivable prototype with a robust endless-road environment. The driver's *movement + world* is in good shape; gameplay systems and the whole backend are greenfield.

---

## 3. Repository Layout (flat, post-cleanup)

All game content lives **flat under `Assets/`** (no wrapper folder).

```
g:\Bame Plastic\
├── Assets/
│   ├── Scenes/        BamePlastic.unity        ← the one playable scene
│   ├── Scripts/
│   │   ├── Core/         BusController.cs, BusTag.cs, ExtensionMethods.cs
│   │   ├── Generation/   LevelLayoutGenerator.cs, LevelChunkData.cs, TriggerExit.cs, PooledChunk.cs
│   │   ├── Environment/  DayNightController.cs, FloatingOrigin.cs, BusCameraFollow.cs
│   │   ├── Gameplay/      ShiftManager.cs, RivalBus.cs, ChunkContent.cs, Obstacle.cs, TrafficSpawner.cs (traffic not yet wired)
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
│   ├── Documentation/   INTEGRATION_GUIDE.md (original asset-pack guide — historical)
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
- **`ChunkContent.cs`** — ★ makes a chunk's world content **ride the treadmill pool**. Built once as **children of the chunk** (at load), then only reset (not re-Instantiated) when the chunk is reused. The generator auto-adds it (toggle `autoPopulateChunks`) and calls `OnActivated()` on each chunk when it goes live (sibling concept to `TriggerExit.ReArm()`). Hosts the placeholder roadside **crowd** now; **bus stops + waiting passengers** next. *(This is the pattern ALL world entities must follow — see §7.)*
- **`Obstacle.cs`** — *(written, NOT yet wired)* trigger that calls `BusController.ApplyImpact` on `BusController` contact; optional `destroyOnHit`.
- **`TrafficSpawner.cs`** — *(written, NOT yet wired)* spawns/recycles obstacle prefabs ahead of the bus (`spawnAhead`, `lateralSpread`, `spawnInterval`, `maxActive`, ground-raycast seating). To wire: create the spawner + an obstacle prefab (e.g. a `raceCar*.fbx` placeholder), assign refs, tune `lateralSpread` to road width.

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

`Assets/Documentation/INTEGRATION_GUIDE.md` is the historical asset-pack rig recipe (hierarchy, layers `Player`/`Ground`/`Triggers`, Rigidbody/collider settings).

---

## 6. Tech Stack & Configuration

- **Engine:** Unity **6000.4.6f1**, URP `17.4.0`, Cinemachine `3.1.6` (installed, unused — `BusCameraFollow` is a custom script instead).
- **⚠️ ProBuilder (`com.unity.probuilder`) is REQUIRED.** The road pieces (`TrackCurved`, `RevisedTrack`) are ProBuilder meshes (`PolyShape` + `ProBuilderMesh`). If the package is missing, the roads don't render (they show as "missing script" + empty MeshFilters — exactly what happened when the project was first combined). Don't remove it. (See memory note `probuilder-roads`.) The bake tool (§4 Editor) can later convert them to static meshes to drop the runtime dependency.
- **Input:** `activeInputHandler = 2` ("Both"). Gameplay uses **legacy** `Input.*`. `PlayerInputActions.inputactions` is registered project-wide but unused (harmless yellow warning). Plan: keep legacy until 3-player multiplayer, then revisit the new Input System.
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

### Next up (not done)
Passenger economy & demand stops (replaces placeholder income) · driver stakes (traffic/health, potholes, police, poaching) · the two conductor roles · re-implement floating origin · bake roads when geometry is final · Spring Boot backend.

*Future session: §1 is design intent, not implemented fact. Verify any file/symbol named here still exists before relying on it — this is an early, fast-moving prototype.*
