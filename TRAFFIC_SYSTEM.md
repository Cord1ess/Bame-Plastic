# Bame Plastic — Traffic & Driver-Guide System

The chaotic Dhaka street: deterministic, pooled vehicles (rickshaws / cars / buses) that drive, avoid each
other and the bus, collide, and get shoved — plus the **DriverGuide**, the "eagle-vision" guide strip that
shows the optimal path through them. Companion to `ROAD_SYSTEM.md` (the road it all rides on).

> **Read this with `ROAD_SYSTEM.md`.** Traffic lives in the road's *road-relative* space (`metresFromBus`,
> `lateral`), so it inherits the road's seamlessness, floating-origin invariance, and determinism.

---

## TL;DR — how to use

Everything is auto-added by **Bame Plastic ▸ Create Tiled Road (Fast)**. On the `TiledRoad` object:
- `TrafficSystem` — spawns/drives the vehicles.
- `DriverGuide` (child) — the guide strip.

Press Play with the BusPlayer in the scene. Tune via the components' inspectors (knobs tables below).

For collisions to work, the bus needs its collision box: **Bame Plastic ▸ Bus ▸ Add Collision Box** (once;
size it to the bus, save the prefab). See `ROAD_SYSTEM.md` / the bus-collider note.

---

## Core concepts

### Logical vs physical (the foundational design)
Each `TrafficVehicle` has:
- **LOGICAL state** — `metresFromBus` (signed: + ahead of the bus, − behind), `lateral` (m right of road
  centre, − = the forward/your side under LHT), `speed`, `dir` (+1 same way as the bus, −1 oncoming). This
  is **road-relative**, so it's **deterministic** and **floating-origin-invariant** (a recenter changes
  nothing; `TrafficSystem.OnOriginShifted` is a deliberate no-op). The AI drives THIS.
- **PHYSICAL** — the transform, snapped each frame to the logical road point via `road.SampleRoad`. On a
  hit it gets a permanent shove (below).

### Determinism = multiplayer for free
Traffic spawns from a seeded RNG and reads only logical state → all clients generate **identical** traffic
with **zero sync**. Only the bus + passenger events + collision *outcomes* are authority-broadcast (the
driver). **Never** use unseeded `Random` or physics for traffic AI, or MP desyncs.

### "Ahead" is direction-relative
A vehicle's forward-along-road = `+dir`. Other is ahead of me when `(other.metres − my.metres) * dir > 0`.
Oncoming (dir −1) only blocks/dodges if it strays into the other's lateral band — so you only threaten
oncoming by going wrong-way into their lane.

---

## Components

