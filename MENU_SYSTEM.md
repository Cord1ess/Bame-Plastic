# Bame Plastic — Menu, Lobby & Settings

The front-end: **Main Menu → Lobby → Settings → Game**, all in the PixelUI theme, code-built, with every
multiplayer call routed through a swappable `INetworkService` (faked now, real Spring Boot WebSocket later).

## Flow

```
MainMenu.unity  (scene 0)
  Camera + EventSystem + MenuRoot[MenuController]
     MenuController builds one canvas; toggles three screens:
       ├── MainMenuScreen   PLAY ONLINE · PLAY SOLO · SETTINGS · QUIT
       ├── LobbyScreen      browser+code  ⇄  room view (3 role cards, ready, start)
       └── SettingsScreen   tabs: AUDIO · GRAPHICS · PLAYER · CONTROLS
                                   │
   PLAY SOLO ──────────────────────┤ BeginSoloShift()      → load BamePlastic.unity
   driver presses START (all ready) ┘ BeginMultiplayerShift → load BamePlastic.unity
```

`BamePlastic.unity` (scene 1) = gameplay, unchanged. The road reads `SessionContext.Seed` in play so the
shift world is deterministic (the MP promise) / fresh for solo.

## Key pieces

- **`SessionContext`** (DontDestroyOnLoad, `SessionContext.Instance`/`Ensure()`): owns the single
  `INetworkService` (the only place the concrete impl is chosen — `new StubNetworkService()`), pumps its
  `Tick`, and carries `IsMultiplayer / LocalRole / Seed / Room` into the game scene. `Bootstrap`
  (`RuntimeInitializeOnLoadMethod`) guarantees it exists from any start scene.
- **`SceneFlow`** — scene names + `GoToMenu()/GoToGame()`.
- **`SettingsStore`** — PlayerPrefs settings + apply (audio via an AudioMixer at `Resources/Audio/MainMixer`
  if present, else `AudioListener.volume` fallback; quality/fullscreen/resolution; player name).

## Networking seam (the important bit)

Everything the lobby does goes through **`INetworkService`** (`Assets/Scripts/Net/`):
`CreateRoom / RefreshRoomList / JoinRoom / ClaimRole / SetReady / StartShift / LeaveRoom`, plus events
`RoomJoined / RoomUpdated / RoomLeft / JoinFailed / RoomListUpdated / ShiftStarting(seed)`.

- **`StubNetworkService`** (now): in-memory fake. Generates `BAME-XXXX` codes, fabricates a browser list,
  and simulates bot players trickling in + readying on timers so the lobby feels live. Enforces the real
  rules below.
- **`WebSocketNetworkService`** (later): implements the SAME interface against Spring Boot's
  `/ws/session/{roomId}` (see `NETWORKING.md`). Drop-in — **no UI changes**. Swap the one line in
  `SessionContext.Ensure()`.

### Room rules (enforced in the service, honored by the UI)
- A room always has a **Driver** (the host / authoritative simulator later). Created room seats you as Driver.
- **Roles freely swappable** in the lobby: clicking a role SWAPS the local player with whoever's there
  (human/AI/empty) — a swap never empties a role, so Driver-mandatory holds automatically.
- Empty **conductor** seats become **AI** on start.
- **Everyone (humans) readies up → only the Driver can press START.** `RoomInfo.AllReady` gates it.
- Join: by **code** (direct) or from the **room browser** list. (Stub fakes both.)

## Data (`Net/NetworkTypes.cs`)
`Role { Driver, Conductor1, Conductor2 }` · `PlayerSlot {role,name,isLocal,isAI,ready}` ·
`RoomInfo {code,hostName,seed,slots[3]}` (helpers `Driver`, `AllReady`, `HumanCount`) · `RoomListing`.

## UI toolkit — PixelUI (see [pixel-ui memory] / the files)
- **`PixelUI`** — palette (dusk-slate dark panels + cream/gold accents), procedural cut-corner 9-slice
  sprites (point-filtered, no art), `Panel/Block/Label/BeveledBar/Canvas`. White was tried & rejected.
- **`PixelUIWidgets`** + **`PixelUIBehaviours`** — interactive: `Button` (hover/press + accent underline),
  `Toggle`, `Slider`, `Stepper` (◀ value ▶ — the pixel "dropdown"), `Input`, `Tabs`. Flat colour-swap
  states, NO gradients/tweens. Reuse these for any future UI (pause menu, dialogs).
- **`RoleCard`** — one lobby role card (title, glyph, occupant, ready ✔, YOU tag; whole card claims/swaps).

## Build order it was made in (each step verifiable)
1 widgets → 2 net stub+model → 3 SessionContext/SceneFlow/Bootstrap (+ scene to build settings) →
4 main menu → 5 settings → 6 lobby → 7 hand-off (seed into road) → 8 docs.

## What's stubbed / TODO later
- Real `WebSocketNetworkService` (NativeWebSocket → Spring Boot). The interface + all UI are ready for it.
- `LocalRole` is carried into the game but the game scene doesn't yet *use* it to pick which entity you
  control (driver vs conductor view) — that's the next gameplay-side wiring.
- Control **rebinding** (currently read-only display). AudioMixer asset (falls back to listener volume).
