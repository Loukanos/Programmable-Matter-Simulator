using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using ParticleSim.Core;
using ParticleSim.InputValidation;

namespace ParticleSim.UI
{
    /// <summary>
    /// Orquestrador da simulação. É o único MonoBehaviour que conhece todos
    /// os módulos: aciona o carregamento (Entrada e Validação), a
    /// instanciação (ParticleManager), o motor de ciclos (SimulationEngine),
    /// o consolidador de métricas (MetricsCalculator) e cuida da
    /// visualização (câmera, bounds, views das partículas). Substitui o
    /// antigo SimulationManager, que concentrava as cinco responsabilidades
    /// em uma única classe.
    /// </summary>
    public class SimulationController : MonoBehaviour
    {
        [Header("Arquivos de dados (relativos a StreamingAssets)")]
        public string scenarioFileName = "scenario.json";
        public string behaviorFileName = "behavior.json";

        [Header("Simulação")]
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
        /// <summary>Descriptive validation errors from the last failed load, one property per file so the UI can show each in its own file card. Null when that file's last load succeeded.</summary>
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

            // Motor de Simulação não conhece ParticleView: a UI que assina
            // os eventos e decide como representar visualmente a mudança.
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
            // Frame rate reflects the app's render rate, not the simulation's
            // run state, so it ticks even while paused/before Run is pressed
            // (matches the previous SimulationUIController-owned behavior).
            _metrics?.UpdateFrameRate(Time.unscaledDeltaTime);

            // On-demand re-framing works regardless of run state — useful to
            // recenter on a structure that has drifted out of view while
            // paused, not just while running.
            if (HasData && Keyboard.current != null && Keyboard.current[reframeKey].wasPressedThisFrame)
                FrameCamera();

            if (!IsRunning) return;

