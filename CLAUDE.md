# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

A Unity 3D simulator for "programmable matter": particles (cubes) placed on a discrete
3D grid move and change state each simulation cycle according to declarative, per-type
behavior rules loaded from JSON at runtime. There is no physics engine involvement —
positions are integer grid coordinates (`Vector3Int`), and one discrete "cycle" advances
all particles simultaneously (like a cellular automaton), including conflict resolution
when multiple particles target the same cell.

Unity Editor version: **6000.3.15f1** (Unity 6). Render pipeline: URP.

## Commands

This is a Unity project with no CLI build/test scripts — development happens through the
Unity Editor.

- **Run the simulation**: open the project in Unity Editor 6000.3.15f1, open
  `Assets/Scenes/SampleScene.unity`, press Play. `SimulationController` auto-loads
  `Assets/StreamingAssets/scenario.json` and `behavior.json` on `Start()`.
- **Run tests**: `com.unity.test-framework` is installed but there is currently no
  `Tests/` assembly in the project. If you add tests, use Unity Test Runner
  (Window > General > Test Runner) with an EditMode/PlayMode asmdef, or run headless via
  `Unity.exe -runTests -projectPath <path> -testResults <path> -batchmode`.
- **Command-line build** (if needed): `Unity.exe -batchmode -quit -projectPath <path> -executeMethod <BuildMethod>`.

## Architecture

The simulation logic was deliberately split out of a single monolithic `SimulationManager`
(see `Assets/Scripts/ParticleSim/SimulationManager.cs` — now fully commented out, kept only
as historical reference) into five single-responsibility modules under
`Assets/Scripts/ParticleSim/`. The dependency direction flows one way; UI is the only layer
allowed to know about everything else:

- **`Core/`** — Plain C# data types with no MonoBehaviour/engine dependency: `Particle`
  (id, type, grid position, arbitrary internal state dict), `ScenarioData`/`ParticleData`
  (scenario JSON shape), `BehaviorData` (`TypeBehavior`/`ActionBlock`, behavior JSON shape),
  `BoundaryMode` enum (`None`/`Clamp`/`Wrap`), and `IGridQuery` — the interface the engine
  uses to query grid occupancy/boundary without depending on `ParticleManager` concretely.
- **`InputValidation/`** — `ScenarioLoader` and `BehaviorLoader`: read + `Newtonsoft.Json`
  deserialize the two JSON files, validating required fields (e.g. `gridSize` must have 3
  values) and returning `null` on failure. Nothing else in the codebase reads these files
  directly.
- **`ParticleManager/`** — Owns the particle population and spatial index
  (`Dictionary<Vector3Int, Particle>`). Builds particles from a validated `ScenarioData`,
  answers `IGridQuery` (`TryGetParticleAt`, `ApplyBoundary`), and resets particles to their
  initial state. Knows nothing about GameObjects, rendering, or the DSL.
- **`SimulationEngine/`** — The stepping engine. Each `Step()`: (1) evaluates every
  particle's `onStep` action blocks to compute desired positions, (2) resolves collisions
  (two particles can't target the same cell; a particle can't move into a cell held by a
  non-mover; resolution repeats until stable), (3) applies final positions and fires events
  (`OnParticleMoved`, `OnColorChanged`, `OnCycleCompleted`, `OnSimulationFinished`) instead
  of touching visuals directly. Stops when `MaxCycles` is hit or `IdleCyclesLimit`
  consecutive cycles produce no movement. Also contains the declarative-language
  interpreter:
  - `ConditionEvaluator` — parses `hasNeighbor('X+'|'X-'|'Y+'|'Y-'|'Z+'|'Z-')`,
    `checkState('key', '==', value)`, boolean `And`/`Or`/`Not` combinators (case-insensitive,
    left-to-right, no parentheses grouping).
  - `MoveAction.TryParse` — `move('X+'|...)`; ignored during `onInit` (init-time move
    actions never displace a particle).
  - `SetStateAction.TryParse` — `setState('key', value)`, value coerced to bool/double/string.
  - `SetColorAction.TryParse` — `setColor('#RRGGBB')` via `ColorUtility`.
- **`MetricsCalculator/`** — Subscribes to `SimulationEngine` events to accumulate
  `TotalMovements`/`CurrentCycle`; `SimulationTime` is ticked externally once per frame.
- **`UI/`** — The only layer allowed to depend on everything else, including MonoBehaviour
  and rendering:
  - `SimulationController` — the single orchestrator MonoBehaviour. Owns instances of
    `ParticleManager`, `SimulationEngine`, `MetricsCalculator`; wires engine events to
    `ParticleView` updates; drives the load → build particles/views → `ExecuteOnInit` →
    optional autostart flow; exposes `Run`/`Pause`/`ResetSimulation`.
  - `SimulationUIController` — wires UI Toolkit (`SimulationUI.uxml`/`.uss`/`.tss` under
    `Assets/UI/Simulation/`) buttons (Run/Pause/Stop, scenario/behavior file pickers via
    `StandaloneFileDialog`, fullscreen, quit) to `SimulationController`.
  - `ParticleView` — per-particle cube MonoBehaviour; animates grid-to-world position
    transitions and applies color changes.
  - `GridBoundsRenderer` — draws the wireframe grid outline; `FlyCameraController` — free
    camera for inspecting the simulation.

When modifying simulation behavior, prefer changing `SimulationEngine`/`ConditionEvaluator`/
action parsers over `SimulationController` — the controller should stay a thin wiring layer.
Never add a dependency from `Core`, `ParticleManager`, or `SimulationEngine` back onto `UI`.

### Scenario / behavior JSON format

Loaded from `Assets/StreamingAssets/scenario.json` and `behavior.json` (filenames
configurable per-`SimulationController`, also swappable at runtime via the UI file pickers).

`scenario.json`: `gridSize: [x,y,z]`, `maxCycles` (0 = uncapped), `idleCyclesLimit` (0 =
disabled, default 5), `particles: [{ id, type, position: [x,y,z] }]`.

`behavior.json`: map of particle `type` -> `{ onInit: [ActionBlock], onStep: [ActionBlock] }`,
where each `ActionBlock` is `{ condition?: string, actions: string[] }`. An absent/empty
`condition` always evaluates true.

### Notable non-obvious things

- `Assets/Scripts/ParticleSim/SimulationManager.cs` is entirely commented out — it's the
  pre-refactor monolith kept for reference, not dead code to silently delete without checking
  whether anything still points at it (nothing currently does).
- `Assets/_Recovery/` is Unity Editor crash-recovery output, not project source.
- Several source comments/log strings are in Portuguese (module docs describe the intended
  module boundaries in detail — read them before restructuring).
