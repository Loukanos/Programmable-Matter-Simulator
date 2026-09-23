using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using ParticleSim.Core;
using ParticleSim.InputValidation;

namespace ParticleSim.UI
{
    public class SimulationController : MonoBehaviour
    {
        [Header("Data files (relative to StreamingAssets)")]
        public string scenarioFileName = "scenario.json";
        public string behaviorFileName = "behavior.json";

        [Header("Simulation")]
        public float stepInterval = 1f;
        public bool autoStart = true;

        [Header("Visual")]
        public float cellSize = 1f;
        [Range(0.1f, 1f)] public float cubeFill = 0.9f;
        public Color particleColor = new Color(0.2f, 0.8f, 1f);
        public Shader particleShader;
        public bool frameCameraOnStart = true;
        public float moveAnimationDuration = 0.25f;

        [Header("Camera framing")]
        [Tooltip("Multiplier applied on top of the exact fit-to-view distance, so particles at the edge of the bounding box aren't flush against the edge of the screen.")]
        [Range(1f, 2f)] public float cameraFramingPadding = 1.15f;
        [Tooltip("When a scenario loads, also scale FlyCameraController's move speed to the scene's size.")]
        public bool scaleFlySpeedToScene = true;
        [Tooltip("Key that re-frames the camera on demand, without reloading.")]
        public Key reframeKey = Key.F;

        [Header("Grid bounds")]
        public bool showGridBounds = true;
        public Color gridBoundsColor = new Color(0.5f, 0.9f, 1f, 0.5f);
        public float gridBoundsLineWidth = 0.04f;
        [Tooltip("Keep assigned (URP/Unlit). Like particleShader, this serialized reference is what keeps the shader in the build — GridBoundsRenderer is created at runtime and has no Inspector slot of its own.")]
        public Shader gridBoundsShader;

        private readonly ParticleManager.ParticleManager _particleManager = new ParticleManager.ParticleManager();
        private SimulationEngine.SimulationEngine _engine;
        private MetricsCalculator.MetricsCalculator _metrics;

        private readonly Dictionary<int, ParticleView> _views = new Dictionary<int, ParticleView>();
        private Transform _particlesRoot;
        private Material _sharedMaterial;
        private GridBoundsRenderer _gridBounds;
        private float _timer;

        public bool HasData { get; private set; }
        public string LastScenarioLoadError { get; private set; }
        public string LastBehaviorLoadError { get; private set; }
        public int ParticleCount => _particleManager.Particles.Count;
        public int TotalMovements => _metrics?.TotalMovements ?? 0;
        public float SimulationTime => _metrics?.SimulationTime ?? 0f;
        public int CycleCount => _engine?.CurrentCycle ?? 0;
        public bool IsRunning => _engine?.IsRunning ?? false;
        public bool IsFinished => _engine?.IsFinished ?? false;
        public int TotalCollisions => _metrics?.TotalCollisions ?? 0;
        public int TotalInvalidMovements => _metrics?.TotalInvalidMovements ?? 0;
        public double AverageStepTimeMs => _metrics?.AverageStepTimeMs ?? 0.0;
        public float FrameRate => _metrics?.FrameRate ?? 0f;
        public int TotalMessagesSent => _metrics?.TotalMessagesSent ?? 0;
        public int TotalMessagesDelivered => _metrics?.TotalMessagesDelivered ?? 0;

        private void Start()
        {
            _engine = new SimulationEngine.SimulationEngine(_particleManager);
            _metrics = new MetricsCalculator.MetricsCalculator(_engine);

            _engine.OnParticleMoved += (particle, _) =>
            {
                if (_views.TryGetValue(particle.Id, out ParticleView view))
                    view.AnimateToCurrentGridPosition();
            };
            _engine.OnColorChanged += (particle, color) =>
            {
                if (_views.TryGetValue(particle.Id, out ParticleView view))
                    view.SetColor(color);
            };

            string scenarioPath = Path.Combine(Application.streamingAssetsPath, scenarioFileName);
            string behaviorPath = Path.Combine(Application.streamingAssetsPath, behaviorFileName);
            LoadSimulation(scenarioPath, behaviorPath, autoStart);
        }

