# 🎮 COMBINED KART & PROCEDURAL GENERATION INTEGRATION GUIDE

Welcome to your unified project! This directory contains everything needed to drive a high-fidelity arcade Kart through an infinitely spawning procedural track.

---

## 📂 Combined Folder structure

The project files have been neatly organized into these subfolders:

- `Scripts/Core/` - Contains the `BusController.cs`, `BusTag.cs`, and `ExtensionMethods.cs`.
- `Scripts/Generation/` - Contains the procedural map generation scripts: `LevelLayoutGenerator.cs`, `LevelChunkData.cs`, `TriggerExit.cs`, and `FloatingOrigin.cs`.
- `Scripts/Optional/` - Extra scripts: `DayNightController.cs`, `SimpleCameraController.cs`, and `EventManager.cs`.
- `Prefabs/LevelPieces/` - Contains the road segment prefabs that will spawn dynamically.
- `Settings/LevelChunkData/` - Scriptable Object assets defining entrance/exit directions for each segment.
- `Models/Kart/` & `Models/FBX format/` - Visual models for the Kart, wheels, roads, and landscape objects.
- `Materials/` & `Images/` - Complete visual assets, texture particles, and drift effects. (Audio and Post-Processing removed for a cleaner project).

---

## 🛠️ Step-by-Step Unity Setup

Follow these steps to create your combined game scene:

### 1. The Environment & Managers
1. Create a new 3D Scene called `CombinedGame` and save it inside `Assets/Combined/Scene/`.
2. Create an empty GameObject named **`Managers`** at position `(0, 0, 0)`.
3. Create a child of `Managers` called **`LevelGenerator`**.
   * Add the `LevelLayoutGenerator` script.
   * **First Chunk**: Drag `LevelBlock S To North.prefab` (from `Assets/Combined/Prefabs/LevelPieces/`) into your scene at `(0,0,0)`, and drag it into this field.
   * **Level Chunk Data**: Expand the array to size `5` and assign the five asset files from `Assets/Combined/Settings/LevelChunkData/` (e.g. `West North.asset`, `South West.asset`, etc.).
   * **Chunks To Spawn**: Set to `8` or `10`.
   * **Spawn Origin**: `(0, 0, 0)`.

### 2. Player Kart Hierarchy
Create an empty GameObject in the Hierarchy named **`Kart`** at position `(0, 1.5, 0)` and construct this exact hierarchy:

```
Kart (Root GameObject)
├── KartModel (Empty GameObject, Position: 0, -0.4, 0)
│   ├── Kart_Visual (Drag the model from Combined/Models/Kart/Kart.FBX here)
│   │   ├── FrontWheels (Group visual front wheels under here)
│   │   ├── BackWheels (Group visual back wheels under here)
│   │   └── SteeringWheel (Assign steering wheel visual)
│   ├── Tube001 (Left Exhaust particle system)
│   └── Tube002 (Right Exhaust particle system)
├── KartNormal (Empty GameObject)
├── Sphere (Empty GameObject)
│   ├── Rigidbody (Actual physics body)
│   └── Sphere Collider
├── WheelParticles (Empty GameObject)
│   ├── LF_Sparks (Particle system)
│   ├── RF_Sparks (Particle system)
│   ├── LB_Sparks (Particle system)
│   └── RB_Sparks (Particle system)
└── FlashParticles (Empty GameObject)
    ├── SparksFlash_L (Particle system)
    └── SparksFlash_R (Particle system)
```

### 3. Component Configuration
1. **On `Kart` (Root)**:
   * Add a `Rigidbody`: `Mass = 1`, `Drag = 0.05`, `Angular Drag = 0.5`, **Freeze Rotation X and Z**, and **Use Gravity**.
   * Add a `Capsule Collider`: `Radius = 0.6`, `Height = 1.8`, `Center = (0, 0.4, 0)`.
   * Add `BusTag` script component.
   * Add `BusController` script component.
2. **On `Kart/Sphere`**:
   * Add a `Rigidbody`: `Mass = 1`, `Drag = 0.1`, `Angular Drag = 0.05`, **Use Gravity**.
   * Add a `Sphere Collider`: `Radius = 0.5`.
3. **In the `BusController` component**:
   * Drag your GameObjects from the hierarchy into their respective slots:
     * **Kart Model** ➡️ `Kart/KartModel`
     * **Kart Normal** ➡️ `Kart/KartNormal`
     * **Sphere** ➡️ `Kart/Sphere`
     * **Front/Back/Steering Wheels** ➡️ Your respective visual mesh objects
     * **Wheel Particles** ➡️ `Kart/WheelParticles`
     * **Flash Particles** ➡️ `Kart/FlashParticles`
     * **Turbo Colors**: Array size `3` (Red, Orange, Yellow)
     * **Layer Mask**: Select **Ground** layer (create one if needed)

### 4. Camera & Core Settings
1. **Setup Layers**:
   * Create layers named `Player`, `Ground`, and `Triggers` in Project Settings.
   * Assign the **Kart Root** to `Player`, **Road chunks** to `Ground`, and **Exit triggers** to `Triggers`.
   * Ensure `BusController` LayerMask is set to target `Ground`.
2. **Main Camera (Smooth Custom Follow)**:
   * Position the camera behind the kart, e.g., `(0, 3, -6)` with a slight downward rotation `(15, 0, 0)`.
   * To achieve the smooth follow transition from the procedural system, make the `MainCamera` a child of **`Kart/KartNormal`** (or attach your preferred smooth camera follow script).
   * Add the `FloatingOrigin` script to your camera. Assign the **Layout Generator** field to your `LevelGenerator` GameObject. Set **Threshold** to `150`.

---

## 🎮 How the Systems Integrate
- **The Trigger Mechanism**: As your Kart moves, it drives through the invisible colliders at the end of each chunk.
- **The Event**: The `TriggerExit` script on the chunk detects the `BusTag` component on your Kart, triggering the `OnChunkExited` event.
- **The Spawning**: The `LevelLayoutGenerator` receives this event, spawns a matching chunk ahead, and deactivates the chunk you just exited after a 5-second delay to keep performance pristine!

Enjoy building your infinite driving game! 🚀
