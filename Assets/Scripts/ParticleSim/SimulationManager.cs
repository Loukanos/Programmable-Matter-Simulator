// using System.Collections.Generic;
// using System.IO;
// using UnityEngine;
// using ParticleSim.Core;
// using ParticleSim.InputValidation;
// using ParticleSim.SimulationEngine;
// using ParticleSim.UI;
//
// namespace ParticleSim
// {
//     /// <summary>
//     /// How particle positions behave when they reach the edge of the grid.
//     /// </summary>
//     public enum BoundaryMode
//     {
//         None,   // particles may leave the grid freely
//         Clamp,  // particles stop at the grid edge
//         Wrap    // particles wrap around to the opposite side
//     }
//
//     /// <summary>
//     /// Drives the whole simulation:
//     /// loads the scenario and behavior files, spawns the particles and
//     /// advances the simulation in discrete cycles, executing the onStep
//     /// event (move actions only) for every particle on each cycle.
//     /// </summary>
//     public class SimulationManager : MonoBehaviour
//     {
//         [Header("Data files (relative to StreamingAssets)")]
//         public string scenarioFileName = "scenario.json";
//         public string behaviorFileName = "behavior.json";
//
//         [Header("Simulation")]
//         [Tooltip("Seconds between discrete simulation cycles.")]
//         public float stepInterval = 1f;
//         public bool autoStart = true;
//         public BoundaryMode boundaryMode = BoundaryMode.Wrap;
//
//         [Header("Visual")]
//         public float cellSize = 1f;
//         [Range(0.1f, 1f)]
//         public float cubeFill = 0.9f;
//         public Color particleColor = new Color(0.2f, 0.8f, 1f);
//         public Shader particleShader;
//         public bool frameCameraOnStart = true;
//         [Tooltip("Seconds each cube takes to slide between cells. Should be <= stepInterval.")]
//         public float moveAnimationDuration = 0.25f;
//
//         [Header("Grid bounds")]
//         [Tooltip("Draw a thin wireframe outline showing the grid limits.")]
//         public bool showGridBounds = true;
//         public Color gridBoundsColor = new Color(0.5f, 0.9f, 1f, 0.5f);
//         [Tooltip("Thickness of the grid outline lines, in world units.")]
//         public float gridBoundsLineWidth = 0.04f;
//
//         private GridBoundsRenderer _gridBounds;
//
//         [Header("Floor")]
//         [Tooltip("Draw a floor plane at the bottom of the grid.")]
//         public bool showFloor = true;
//         public Color floorColor = new(0.18f, 0.18f, 0.22f);
//         [Tooltip("Extra size added around the grid footprint, in cells.")]
//         public float floorMargin = 1f;
//
//         private GameObject _floor;
//         private Material _floorMaterial;
//
//         private ScenarioData _scenario;
//         private Dictionary<string, TypeBehavior> _behaviors;
//         private readonly List<Particle> _particles = new List<Particle>();
//         private readonly Dictionary<Particle, ParticleView> _particleViews = new();
//
//         private Vector3Int _gridSize;
//         private float _timer;
//         private bool _running;
//         private Transform _particlesRoot;
//         private Material _sharedMaterial;
//
//         private Dictionary<Vector3Int, Particle> _gridLookup = new();
//
//         private int _maxCycles;
//         private int _idleCyclesLimit;
//         private int _cycleCount;
//         private int _idleCount;
//         private bool _finished;
//
//         private int _totalMovements;
//         private float _simulationTime;
//
//         /// <summary>True once a valid scenario + behavior set has been loaded.</summary>
//         public bool HasData => _scenario != null && _behaviors != null;
//
//         /// <summary>Total moves made by all particles since start.</summary>
//         public int TotalMovements => _totalMovements;
//
//         /// <summary>Elapsed simulation time in seconds.</summary>
//         public float SimulationTime => _simulationTime;
//
//         /// <summary>Current cycle count.</summary>
//         public int CycleCount => _cycleCount;
//
//         /// <summary>True while the simulation is actively stepping.</summary>
//         public bool IsRunning => _running;
//
//         /// <summary>True once a stop criterion was reached (cubes held in place).</summary>
//         public bool IsFinished => _finished;
//
//         private void Start()
//         {
//             string scenarioPath = Path.Combine(Application.streamingAssetsPath, scenarioFileName);
//             string behaviorPath = Path.Combine(Application.streamingAssetsPath, behaviorFileName);
//             LoadSimulation(scenarioPath, behaviorPath, autoStart);
//         }
//
//         private void Update()
//         {
//             if (!_running)
//                 return;
//
//             _simulationTime += Time.deltaTime;
//             _timer += Time.deltaTime;
//             if (_timer >= stepInterval)
//             {
//                 _timer -= stepInterval;
//                 Step();
//             }
//         }
//
//         // ---------- Loading ----------
//
//         /// <summary>
//         /// Loads scenario + behavior from the given absolute file paths, rebuilds
//         /// all particles and resets the simulation. On failure the previous
//         /// simulation is left untouched and the method returns false.
//         /// </summary>
//         public bool LoadSimulation(string scenarioPath, string behaviorPath, bool startRunning = false)
//         {
//             ScenarioData scenario = ScenarioLoader.LoadFromFile(scenarioPath);
//             Dictionary<string, TypeBehavior> behaviors = BehaviorLoader.LoadFromFile(behaviorPath);
//
//             if (scenario == null || behaviors == null)
//             {
//                 Debug.LogError("[SimulationManager] Load failed: scenario or behavior could not be parsed. Keeping current simulation.");
//                 return false;
//             }
//
//             _scenario = scenario;
//             _behaviors = behaviors;
//
//             _gridSize = new Vector3Int(
//                 _scenario.gridSize[0],
//                 _scenario.gridSize[1],
//                 _scenario.gridSize[2]);
//
//             _maxCycles = _scenario.maxCycles;
//             _idleCyclesLimit = _scenario.idleCyclesLimit;
//
//             ClearParticles();
//             BuildParticles();
//             ExecuteOnInit();
//             BuildGridBounds();
//             //BuildFloor();
//
//             _cycleCount = 0;
//             _idleCount = 0;
//             _timer = 0f;
//             _totalMovements = 0;
//             _simulationTime = 0f;
//             _finished = false;
//             _running = startRunning;
//
//             if (frameCameraOnStart)
//                 FrameCamera();
//
//             Debug.Log($"[SimulationManager] Loaded grid {_gridSize} with " +
//                       $"{_scenario.particles.Count} particle(s) and " +
//                       $"{_behaviors.Count} behavior type(s). " +
//                       $"maxCycles={_maxCycles}, idleCyclesLimit={_idleCyclesLimit}.");
//             return true;
//         }
//
//         // ---------- Spawning ----------
//
//         private void ClearParticles()
//         {
//             _particles.Clear();
//             _particleViews.Clear();
//
//             if (_particlesRoot != null)
//             {
//                 Destroy(_particlesRoot.gameObject);
//                 _particlesRoot = null;
//             }
//         }
//
//         private void BuildGridBounds()
//         {
//             if (_gridBounds != null)
//             {
//                 Destroy(_gridBounds.gameObject);
//                 _gridBounds = null;
//             }
//
//             if (!showGridBounds)
//                 return;
//
//             var boundsGo = new GameObject("GridBounds");
//             boundsGo.transform.SetParent(transform, false);
//             _gridBounds = boundsGo.AddComponent<GridBoundsRenderer>();
//             _gridBounds.Build(_gridSize, cellSize, gridBoundsColor, gridBoundsLineWidth);
//         }
//
//         private void BuildFloor()
//         {
//             if (_floor != null)
//             {
//                 Destroy(_floor);
//                 _floor = null;
//             }
//
//             if (!showFloor)
//                 return;
//
//             // Plane primitive is 10x10 units, centered, lying on the XZ plane.
//             _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
//             _floor.name = "Floor";
//             _floor.transform.SetParent(transform, false);
//             
//             //float width = (_gridSize.x + floorMargin * 2f) * cellSize;
//             //float depth = (_gridSize.z + floorMargin * 2f) * cellSize;
//             _floor.transform.localScale = new Vector3(500f, 1f, 500f);
//
//             // Sit the floor right at the bottom edge of the grid bounds.
//             float centerX = (_gridSize.x - 1) * 0.5f * cellSize;
//             float centerZ = (_gridSize.z - 1) * 0.5f * cellSize;
//             float bottomY = -0.5f * cellSize;
//             _floor.transform.position = new Vector3(centerX, bottomY, centerZ);
//
//             _floorMaterial = CreateFloorMaterial();
//             _floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
//         }
//
//         private Material CreateFloorMaterial()
//         {
//             Shader shader = Shader.Find("Universal Render Pipeline/Lit");
//             if (shader == null)
//                 shader = Shader.Find("Standard");
//
//             var mat = new Material(shader);
//             if (mat.HasProperty("_BaseColor"))
//                 mat.SetColor("_BaseColor", floorColor);
//             if (mat.HasProperty("_Color"))
//                 mat.SetColor("_Color", floorColor);
//             return mat;
//         }
//
//         private void BuildParticles()
//         {
//             _particlesRoot = new GameObject("Particles").transform;
//             _particlesRoot.SetParent(transform, false);
//
//             _sharedMaterial = CreateParticleMaterial();
//
//             foreach (ParticleData pd in _scenario.particles)
//             {
//                 if (pd.position == null || pd.position.Length < 3)
//                 {
//                     Debug.LogWarning($"[SimulationManager] Particle {pd.id} has an invalid position, skipped.");
//                     continue;
//                 }
//
//                 GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
//                 cube.transform.SetParent(_particlesRoot, false);
//                 cube.transform.localScale = Vector3.one * cellSize * cubeFill;
//
//                 var renderer = cube.GetComponent<Renderer>();
//                 renderer.sharedMaterial = _sharedMaterial;
//
//                 var gridPos = new Vector3Int(pd.position[0], pd.position[1], pd.position[2]);
//
//                 var particle = new Particle(pd.id, pd.type, gridPos);
//                 var particleView = cube.AddComponent<ParticleView>();
//                 particleView.Bind(particle, cellSize, moveAnimationDuration);
//
//                 _particles.Add(particle);
//                 _particleViews[particle] = particleView;
//             }
//         }
//
//         /// <summary>
//         /// Executes the onInit event for all particles.
//         /// Currently, move actions are ignored in this event.
//         /// </summary>
//         private void ExecuteOnInit()
//         {
//             UpdateGridLookup();
//             foreach (Particle particle in _particles)
//             {
//                 if (_behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
//                 {
//                     ProcessActions(particle, behavior.onInit, isOnInit: true);
//                 }
//             }
//         }
//
//         private Material CreateParticleMaterial()
//         {
//             Shader shader = particleShader;
//
//             if (shader == null)
//             {
//                 // Fallback logic for Editor convenience, but Serialized Field is preferred for builds
//                 shader = Shader.Find("Universal Render Pipeline/Lit");
//                 if (shader == null)
//                 {
//                     shader = Shader.Find("Standard");
//                     if (shader == null)
//                     {
//                         Debug.LogError("[SimulationManager] CRITICAL: No shader assigned and fallbacks failed. Particles will be pink.");
//                         shader = Shader.Find("Hidden/InternalErrorShader");
//                     }
//                 }
//             }
//
//             var mat = new Material(shader);
//             if (mat.HasProperty("_BaseColor"))
//                 mat.SetColor("_BaseColor", particleColor);
//             if (mat.HasProperty("_Color"))
//                 mat.SetColor("_Color", particleColor);
//             return mat;
//         }
//
//         // ---------- Simulation cycle ----------
//
//         /// <summary>Advances the simulation by exactly one discrete cycle.</summary>
//         public void Step()
//         {
//             if (_finished)
//                 return;
//
//             UpdateGridLookup();
//
//             // Phase 1: Calculate desired positions for all particles
//             var desiredPositions = new Dictionary<Particle, Vector3Int>();
//             foreach (Particle particle in _particles)
//             {
//                 if (_behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
//                 {
//                     desiredPositions[particle] = ProcessActions(particle, behavior.onStep, isOnInit: false);
//                 }
//                 else
//                 {
//                     desiredPositions[particle] = particle.GridPosition;
//                 }
//             }
//
//             // Phase 2: Detect conflicts and remove conflicting moves
//             var finalPositions = ResolveCollisions(desiredPositions);
//
//             // Phase 3: Apply the resolved positions
//             bool anyMoved = false;
//             foreach (var kvp in finalPositions)
//             {
//                 Particle particle = kvp.Key;
//                 Vector3Int newPos = kvp.Value;
//
//                 if (newPos != particle.GridPosition)
//                 {
//                     particle.SetGridPosition(newPos);
//                     if (_particleViews.TryGetValue(particle, out ParticleView view))
//                         view.AnimateToCurrentGridPosition();
//                     anyMoved = true;
//                 }
//             }
//
//             _cycleCount++;
//             _idleCount = anyMoved ? 0 : _idleCount + 1;
//
//             CheckStopConditions();
//         }
//
//         private void UpdateGridLookup()
//         {
//             _gridLookup.Clear();
//             foreach (Particle p in _particles)
//             {
//                 _gridLookup[p.GridPosition] = p;
//             }
//         }
//
//         private Vector3Int ProcessActions(Particle particle, List<ActionBlock> blocks, bool isOnInit)
//         {
//             Vector3Int pos = particle.GridPosition;
//             if (blocks == null) return pos;
//
//             foreach (ActionBlock block in blocks)
//             {
//                 if (block == null || block.actions == null) continue;
//
//                 if (ConditionEvaluator.Evaluate(block.condition, particle, _gridLookup, this))
//                 {
//                     foreach (string action in block.actions)
//                     {
//                         if (string.IsNullOrWhiteSpace(action)) continue;
//
//                         if (MoveAction.TryParse(action, out Vector3Int delta))
//                         {
//                             if (!isOnInit)
//                                 pos = ApplyBoundaryPublic(pos + delta);
//                         }
//                         else if (SetStateAction.TryParse(action, out string key, out object value))
//                         {
//                             particle.SetInternalState(key, value);
//                         }
//                         else if (SetColorAction.TryParse(action, out Color color))
//                         {
//                             if (_particleViews.TryGetValue(particle, out ParticleView view))
//                                 view.SetColor(color);
//                         }
//                         else
//                         {
//                             Debug.LogWarning($"[SimulationManager] Action not recognized or failed to parse: '{action}'");
//                         }
//                     }
//                 }
//             }
//
//             return pos;
//         }
//
//         /// <summary>
//         /// Resolves collisions between particles:
//         /// - If two or more particles want the same space, none of them move to that space
//         /// - Particles can occupy a space being vacated by another particle
//         /// - If a destination is blocked by a particle that isn't moving, the move is cancelled
//         /// </summary>
//         private Dictionary<Particle, Vector3Int> ResolveCollisions(Dictionary<Particle, Vector3Int> desiredPositions)
//         {
//             var finalPositions = new Dictionary<Particle, Vector3Int>();
//             var willMove = new Dictionary<Particle, bool>();
//
//             // Phase 1: Initial intent
//             foreach (var particle in _particles)
//             {
//                 willMove[particle] = desiredPositions[particle] != particle.GridPosition;
//             }
//
//             // Phase 2: Rule - If multiple particles want the same spot, none of them move
//             var requestCounts = new Dictionary<Vector3Int, int>();
//             foreach (var pos in desiredPositions.Values)
//             {
//                 if (!requestCounts.ContainsKey(pos)) requestCounts[pos] = 0;
//                 requestCounts[pos]++;
//             }
//
//             foreach (var particle in _particles)
//             {
//                 if (willMove[particle] && requestCounts[desiredPositions[particle]] > 1)
//                 {
//                     willMove[particle] = false;
//                 }
//             }
//
//             // Phase 3: Chain resolution - if A wants to move to P, but P is occupied by B 
//             // who is NOT moving (or had its move cancelled), A's move must also be cancelled.
//             bool changed;
//             do
//             {
//                 changed = false;
//                 foreach (var particle in _particles)
//                 {
//                     if (willMove[particle])
//                     {
//                         Vector3Int dest = desiredPositions[particle];
//                         // Is dest occupied by someone who isn't moving?
//                         if (_gridLookup.TryGetValue(dest, out Particle occupant))
//                         {
//                             if (!willMove[occupant])
//                             {
//                                 willMove[particle] = false;
//                                 changed = true;
//                             }
//                         }
//                     }
//                 }
//             } while (changed);
//
//             // Phase 4: Build final positions based on resolution
//             foreach (var particle in _particles)
//             {
//                 if (willMove[particle])
//                 {
//                     finalPositions[particle] = desiredPositions[particle];
//                     _totalMovements++;
//                 }
//                 else
//                 {
//                     finalPositions[particle] = particle.GridPosition;
//                 }
//             }
//
//             return finalPositions;
//         }
//
//         /// <summary>
//         /// Stops the simulation (without resetting) once a stop criterion from
//         /// the scenario is met. Particles remain at their final position.
//         /// </summary>
//         private void CheckStopConditions()
//         {
//             if (_maxCycles > 0 && _cycleCount >= _maxCycles)
//             {
//                 FinishSimulation($"reached maxCycles ({_maxCycles})");
//                 return;
//             }
//
//             if (_idleCyclesLimit > 0 && _idleCount >= _idleCyclesLimit)
//             {
//                 FinishSimulation($"no movement for {_idleCount} cycle(s)");
//             }
//         }
//
//         private void FinishSimulation(string reason)
//         {
//             _finished = true;
//             _running = false;
//             Debug.Log($"[SimulationManager] Simulation finished after {_cycleCount} cycle(s): {reason}. " +
//                       "Particles held at their final position.");
//         }
//
//         public Vector3Int ApplyBoundaryPublic(Vector3Int pos)
//         {
//             switch (boundaryMode)
//             {
//                 case BoundaryMode.Clamp:
//                     pos.x = Mathf.Clamp(pos.x, 0, _gridSize.x - 1);
//                     pos.y = Mathf.Clamp(pos.y, 0, _gridSize.y - 1);
//                     pos.z = Mathf.Clamp(pos.z, 0, _gridSize.z - 1);
//                     break;
//                 case BoundaryMode.Wrap:
//                     pos.x = Wrap(pos.x, _gridSize.x);
//                     pos.y = Wrap(pos.y, _gridSize.y);
//                     pos.z = Wrap(pos.z, _gridSize.z);
//                     break;
//             }
//             return pos;
//         }
//
//         private static int Wrap(int value, int size)
//         {
//             if (size <= 0)
//                 return value;
//             return ((value % size) + size) % size;
//         }
//
//         // ---------- Helpers ----------
//
//         public void SetRunning(bool running)
//         {
//             // Once finished, the simulation stays put and is not restarted.
//             if (_finished)
//                 return;
//             _running = running;
//         }
//
//         /// <summary>Starts or resumes the simulation stepping.</summary>
//         public void Run()
//         {
//             if (!HasData)
//             {
//                 Debug.LogWarning("[SimulationManager] Cannot run: no simulation data loaded.");
//                 return;
//             }
//             if (_finished)
//             {
//                 Debug.Log("[SimulationManager] Simulation already finished. Press \"Encerrar\" to reset before running again.");
//                 return;
//             }
//             _running = true;
//         }
//
//         /// <summary>Pauses the simulation, keeping cubes where they are.</summary>
//         public void Pause() => _running = false;
//
//         /// <summary>
//         /// Stops the simulation and snaps every cube back to its initial
//         /// scenario position, ready to be run again.
//         /// </summary>
//         public void ResetSimulation()
//         {
//             foreach (Particle particle in _particles)
//             {
//                 particle.ResetToInitial();
//                 if (_particleViews.TryGetValue(particle, out ParticleView view))
//                     view.ResetVisual();
//             }
//
//             _totalMovements = 0;
//             _simulationTime = 0f;
//             _cycleCount = 0;
//             _idleCount = 0;
//             _timer = 0f;
//             _finished = false;
//             _running = false;
//
//             ExecuteOnInit();
//
//             Debug.Log("[SimulationManager] Simulation reset: positions, stats and onInit re-executed.");
//         }
//
//         private void FrameCamera()
//         {
//             Camera cam = Camera.main;
//             if (cam == null)
//                 return;
//
//             Vector3 center = new Vector3(
//                 (_gridSize.x - 1) * 0.5f,
//                 (_gridSize.y - 1) * 0.5f,
//                 (_gridSize.z - 1) * 0.5f) * cellSize;
//
//             float extent = Mathf.Max(_gridSize.x, _gridSize.y, _gridSize.z) * cellSize;
//             Vector3 offset = new Vector3(0.9f, 0.8f, -1.2f).normalized * extent * 1.6f;
//
//             cam.transform.position = center + offset;
//             cam.transform.LookAt(center);
//         }
//     }
// }
