# Roadside Buildings — Implementation Handoff

> Context for a FRESH chat to build the roadside-building system against a NEW (city-targeted) building pack.
> The previous POLYGON-pack attempt + its `BuildingSpawner` were REMOVED (clean). Everything below is the
> distilled "what worked / what to do / the gotchas" so you don't rediscover it.

---

## The goal (user's exact requirements)

A streamed building wall down **both sides** of the endless road so it doesn't look like a floating strip:

1. **Layout, in order from the road outward:** lanes → footpath → **a small dirt gap** → **buildings, in a
   serial line, one after another** (NOT scattered, NOT random positions). Fronts face the road, on a
   straight back-line, bases planted on the ground.
2. **Building types:** city buildings — **apartment blocks, large towers, shops, commercial**. **NO houses,
   NO pitched-roof buildings.** (User is importing a new pack that's city-targeted, so this may be moot, but
   keep the option to filter.)
3. Must **stream endlessly** with the road, **not pop in** (the smog hides the spawn edge), and never break
   the floating-origin / pooling correctness.

---

## The approach that's correct here (decided + validated)

**HYBRID: imported building MODELS + our own streaming PLACEMENT.**
- ❌ NOT procedural building geometry (huge effort, mediocre look).
- ❌ NOT dropping a city-pack's prebuilt static scene (fights the endless streaming road).
- ✅ Pool the pack's building prefabs and stream them along the road with a spawner that mirrors the existing
  `SplineStopSpawner` / `FootpathPedestrians` pattern.

---

## The road API to build against (all on `TiledRoadStreamer`, Assets/Scripts/Generation/)

The road is **endless, pooled tiles, road-relative, floating-origin-safe**. Use these (they're proven):

- `bool SampleRoad(float metresFromBus, float lateral, out Vector3 pos, out Vector3 fwd, out Vector3 right)`
  — THE key call. Given a signed distance along the road relative to the bus (+ = ahead) and a lateral offset
  (m right of centre; **negative = the forward/your side under LHT**), returns world pos + frame. Follows
  curves, floating-origin-invariant. Place buildings with this every frame.
- `float MetresAhead` / `float MetresBehind` — how much usable road exists ahead/behind the bus right now.
- `float TileLength` (60), `RoadZone Zone` (the cross-section).
- `System.Action<Vector3> OnForwardAdvanced` — fires when a new section streams in (SplineStopSpawner uses
  this; the building spawner used a per-frame cursor instead — either works).
- `bool TryGetLeadFrame(out pos, fwd, right)` — the leading edge frame.

### `RoadZone` (Assets/Scripts/Generation/RoadZone.cs) — the cross-section widths (METRES)
- `laneWidth` 6.5, `medianWidth` 1, `footpathWidth` 4, `groundWidth` 22, `leftHandTraffic` true.
- Computed half-widths (from centre): `MedianHalf`, `DriveHalf` (lane outer edge), `RoadHalf` (= footpath
  outer edge), **`GroundHalf`** (= RoadHalf + groundWidth, the outer edge of the ground shoulder).
- **Buildings go at lateral `±(GroundHalf + dirtGap)`** — i.e. past the footpath + the dirt-gap shoulder.

---

## The proven spawner pattern (copy this — it's how stops/pedestrians/traffic all work)

A `MonoBehaviour` `[RequireComponent(typeof(TiledRoadStreamer))]`, **PLAY-ONLY** (see Robustness below):
- In `Start()` (play only): make a child container `_parent` under the road object; build the usable prefab
  list.
- Per-object state is **bus-relative**: `float metres` = distance ahead of the bus. Static objects decay it
  each frame: `metres -= busSpeed * Time.deltaTime` (busSpeed = `BusController.Instance.SpeedMps`).
- Each frame: recycle anything with `metres < -cullBehind`; for the rest, re-place world pos via
  `SampleRoad(metres, lateral)` (so it rides curves + floating origin).
- **POOL by prefab** (Dictionary<int, Stack<GameObject>>), Instantiate on miss, SetActive(false)+push on
  recycle.
- Spawn ahead up to a `spawnAhead` (~220m, beyond the smog) so nothing pops in.
- `public void OnOriginShifted(Vector3 delta) {}` — no-op (road-relative; objects parented to the road move
  with the scene shift automatically).

### SERIAL placement (the part the first attempt got wrong — buildings were scattered)
- Keep a **cursor** = the front edge (in `metres`) of the last-placed building.
- For the next building: pick a prefab, **measure its real world width**, place its CENTRE at
  `cursor + width/2`, then `cursor += width + buildingGap`. → edge-to-edge serial wall, no overlap/gaps.
- **Straight back-line:** all FRONTS at lateral `GroundHalf + dirtGap`; the building's centre lateral is
  `frontLateral + depth/2` (so fronts align regardless of building depth).
- **Plant the base on the ground:** measure the prefab's bounds; lift by `-(boundsMin.y at unit scale)*scale`
  so the mesh bottom meets the road Y (Synty pivots are often not at the base).
- Orient: `face = -fwd * side` (front toward the road), `LookRotation(face, up)`.

### Sizing & filtering (robust to any pack's authored scale)
- **Measure each prefab ONCE** via `GetComponentsInChildren<Renderer>().bounds`, then **divide out the
  prefab's own `localScale`** to get per-unit height/width/depth. (Synty POLYGON prefabs were authored at
  ~100× scale — measuring makes scale irrelevant.)