### `TrafficSystem` (`Assets/Scripts/Gameplay/TrafficSystem.cs`)
Pooled spawner + driver of all vehicles. Each frame: tick every vehicle (avoidance), recycle ones off the
usable road, run the anti-stack separation pass, then top up the population.
- **Spawn ahead / cull behind:** vehicles spawn `spawnMinAhead`…(road edge) metres ahead (appear in the
  distance, drive toward you) and recycle at `cullBehind` m behind (camera can't look back). Spawn respects
  spacing (`minGap`/`sameLaneWidth`) so vehicles never stack at spawn.
- **Mix + cap:** `rickshawWeight`/`carWeight`/`busWeight`, hard `maxBuses` cap (a would-be 3rd bus
  downgrades to a car) — keeps the road threadable for the guide line.
- **Lateral by kind:** rickshaws hug the edge, buses toward the inner lane, cars mid, with jitter.
- `OnOriginShifted` = no-op (road-relative). `Live` (IReadOnlyList) exposes vehicles to the DriverGuide.

### `TrafficVehicle` (`Assets/Scripts/Gameplay/TrafficVehicle.cs`)
One pooled agent. `Tick(allVehicles, busSpeed, busLateral, dt)` does:
- **L2 avoidance** — senses the nearest obstacle ahead in its lane (vehicles + the bus, the bus at
  `metres=0, lateral=BusLateral`); **brakes** to a follow gap (down to a stop if boxed) and **steers**
  laterally to squeeze around, picking the side with more open road (`SideOpenness`). Surges into open
  gaps (speeds *above* cruise, scaled by `eagerness` — rickshaws boldest). Clamped to the drivable band
  per direction (never steers onto footpath/median).
- **L3 collision** — a root BoxCollider trigger (+ kinematic Rigidbody), enabled only within
  `colliderActiveRange` of the bus. `OnTriggerEnter/Stay` detects the bus by `BusTag` →
  `bus.ApplyImpact(busSpeedAfterHit)` + `ShiftManager.Damage(busDamageOnHit)` (gated by `hitCooldown`) →
  a **permanent jumpy shove**: the lateral + backward displacement is committed into the logical position
  (the pushed spot is the new position — no spring-back), plus a one-shot hop arc on the model. speed ×0.3
  jolt. Traffic↔traffic is NOT physics — a deterministic `TrafficSystem.SeparateOverlaps` nudges
  overlapping same-direction pairs apart.

### `DriverGuide` (`Assets/Scripts/Gameplay/DriverGuide.cs`)
The eagle-vision guide strip — the driver's skill externalised. Bus-attached, at roof height.
- **Model:** a flat, level **procedural mesh strip** ("GuideStrip" child) lying horizontal at `height`
  (~3 m, roof). Opaque URP/Unlit, double-sided, colour set directly via `_BaseColor` each frame
  (green=go → amber → red=blocked). *(Vertex colours and LineRenderer start/endColor both failed to show
  in-project; opaque + SetColor + flat mesh is the reliable combo. Transparent URP keywords were finicky
  and caused the strip to vanish — opaque fixed it.)*
- **Path = lateral-offset profile** (not a ray angle): for each forward vehicle, push the path to the side
  the **whole bus fits** (`band-edge − vehicle-edge − clearance − halfBusWidth ≥ 0`, using the bus
  collision-box width) across `[vehicle − leadIn … past it]` → it **swings out to overtake and tucks back**.
  If neither side fits → don't swing, flag blocked (so a closing gap stops being suggested immediately).
- **Within-lane only:** clamped to `[bandMin + halfBus, bandMax − halfBus]` = the forward drive lanes with
  the whole bus inside. Never suggests footpath/median/oncoming.
- **Root immobile, tip reactive:** `_lat[0]` pinned to the bus lateral, first ~15% held near it
  (`rootHold`), and the time-ease ramps 0.4→1 root→tip — near end rock-steady, far end whips. NaN-guarded
  so it can't collapse/vanish.
- **Steer assist:** a gentle tunable pull toward the path via `BusController.ApplySteerAssist`.
- Driver-only, local, **no MP sync** (it's a UI aid; the bus is driver-authoritative).

### `RivalBrain` + `RivalManager` (L4 — the competitive layer)
Marked, named **rival company buses** (distinct colour) that compete with you for fares.

**KEY DESIGN — rivals ARE traffic vehicles.** The first cut (a standalone `RivalBusAgent` with its own
motion) drifted "all over the place" because it reimplemented driving in a slightly-off coordinate space
and used world-space distances + a kinematic collider it teleported each frame. The fix: a rival is a
**normal `TrafficVehicle`** (bus kind) with a **`RivalBrain` (`TrafficVehicle.IVehicleBrain`)** attached.
The vehicle does ALL the driving — same road-relative motion against the bus, same curve-following, same
avoidance, same collision/shove, same band-clamp. The brain only adds *intent* via the `Decide(self, dt,
ref desiredSpeed, ref desiredLateral)` hook, which `TrafficVehicle.Tick` calls **after** its own avoidance
(so collision-avoidance is the floor and the rival plan layers on top). It can't go rogue — it lives in the
same deterministic space as every car.

- **`RivalBrain`** state machine: **Cruise → SeekStop → Camp → Leaving**. Cruise hunts a stop ahead
  (`stopSeekRange`) with waiting passengers via `SplineStopSpawner.TryGetStopAhead`, and drifts to block the
  player (`blockRange`); SeekStop eases `desiredLateral` to the kerb lane + slows to `approachSpeed`; Camp
  sets `desiredSpeed=0` and **steals** passengers (`ClaimWaitingPassengers`, `grabRate`/sec) → real fare to
  `RivalBus.AddEarnings`; leaves after `leaveAfter` grabbed or `maxCampTime`. The target stop is
  **re-queried every frame** (`RefreshStop`) so a floating-origin recenter never leaves it chasing a stale
  world point (the old cache bug).
- **`TrafficSystem.SpawnRival(name,color)`** acquires a bus vehicle, attaches the brain, adds it to `_live`
  AND `_rivals`. Rivals are **never pooled** — when one falls off the live road, `RepositionRivalAhead`
  re-deploys it ahead (keeps it competing all shift) instead of recycling. `Acquire` clears `Brain` so
  pooled NORMAL vehicles never inherit one.
- **`RivalManager`** (`[RequireComponent(TrafficSystem)]`, on the road object) just deploys each `RivalDef`
  (name+colour) once via `SpawnRival`, retrying until the traffic system is ready. No motion logic.
- **Standings link** — the brain finds/creates its `ShiftManager.rivals` entry, sets `drivenByAgent=true`
  so the simulated `RivalBus.Tick` is skipped and only REAL stolen fares count on the leaderboard.
- **Stop API on `SplineStopSpawner`:** `Instance`, `TryGetStopAhead(from,fwd,maxDist,out pos,out waiting)`,
  `ClaimWaitingPassengers(nearStopPos,maxCount)→totalFare`. `Passenger.Fare` getter added.
- **Collision:** a rival uses the standard `TrafficVehicle` trigger — hitting it behaves like hitting any
  ambient bus (impact + damage + shove), NOT a solid wall. The "block" is positional: it sits in your lane
  and you avoid it or eat the bump. (v1 — a true solid blocker can come later.)
- **v1 limitations:** overtake is passive (rivals cruise + camp ahead, no aggressive pass-and-cut). Tunables
  on `RivalBrain`: `stopSeekRange`, `campDistance`, `grabRate`, `leaveAfter`, `maxCampTime`, `approachSpeed`,
  `blockRange`.

### `FootpathPedestrians` (L5 — living crowds)
Pedestrians strolling the footpath who **convert into waiting fares** at stops — so a stop's crowd **grows
over time** as walkers arrive (timing matters: early = thin crowd, wait = bigger payoff but a rival may
camp it).
- Pooled **`Passenger`s in the new `Walking` state**, ridden **road-relative** on the footpath lateral
  (`(DriveHalf+RoadHalf)/2`), floating-origin-safe like traffic. `metres` advances vs the bus; `SampleRoad`
  places them so they follow curves. Some walk with the bus, some toward it.
- **BOTH footpaths** populated (`maxWalkersPerSide` each, balanced so both stay full). `side +1` = player/
  forward footpath (these join stops as fares); `side -1` = the far footpath (ambience only, never joins).
- **No pop-in:** spawn at `spawnMinAhead`=75…`spawnMaxAhead`=140m — BEYOND the visible-ahead range — so
  they're always already there when they come into view. Culled at `cullBehind`=14m (just off-camera behind),
  so they vanish unseen.
- On reaching a live stop (`joinRange`), hands off via **`SplineStopSpawner.TryJoinNearestStop`** → the
  walker re-parents to the road, becomes `Waiting`, joins the stop crowd (gathers to the curb if the bus is
  already crowding it). Capped at `crowdCap` per stop.
- `Passenger.BeginWalking(fare,col)` / `ConvertToWaiting(col)` added; `Walking` is a no-op in `Passenger.Update`
  (position is manager-driven). Stops now seed a **small** crowd (`crowdMin/Max` lowered to 3–8) so the
  growth is visible; raise them to skip the walker feed.
- Auto-added by **Create Tiled Road (Fast)**. Shares the one `PassengerPool` with the stops.
- Tunables: `maxWalkers`, `spawnMinAhead`/`spawnMaxAhead`/`cullBehind`, `walkSpeed`, `sameWayChance`,
  `lateralWander`; on the spawner: `joinRange`, `crowdCap`.

### Supporting API (on other scripts, for traffic/guide)
- `TiledRoadStreamer.SampleRoad(metresFromBus, lateral, …)` — world pos + frame, road-relative.
- `TiledRoadStreamer.SampleBand(forward, out min, out max)` — drivable lateral band per direction.
- `TiledRoadStreamer.BusLateral` — bus's lateral offset (traffic + guide read it).
- `TiledRoadStreamer.MetresAhead/MetresBehind`, `TileLength`.
- `BusController.SpeedMps` (signed), `BusController.ApplySteerAssist(bias)`, `BusController.BuildCollisionBox(...)`.
- `TrafficSystem.Live`.

---

## Tunables

### `TrafficSystem`
| Field | Default | Effect |
|---|---|---|
| `maxVehicles` | 8 | upper cap (spacing limits the real count) |
| `rickshaw/car/busWeight` | .65/.25/.10 | mix |
| `maxBuses` | 2 | hard cap on AI buses ahead |
| `rickshaw/car/busSpeed` (Vec2) | 5–8 / 10–16 / 8–13 | m/s ranges |
| `minGap` / `sameLaneWidth` | 20 / 2.5 | spawn spacing within a lane |
| `spawnMinAhead` / `cullBehind` | 70 / 18 | spawn-far / cull-near |

### `TrafficVehicle` (per-vehicle; kind-tuned in Acquire)
`followGap`, `lookahead` (30), `scratchClearance`, `eagerness` (rickshaw .8/car .6/bus .3),
`busSpeedAfterHit`, `busDamageOnHit`, `colliderActiveRange` (35), shove (`shoveLateral`/`shoveBack`/
`shoveHop`/`shoveTime`), `hitCooldown`.

### `DriverGuide`
| Field | Default | Effect |
|---|---|---|
| `length` / `segments` | 22 / 40 | strip length & smoothness |
| `height` | 3.0 | roof height (visibility) |
| `width` / `tipNarrow` | 1.0 / 0.6 | thin-wide strip taper |
| `scanRange` | 32 | how far ahead it plans |
| `clearance` | 1.6 | pass distance (smaller = tighter) |
| `leadIn` | 14 | how early the overtake swing starts (main "smooth overtake" dial) |
| `smoothPasses` | 4 | curve flowiness |
| `responsiveness` | 30 | reaction speed (0 = instant) |
| `steerAssist` | 0.3 | 0 = pure visual, 1 = sticky |

---

## Bug history (what we hit, so we don't repeat it)

| Symptom | Cause | Fix |
|---|---|---|
| Traffic teleports ~60 m periodically | sampled from the DISCRETE `_busTileIndex` (jumps on front-append / boundary) | continuous, smoothed `_busTileF` + append-compensation |
| Ghosting / snap-back | per-frame bus projection was noisy near tile seams | low-pass smooth `_busTileF` |
| Big connector gap after a while | `OnOriginShifted` shifted everything except `_carryPt` (road) | shift `_carryPt` too (road bug) |
| Cars stack / pass through | no avoidance (L1) | L2 avoidance + L3 separation pass |
| Knockback fell through ground / trash tumble | physics detach + free-fall | road-space animated shove (lateral + hop), then PERMANENT (no spring-back) |
| Guide line "joined sticks" | LineRenderer over coarse points | (later) flat mesh strip |
| Guide line speed-inconsistent / lagged | built in ROAD space | rebuilt in BUS space, anchored to the bus front |
| Guide invisible (on ground) | flat on the road, hidden behind the bus | lifted to roof `height`, level |
| Guide "doesn't work at all" / colours dead | URP/Unlit ignores vertex colours; LineRenderer start/endColor didn't show | OPAQUE URP/Unlit + `_BaseColor` set directly on a flat mesh |
| Guide suddenly disappears | finicky URP TRANSPARENT material flicker + possible NaN | opaque material + NaN guards |
| Guide stiff / went through cars / outside lane | ray-angle aim + straight-bias; no bus-size check | lateral-offset path; whole-bus-fits check; band clamp; root pinned/tip reactive |

---

## What's done vs remaining

**DONE:** L1 (kinematic both-way traffic) · L2 (avoidance: brake/steer/surge) · L3 (collisions + permanent
shove, bus box trigger, deterministic separation) · DriverGuide (reactive in-lane overtake strip) ·
**L4 (rival buses: camp stops + steal fares + block player + real standings)** ·
**L5 (footpath pedestrians that walk in and grow stop crowds; rivals & player compete for them).**

**The full traffic/passenger arc (L1–L5) is now in place — next is TUNING everything together.**

**REMAINING (polish, not blocking):**
- **L4 polish:** aggressive overtake-and-cut maneuver; rivals avoiding ambient traffic; a true solid block.
- **Visual polish:** real vehicle/pedestrian models (placeholder boxes/billboards), edge clutter (parked
  cars/shops), occasional wrong-way intruders.
- **Polish (anytime):** real vehicle models (currently placeholder boxes), edge clutter (parked cars/shops),
  occasional wrong-way intruders.

Related docs: `ROAD_SYSTEM.md` (road), `NETWORKING.md` (multiplayer plan), `PROJECT_UNDERSTANDING.md`.
