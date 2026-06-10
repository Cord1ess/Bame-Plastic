# Conductor Networking — TODO (deferred from the gameplay pass)

The unified conductor/fare/passenger **gameplay** is done and works in single-player and for the local
player's role. What's left is making the **conductor actions sync across the 3 players** in a multiplayer
room. This is a dedicated subsystem — captured here so it can be built cleanly later.

## The model (per NETWORKING.md / BACKEND.md)
- **Driver's client is authoritative** for the bus AND the cabin/passengers.
- **Conductors send INTENTS, not state.** The driver applies them to the authoritative sim and **broadcasts
  the result**; all 3 clients converge.
- The world is **seed-deterministic**, so passengers spawn identically on every client.

## What's needed

### 1. A binary game-net layer (`GameNet` / `NetGameClient`)
- Sends/receives **binary** frames on the EXISTING WebSocket (`WebSocketNetworkService` already ignores
  non-`{` frames so the game layer can claim them — see `OnMessage`).
- First byte = message id (per the table in NETWORKING.md). Little-endian, quantized.

### 2. Deterministic passenger IDs
- Passengers must be identified the SAME way on every client. They spawn from the seeded pool, but **board/
  alight timing depends on the driver's bus position** (authoritative). Cleanest: the **driver assigns an id**
  when a passenger boards and includes it in the `BOARDED` broadcast; conductors map their local passenger to
  that id. (Pure client-side deterministic IDs are fragile because boarding is driver-timed.)

### 3. Message types (extend the NETWORKING.md table)
| Dir | Name | Payload |
|---|---|---|
| conductor → driver | `Intent_Grab` | passengerId |
| conductor → driver | `Intent_Throw` | (none) |
| conductor → driver | `Intent_Collect` | passengerId |
| conductor → driver | `Intent_Shove` | passengerId |
| driver → room | `PassengerBoard` | passengerId, spotIdx, isSeat |
| driver → room | `FareCollected` | passengerId, amount, bySlot |
| driver → room | `PassengerAlight` | passengerId |
| driver → room | `ConductorPose` | slot, cabin/world pos (for the avatars) |

### 4. Where to hook in (the gameplay is already structured for this)
- **Run the fare/passenger sim only on the DRIVER** (or make the conductor calls go through the net layer).
  Today every client runs it identically; for MP, the driver is the source of truth and others render.
- `InsideConductor.Collect()` → on a conductor client, send `Intent_Collect`; the driver runs the real
  `Passenger.Collect()` + `ShiftManager.AddEarnings` and broadcasts `FareCollected`.
- `Conductor.TryGrab/ThrowHeld()` → send `Intent_Grab/Throw`; driver applies + broadcasts.
- `Passenger` board/alight → driver broadcasts `PassengerBoard/PassengerAlight`; conductors apply to their
  local passenger of that id.
- Conductor avatars for the OTHER two players → interpolate `ConductorPose`.

### 5. Build order
1. `GameNet` binary codec + send/recv on the WS (extend `WebSocketNetworkService`).
2. Driver-assigned passenger ids + `PassengerBoard`/`PassengerAlight` broadcast (the simplest visible sync).
3. `Intent_Collect` → driver applies → `FareCollected` (the earnings sync — most important for competition).
4. `Intent_Grab/Throw/Shove`.
5. `ConductorPose` for the other players' avatars.

## Current state (so the next session knows)
- Gameplay (fare tiers, collect-to-earn, alight→pedestrian, overhead carry) is DONE + compiling.
- `RoleController.Apply` already enables exactly one local controller per role via `SetControlled`.
- No game-net layer exists yet; passengers have no network ids yet.
- See the memory note `conductor-fare-passenger.md` for the gameplay details.