- Scale uniformly to a random target height (`heightRange`, e.g. 14–30m).
- **No-houses filter (if needed):** keep a prefab only if `height/min(width,depth) >= minTallness` (~0.9 →
  tall apartments/towers) OR its name reads commercial (shop/store/market/mall). Pitched-roof houses are
  short+wide → filtered. **Safety:** if the filter leaves <4 usable, fall back to all (avoid empty street).
  (The new city-pack may be all-towers already → filter may be unnecessary; keep it as a knob.)

### Tunables to expose
`dirtGap`, `buildingGap`, `heightRange`, `minTallness`, `spawnAhead` (~220), `cullBehind` (~50).

---

## ⚠️ Robustness rules (HARD-LEARNED — do not violate)

These caused days of pain ("UI/objects embed into the scene, never disappear, must delete ScriptAssemblies").

1. **NEVER `[ExecuteAlways]` on anything that CREATES GameObjects.** Edit-mode object creation gets
   baked/orphaned into the scene. The spawner must be **play-only** (plain MonoBehaviour; build in `Start`,
   tear down in `OnDestroy`). `[ExecuteAlways]` is ONLY for components that *modify* existing things
   (Billboard, DayNightController, BusCameraFollow) or the road preview (whose tiles are
   `HideFlags.DontSaveInEditor|DontSaveInBuild`).
2. **There is a self-healing cleaner** — `TransientUICleaner` (Assets/Scripts/Core) + its editor companion
   (Assets/Editor/TransientUICleanerEditor.cs) strip orphaned runtime objects on scene open/save by NAME.
   **If the new spawner's container has a stable name (e.g. "Buildings"), ADD that name to
   `TransientUICleaner.OrphanNames`** so a stray one can never persist.
3. **Imported materials are usually built-in Standard shader → render PINK in URP.** Run
   *Edit ▸ Rendering ▸ Materials ▸ Convert All Built-in Materials to URP* after importing the pack.
4. Pooled objects parent under a child of the road object; recompute their transforms from `SampleRoad`
   each frame — do NOT cache world positions across a floating-origin recenter.

---

## Wiring (how the old one hooked in — replicate)

- `Assets/Editor/CreateTiledRoad.cs` (menu **Bame Plastic ▸ Create Tiled Road (Fast)**) builds the road
  object with RoadZone + TiledRoadStreamer + SplineStopSpawner + FootpathPedestrians + TrafficSystem +
  RivalManager + a DriverGuide child. **Add the new BuildingSpawner here** (`go.AddComponent<…>()`), and
  optionally an editor helper that auto-fills its prefab list from the new pack's prefab folder
  (`AssetDatabase.FindAssets("t:Prefab", new[]{ "<pack prefabs dir>" })`), filtering to the city buildings.
- Also add a menu like **"Bame Plastic ▸ Add or Refresh Buildings On Road"** to apply it to the EXISTING
  road in the open scene without recreating it (the old one did this).
- Existing game scene: `Assets/Scenes/BamePlastic.unity` (the ONLY scene; it opens as a living menu, then
  transitions to gameplay — see the menu/atmosphere systems).

---

## Smog (already done — buildings benefit from it)

`DayNightController` runs LINEAR distance fog (`smogStart`~45m, `smogEnd`~180m) + a horizon "Fog Ring" mesh
(Assets/Resources/FogRing) = polluted smog that **hides the building spawn edge**, so buildings fade in with
no pop-in. Set `spawnAhead` beyond `smogEnd` and it's seamless.

---

## When the new pack is imported, the fresh chat should:
1. Find the pack folder + its building prefabs; check the material shader (convert to URP if Standard).
2. Write a play-only, pooled `BuildingSpawner` per the pattern above (serial wall, measured width/base, back-
   line at `GroundHalf + dirtGap`, both sides).
3. Wire it into `CreateTiledRoad.cs` + an "add to existing road" menu; auto-seed the prefab list from the pack.
4. Add the container name to `TransientUICleaner.OrphanNames`.
5. Tell the user to URP-convert + run the menu, then tune `dirtGap`/`buildingGap`/`heightRange`.