        private void Update()
        {
            _metrics?.UpdateFrameRate(Time.unscaledDeltaTime);

            if (HasData && Keyboard.current != null && Keyboard.current[reframeKey].wasPressedThisFrame)
                FrameCamera();

            if (!IsRunning) return;

            _metrics.Tick(Time.deltaTime);
            _timer += Time.deltaTime;
            // if, not while: with a zero step interval a while loop would never exit.
            if (_timer >= stepInterval)
            {
                _timer -= stepInterval;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _engine.Step();
                stopwatch.Stop();
                _metrics.RecordStepDuration(stopwatch.Elapsed.TotalSeconds);
            }
        }

        public bool LoadSimulation(string scenarioPath, string behaviorPath, bool startRunning = false)
        {
            ValidatedSimulation loaded = ValidatedSimulation.Load(
                scenarioPath, behaviorPath,
                out ValidationResult scenarioValidation,
                out ValidationResult behaviorValidation);

            if (loaded == null)
            {
                LastScenarioLoadError = scenarioValidation.IsValid ? null : scenarioValidation.ToMessage();
                LastBehaviorLoadError = behaviorValidation.IsValid ? null : behaviorValidation.ToMessage();
                Debug.LogWarning($"[SimulationController] Load failed:\n{LastScenarioLoadError}\n{LastBehaviorLoadError}");
                return false;
            }

            LastScenarioLoadError = null;
            LastBehaviorLoadError = null;

            _particleManager.BuildFromScenario(loaded);
            _engine.SetBehaviors(loaded.Behaviors);
            _engine.MaxCycles = loaded.Scenario.maxCycles;
            _engine.IdleCyclesLimit = loaded.Scenario.idleCyclesLimit;
            _engine.Reset();
            _metrics.Reset();

            BuildViews();
            _engine.ExecuteOnInit();
            BuildGridBounds();

            HasData = true;
            _timer = 0f;

            if (frameCameraOnStart) FrameCamera();
            if (startRunning) _engine.Run();

            return true;
        }

        private void BuildViews()
        {
            if (_particlesRoot != null) Destroy(_particlesRoot.gameObject);
            _particlesRoot = new GameObject("Particles").transform;
            _particlesRoot.SetParent(transform, false);
            _views.Clear();

            _sharedMaterial = CreateParticleMaterial();

            foreach (Particle particle in _particleManager.Particles)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(_particlesRoot, false);
                cube.transform.localScale = Vector3.one * cellSize * cubeFill;
                cube.GetComponent<Renderer>().sharedMaterial = _sharedMaterial;

                ParticleView view = cube.AddComponent<ParticleView>();
                view.Bind(particle, cellSize, moveAnimationDuration);
                _views[particle.Id] = view;
            }
        }

        private void BuildGridBounds()
        {
            if (_gridBounds != null) { Destroy(_gridBounds.gameObject); _gridBounds = null; }
            if (!showGridBounds) return;

            var boundsGo = new GameObject("GridBounds");
            boundsGo.transform.SetParent(transform, false);
            _gridBounds = boundsGo.AddComponent<GridBoundsRenderer>();
            _gridBounds.Build(_particleManager.GridSize, cellSize, gridBoundsColor, gridBoundsLineWidth, gridBoundsShader);
        }