            _metrics.Tick(Time.deltaTime);
            _timer += Time.deltaTime;
            if (_timer >= stepInterval)
            {
                _timer -= stepInterval;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _engine.Step();
                stopwatch.Stop();
                _metrics.RecordStepDuration(stopwatch.Elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// Carrega cenário e comportamento, reconstrói partículas e views,
        /// e opcionalmente já inicia a execução.
        ///
        /// Toda a validação — de cada arquivo e a cruzada entre os dois — mora
        /// em <see cref="ValidatedSimulation.Load"/>, não aqui: este método só
        /// pede o carregamento e repassa os erros. É o fluxo da Figura 7 ("a
        /// Interface aciona o carregamento, o módulo de Entrada e Validação os
        /// valida"), e é o que faz o tipo recebido pelo Gerenciador de Partículas
        /// carregar a garantia de validação consigo.
        /// </summary>
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
                // Aviso (amarelo), não erro: um arquivo inválido fornecido pelo
                // usuário é uma situação prevista, já explicada nas caixas de erro
                // da interface — não um defeito do programa. Vermelho fica
                // reservado para configuração quebrada que exige correção no
                // código ou na cena (shader não atribuído, elemento de UI
                // ausente). Também evita que o "Error Pause" do console pause o
                // Play Mode a cada arquivo inválido testado de propósito.
                Debug.LogWarning($"[SimulationController] Falha ao carregar:\n{LastScenarioLoadError}\n{LastBehaviorLoadError}");
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

        /// <summary>
        /// Builds the shared particle material. **Keep `particleShader` assigned in the
        /// Inspector.** `Shader.Find` only finds shaders that are actually in the build, and a
        /// shader nothing references gets stripped — so the `Shader.Find` path below works in
        /// the Editor (where every shader is loaded) and then silently fails in a player,
        /// landing on the magenta error shader, which ignores the colors set on it. That is
        /// exactly what happened once: the scene's serialized reference to URP/Lit was dropped
        /// along with a duplicate component, and every particle turned pink in builds only.
        /// A serialized reference is what guarantees the shader survives into the build; the
        /// error below exists so the next occurrence is one console line instead of a hunt.
        /// </summary>
        private Material CreateParticleMaterial()
        {
            Shader shader = particleShader != null ? particleShader
                : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

            if (shader == null)
                Debug.LogError($"[{nameof(SimulationController)}] Shader das partículas não encontrado — atribua '{nameof(particleShader)}' no Inspector. Em builds, Shader.Find só acha shaders incluídos, então as partículas vão aparecer em rosa (shader de erro).", this);

            var mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", particleColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", particleColor);
            return mat;
        }

        // ---------- Live visual/pacing settings ----------
        // These six fields are serialized and so appear in the Inspector, but
        // five of them are only *read* while building (BuildViews /
        // BuildGridBounds), so editing them there during Play Mode does
        // nothing until the next load. The setters below are what make them
        // actually live for the UI: each one writes the field and then
        // re-applies it to whatever already exists. None of them touch
        // simulation state — no particle position, internal state, cycle
        // count or metric is read or written here, so changing any of them
        // mid-run cannot alter the outcome of a scenario.

        /// <summary>Seconds between cycles. Already live: <see cref="Update"/> reads it every frame.</summary>
        public void SetStepInterval(float seconds)
        {
            stepInterval = Mathf.Max(0f, seconds);
        }

        /// <summary>Cube size as a fraction of a cell. Rescales the existing cubes in place.</summary>
        public void SetCubeFill(float fill)
        {
            cubeFill = Mathf.Clamp(fill, 0.1f, 1f);
            foreach (ParticleView view in _views.Values)
                view.transform.localScale = Vector3.one * cellSize * cubeFill;
        }

        /// <summary>
        /// Default particle color. Updates the shared material (so anything
        /// still rendering straight from it follows) and each view's fallback
        /// color. Particles currently colored by their own `setColor()` keep
        /// that color — see <see cref="ParticleView.SetBaseColor"/>.
        /// </summary>
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

        /// <summary>Seconds a cube takes to slide between cells. Applies to in-flight animations too.</summary>
        public void SetMoveAnimationDuration(float seconds)
        {
            moveAnimationDuration = Mathf.Max(0f, seconds);
            foreach (ParticleView view in _views.Values)
                view.SetMoveDuration(moveAnimationDuration);
        }

        /// <summary>
        /// Shows/hides the grid wireframe. Rebuilds only the bounds object —
        /// <see cref="BuildGridBounds"/> destroys and recreates one decorative
        /// GameObject and never touches particles or the engine.
        /// </summary>
        public void SetShowGridBounds(bool show)
        {
            showGridBounds = show;
            if (HasData) BuildGridBounds();
        }

        /// <summary>Grid wireframe color. Same cheap rebuild as <see cref="SetShowGridBounds"/>.</summary>
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

        /// <summary>
        /// Positions the camera so every particle is visible, computed from
        /// the particles' actual occupied bounding box — never the declared
        /// gridSize, which in several scenarios is far larger than the
        /// region particles actually use (e.g. a tall, mostly-empty grid;
        /// framing by gridSize there would leave the real structure tiny on
        /// screen). Uses the camera's real field of view/aspect to guarantee
        /// the whole bounding sphere of that box fits in frame (see
        /// <see cref="FramingDistance"/>), rather than a fixed distance
        /// multiplier that could clip content depending on the scenario's
        /// shape or the camera's aspect ratio. Also scales
        /// <see cref="FlyCameraController"/>'s move speed to the scene's
        /// size, so crossing a large scenario doesn't feel glacially slow
        /// (and a small one doesn't overshoot on the first key press).
        /// Public — and re-checked every frame against <see cref="reframeKey"/>
        /// in <see cref="Update"/> — so the "reenquadrar" UI button and the
        /// key shortcut can both call it on demand, mid-simulation, without
        /// reloading (particles can drift well outside the initial framing
        /// as a simulation runs).
        /// </summary>
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

            // Padded by half a cell on each side (cells span integer coords,
            // a particle's cube is centered on its cell), same convention
            // GridBoundsRenderer already uses.
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

        /// <summary>
        /// Distance along the view axis at which a bounding sphere of the
        /// given radius is fully contained by the camera's frustum, using
        /// whichever of the vertical/horizontal field of view is narrower
        /// (so it holds regardless of aspect ratio or which axis the scene
        /// is longer along) — the standard "frame bounds" distance formula:
        /// a sphere's angular radius as seen from the camera is
        /// asin(radius / distance), so the sphere stays fully inside a
        /// half-angle θ once distance ≥ radius / sin(θ). A pure function of
        /// geometry (no Camera/Transform dependency), verified independently
        /// against a sampled-frustum containment check before this was wired
        /// into <see cref="FrameCamera"/>.
        /// </summary>
        internal static float FramingDistance(float radius, float verticalFovDegrees, float aspect, float padding)
        {
            float verticalHalfAngle = verticalFovDegrees * 0.5f * Mathf.Deg2Rad;
            float horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * aspect);
            float limitingHalfAngle = Mathf.Min(verticalHalfAngle, horizontalHalfAngle);
            return radius / Mathf.Sin(limitingHalfAngle) * padding;
        }
    }
}