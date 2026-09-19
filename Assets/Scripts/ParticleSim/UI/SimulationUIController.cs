using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ParticleSim.UI
{
    /// <summary>
    /// Wires the UI Toolkit controls to the <see cref="SimulationController"/>:
    /// transport buttons (run / pause / stop), camera framing, the sidebar
    /// toggle, and the native file pickers that let the user load their own
    /// scenario and behavior JSON files.
    ///
    /// Every clickable control is a real <see cref="Button"/> rather than a
    /// <see cref="VisualElement"/> with a hand-attached <see cref="Clickable"/>:
    /// Button already contains one, and it additionally brings focus handling
    /// and keyboard activation, so the transport controls are reachable without
    /// a mouse. It also means there are no manipulators to store and detach —
    /// unsubscribing a `clicked` delegate is enough.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SimulationUIController : MonoBehaviour
    {
        /// <summary>USS class that hides an element; see `.hidden` in SimulationUI.uss.</summary>
        private const string HiddenClass = "hidden";

        /// <summary>
        /// Indicators refresh on a fixed interval rather than every frame.
        /// Each refresh builds ten interpolated strings, so at the frame rates
        /// this project actually hits (800+ FPS with a small scenario) a
        /// per-frame refresh meant thousands of throwaway strings per second —
        /// avoidable GC churn in the one panel whose job is to report this
        /// project's own performance indicators. 10 Hz is also simply more
        /// readable: digits changing 800 times a second are a blur, not a
        /// measurement.
        /// </summary>
        private const float StatsRefreshInterval = 0.1f;

        [SerializeField] private SimulationController simulation;

        private Button _runButton;
        private Button _pauseButton;
        private Button _stopButton;
        private Button _reframeButton;
        private Button _expandButton;
        private Button _fullscreenButton;
        private Button _quitButton;
        private Button _scenarioButton;
        private Button _behaviorButton;
        private Button _scenarioXButton;
        private Button _behaviorXButton;
        private Button _loadButton;
        private Button _confirmQuitButton;
        private Button _cancelQuitButton;

        private VisualElement _filePanel;
        private VisualElement _quitPopup;
        private ScrollView _scenarioErrorBox;
        private ScrollView _behaviorErrorBox;

        private Label _scenarioPathLabel;
        private Label _behaviorPathLabel;
        private Label _statusLabel;
        private Label _scenarioErrorLabel;
        private Label _behaviorErrorLabel;
        private Label _particleCountLabel;
        private Label _movementsLabel;
        private Label _collisionsLabel;
        private Label _invalidMovementsLabel;
        private Label _fpsLabel;
        private Label _timeLabel;
        private Label _cyclesLabel;
        private Label _stepTimeLabel;
        private Label _messagesSentLabel;
        private Label _messagesDeliveredLabel;

        private string _scenarioPath;
        private string _behaviorPath;
        private float _statsTimer;

        /// <summary>
        /// True once every element was found and every handler subscribed.
        /// Guards <see cref="OnDisable"/>, which still runs when binding failed
        /// and disabled this component.
        /// </summary>
        private bool _bound;

        /// <summary>Set by <see cref="Find{T}"/> when a named element is missing.</summary>
        private bool _bindingFailed;

        private void Awake()
        {
            if (simulation == null)
                simulation = FindFirstObjectByType<SimulationController>();
        }

        private void OnEnable()
        {
            if (simulation == null)
            {
                Debug.LogError($"[{nameof(SimulationUIController)}] Nenhum {nameof(SimulationController)} na cena. UI desativada.", this);
                enabled = false;
                return;
            }

            if (!Bind())
            {
                // Binding only fails when SimulationUI.uxml and this script
                // disagree about an element name. Disabling the component makes
                // that a single loud error at startup instead of a silently
                // dead control, or a NullReferenceException every frame.
                Debug.LogError($"[{nameof(SimulationUIController)}] SimulationUI.uxml não corresponde a este script (veja os erros acima). UI desativada.", this);
                enabled = false;
                return;
            }

            Subscribe();
            _bound = true;

            _scenarioPathLabel.text = simulation.scenarioFileName;
            _behaviorPathLabel.text = simulation.behaviorFileName;
        }

        private void Start()
        {
            if (!_bound) return;

            SetStatus(simulation.HasData
                ? "Simulação carregada (padrão)."
                : "Falha ao carregar padrões. Verifique os erros abaixo.");

            ShowLoadErrors();
        }

        private void OnDisable()
        {
            if (!_bound) return;

            _runButton.clicked -= OnRun;
            _pauseButton.clicked -= OnPause;
            _stopButton.clicked -= OnStop;
            _reframeButton.clicked -= OnReframe;
            _expandButton.clicked -= OnToggleSidebar;
            _fullscreenButton.clicked -= OnToggleFullscreen;
            _quitButton.clicked -= OnShowQuitPopup;
            _confirmQuitButton.clicked -= OnConfirmQuit;
            _cancelQuitButton.clicked -= OnCancelQuit;
            _scenarioButton.clicked -= OnPickScenario;
            _behaviorButton.clicked -= OnPickBehavior;
            _scenarioXButton.clicked -= OnClearScenario;
            _behaviorXButton.clicked -= OnClearBehavior;
            _loadButton.clicked -= OnLoad;

            _bound = false;
        }

        // ---------- Binding ----------

        /// <summary>
        /// Resolves every element this script drives. Returns false if any one
        /// of them is missing, having already logged which — a missing element
        /// means the UXML was renamed without updating this file, which is a
        /// developer error worth surfacing rather than swallowing.
        /// </summary>
        private bool Bind()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _bindingFailed = false;

            _runButton = Find<Button>(root, "runButton");
            _pauseButton = Find<Button>(root, "pauseButton");
            _stopButton = Find<Button>(root, "stopButton");
            _reframeButton = Find<Button>(root, "reframeButton");
            _expandButton = Find<Button>(root, "expandButton");
            _fullscreenButton = Find<Button>(root, "fullscreenButton");
            _quitButton = Find<Button>(root, "quitButton");
            _scenarioButton = Find<Button>(root, "scenarioButton");
            _behaviorButton = Find<Button>(root, "behaviorButton");
            _scenarioXButton = Find<Button>(root, "scenarioXButton");
            _behaviorXButton = Find<Button>(root, "behaviorXButton");
            _loadButton = Find<Button>(root, "loadButton");
            _confirmQuitButton = Find<Button>(root, "confirmQuitButton");
            _cancelQuitButton = Find<Button>(root, "cancelQuitButton");

            _filePanel = Find<VisualElement>(root, "filePanel");
            _quitPopup = Find<VisualElement>(root, "quitPopup");
            _scenarioErrorBox = Find<ScrollView>(root, "scenarioErrorBox");
            _behaviorErrorBox = Find<ScrollView>(root, "behaviorErrorBox");

            _scenarioPathLabel = Find<Label>(root, "scenarioPathLabel");
            _behaviorPathLabel = Find<Label>(root, "behaviorPathLabel");
            _statusLabel = Find<Label>(root, "statusLabel");
            _scenarioErrorLabel = Find<Label>(root, "scenarioErrorLabel");
            _behaviorErrorLabel = Find<Label>(root, "behaviorErrorLabel");
            _particleCountLabel = Find<Label>(root, "particleCountLabel");
            _movementsLabel = Find<Label>(root, "movementsLabel");
            _collisionsLabel = Find<Label>(root, "collisionsLabel");
            _invalidMovementsLabel = Find<Label>(root, "invalidMovementsLabel");
            _fpsLabel = Find<Label>(root, "fpsLabel");
            _timeLabel = Find<Label>(root, "timeLabel");
            _cyclesLabel = Find<Label>(root, "cyclesLabel");
            _stepTimeLabel = Find<Label>(root, "stepTimeLabel");
            _messagesSentLabel = Find<Label>(root, "messagesSentLabel");
            _messagesDeliveredLabel = Find<Label>(root, "messagesDeliveredLabel");

            return !_bindingFailed;
        }

        private T Find<T>(VisualElement root, string elementName) where T : VisualElement
        {
            T element = root.Q<T>(elementName);
            if (element == null)
            {
                Debug.LogError($"[{nameof(SimulationUIController)}] '{elementName}' ({typeof(T).Name}) não encontrado em SimulationUI.uxml.", this);
                _bindingFailed = true;
            }

            return element;
        }

        private void Subscribe()
        {
            _runButton.clicked += OnRun;
            _pauseButton.clicked += OnPause;
            _stopButton.clicked += OnStop;
            _reframeButton.clicked += OnReframe;
            _expandButton.clicked += OnToggleSidebar;
            _fullscreenButton.clicked += OnToggleFullscreen;
            _quitButton.clicked += OnShowQuitPopup;
            _confirmQuitButton.clicked += OnConfirmQuit;
            _cancelQuitButton.clicked += OnCancelQuit;
            _scenarioButton.clicked += OnPickScenario;
            _behaviorButton.clicked += OnPickBehavior;
            _scenarioXButton.clicked += OnClearScenario;
            _behaviorXButton.clicked += OnClearBehavior;
            _loadButton.clicked += OnLoad;
        }

        // ---------- View controls ----------

        private void OnToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        private void OnReframe()
        {
            simulation.FrameCamera();
            SetStatus("Câmera reenquadrada.");
        }

        /// <summary>
        /// Shows/hides the configuration sidebar so the viewport can use the
        /// full window width. The 3D viewport already renders behind the
        /// sidebar at all times (it's just an opaque overlay), so nothing else
        /// needs to resize or re-frame when this toggles. The panel's class
        /// list is the single source of truth for whether it's open — there is
        /// no separate bool that could drift out of sync with what's on screen.
        /// </summary>
        private void OnToggleSidebar()
        {
            _filePanel.ToggleInClassList(HiddenClass);
        }

        private void OnShowQuitPopup()
        {
            _quitPopup.RemoveFromClassList(HiddenClass);
        }

        private void OnCancelQuit()
        {
            _quitPopup.AddToClassList(HiddenClass);
        }

        private void OnConfirmQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Indicators ----------

        private void Update()
        {
            _statsTimer += Time.unscaledDeltaTime;
            if (_statsTimer < StatsRefreshInterval) return;

            _statsTimer = 0f;
            UpdateStats();
        }

        private void UpdateStats()
        {
            _particleCountLabel.text = $"Partículas: {simulation.ParticleCount}";
            _movementsLabel.text = $"Movimentos: {simulation.TotalMovements}";
            _collisionsLabel.text = $"Colisões: {simulation.TotalCollisions}";
            _invalidMovementsLabel.text = $"Movimentos inválidos: {simulation.TotalInvalidMovements}";
            _fpsLabel.text = $"FPS: {Mathf.RoundToInt(simulation.FrameRate)}";
            _timeLabel.text = $"Tempo decorrido: {simulation.SimulationTime:F1}s";
            _cyclesLabel.text = $"Ciclos: {simulation.CycleCount}";
            _stepTimeLabel.text = $"Tempo por ciclo: {simulation.AverageStepTimeMs:F2}ms";
            _messagesSentLabel.text = $"Mensagens enviadas: {simulation.TotalMessagesSent}";
            _messagesDeliveredLabel.text = $"Mensagens recebidas: {simulation.TotalMessagesDelivered}";
        }

        // ---------- Simulation controls ----------

        private void OnRun()
        {
            simulation.Run();
            SetStatus(simulation.IsFinished
                ? "Simulação encerrada. Use \"Encerrar\" para resetar."
                : "Rodando...");
        }

        private void OnPause()
        {
            simulation.Pause();
            SetStatus("Pausado.");
        }

        private void OnStop()
        {
            simulation.ResetSimulation();
            SetStatus("Resetado para o início.");
        }

        // ---------- File pickers ----------

        private void OnPickScenario()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de cenário (JSON)");
            if (string.IsNullOrEmpty(path)) return;

            _scenarioPath = path;
            _scenarioPathLabel.text = Path.GetFileName(path);
        }

        private void OnPickBehavior()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de comportamento (JSON)");
            if (string.IsNullOrEmpty(path)) return;

            _behaviorPath = path;
            _behaviorPathLabel.text = Path.GetFileName(path);
        }

        private void OnClearScenario()
        {
            _scenarioPath = null;
            _scenarioPathLabel.text = simulation.scenarioFileName;
        }

        private void OnClearBehavior()
        {
            _behaviorPath = null;
            _behaviorPathLabel.text = simulation.behaviorFileName;
        }

        private void OnLoad()
        {
            string scenarioPath = string.IsNullOrEmpty(_scenarioPath)
                ? Path.Combine(Application.streamingAssetsPath, simulation.scenarioFileName)
                : _scenarioPath;

            string behaviorPath = string.IsNullOrEmpty(_behaviorPath)
                ? Path.Combine(Application.streamingAssetsPath, simulation.behaviorFileName)
                : _behaviorPath;

            bool ok = simulation.LoadSimulation(scenarioPath, behaviorPath, false);
            SetStatus(ok
                ? "Simulação carregada. Pressione \"Rodar\"."
                : "Falha ao carregar. Verifique os erros abaixo.");

            ShowLoadErrors();
        }

        private void SetStatus(string message)
        {
            _statusLabel.text = message;
        }

        /// <summary>
        /// Provisional display of the validation errors that blocked the last
        /// load, one independently-scrollable box per file. The message is built
        /// by the validation module and only passed through by
        /// SimulationController — split per file there
        /// (LastScenarioLoadError / LastBehaviorLoadError) so an error in one
        /// file never shows under the other's card.
        /// </summary>
        private void ShowLoadErrors()
        {
            ApplyError(_scenarioErrorBox, _scenarioErrorLabel, simulation.LastScenarioLoadError);
            ApplyError(_behaviorErrorBox, _behaviorErrorLabel, simulation.LastBehaviorLoadError);
        }

        private static void ApplyError(VisualElement box, Label label, string error)
        {
            bool hasError = !string.IsNullOrEmpty(error);
            label.text = hasError ? error : string.Empty;
            box.EnableInClassList(HiddenClass, !hasError);
        }
    }
}