        // Keep particleShader assigned in the Inspector: Shader.Find alone fails in builds and the particles turn pink.
        private Material CreateParticleMaterial()
        {
            Shader shader = particleShader != null ? particleShader
                : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

            if (shader == null)
                Debug.LogError($"[{nameof(SimulationController)}] Particle shader not found: assign '{nameof(particleShader)}' in the Inspector. In builds Shader.Find only finds included shaders, so the particles will render pink (error shader).", this);

            var mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", particleColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", particleColor);
            return mat;
        }

        public void SetStepInterval(float seconds)
        {
            stepInterval = Mathf.Max(0f, seconds);
        }

        public void SetCubeFill(float fill)
        {
            cubeFill = Mathf.Clamp(fill, 0.1f, 1f);
            foreach (ParticleView view in _views.Values)
                view.transform.localScale = Vector3.one * cellSize * cubeFill;
        }

        public void SetParticleColor(Color color)
        {
            particleColor = color;

            if (_sharedMaterial != null)
            {
                if (_sharedMaterial.HasProperty("_BaseColor")) _sharedMaterial.SetColor("_BaseColor", color);
                if (_sharedMaterial.HasProperty("_Color")) _sharedMaterial.SetColor("_Color", color);
            }

            foreach (ParticleView view in _views.Values)
                view.SetBaseColor(color);
        }

        public void SetMoveAnimationDuration(float seconds)
        {
            moveAnimationDuration = Mathf.Max(0f, seconds);
            foreach (ParticleView view in _views.Values)
                view.SetMoveDuration(moveAnimationDuration);
        }

        public void SetShowGridBounds(bool show)
        {
            showGridBounds = show;
            if (HasData) BuildGridBounds();
        }

        public void SetGridBoundsColor(Color color)
        {
            gridBoundsColor = color;
            if (HasData) BuildGridBounds();
        }

        public void Run() => _engine.Run();
        public void Pause() => _engine.Pause();

        public void ResetSimulation()
        {
            _particleManager.ResetParticles();
            foreach (ParticleView view in _views.Values)
                view.ResetVisual();

            _engine.Reset();
            _metrics.Reset();
            _timer = 0f;
            _engine.ExecuteOnInit();
        }

        public void FrameCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            IReadOnlyList<Particle> particles = _particleManager.Particles;
            if (particles.Count == 0) return;

            Vector3Int min = particles[0].GridPosition;
            Vector3Int max = min;
            foreach (Particle particle in particles)
            {
                Vector3Int pos = particle.GridPosition;
                if (pos.x < min.x) min.x = pos.x; else if (pos.x > max.x) max.x = pos.x;
                if (pos.y < min.y) min.y = pos.y; else if (pos.y > max.y) max.y = pos.y;
                if (pos.z < min.z) min.z = pos.z; else if (pos.z > max.z) max.z = pos.z;
            }

            Vector3 worldMin = new Vector3(min.x - 0.5f, min.y - 0.5f, min.z - 0.5f) * cellSize;
            Vector3 worldMax = new Vector3(max.x + 0.5f, max.y + 0.5f, max.z + 0.5f) * cellSize;
            Vector3 center = (worldMin + worldMax) * 0.5f;
            Vector3 extents = (worldMax - worldMin) * 0.5f;
            float radius = Mathf.Max(extents.magnitude, cellSize * 0.5f);

            float distance = FramingDistance(radius, cam.fieldOfView, cam.aspect, cameraFramingPadding);
            Vector3 direction = new Vector3(0.9f, 0.8f, -1.2f).normalized;

            cam.transform.position = center + direction * distance;
            cam.transform.LookAt(center);

            if (scaleFlySpeedToScene)
            {
                FlyCameraController fly = cam.GetComponent<FlyCameraController>();
                if (fly != null) fly.ConfigureForSceneSize(radius * 2f);
            }
        }

        internal static float FramingDistance(float radius, float verticalFovDegrees, float aspect, float padding)
        {
            float verticalHalfAngle = verticalFovDegrees * 0.5f * Mathf.Deg2Rad;
            float horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * aspect);
            float limitingHalfAngle = Mathf.Min(verticalHalfAngle, horizontalHalfAngle);
            return radius / Mathf.Sin(limitingHalfAngle) * padding;
        }
    }
}
