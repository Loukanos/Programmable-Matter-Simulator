using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ParticleSim.UI
{
    /// <summary>
    /// Wires the UI Toolkit controls to the <see cref="SimulationController"/>:
    /// Run / Pause / Reset buttons plus native file pickers that let the user
    /// load their own scenario and behavior JSON files.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SimulationUIController : MonoBehaviour
    {
        [SerializeField] private SimulationController simulation;

        private VisualElement _runButton;
        private VisualElement _pauseButton;
        private VisualElement _stopButton;
        private Button _scenarioButton;
        private Button _behaviorButton;
        private Button _loadButton;
        private Label _scenarioPathLabel;
        private Label _behaviorPathLabel;
        private Label _statusLabel;
        private Label _movementsLabel;
        private Label _collisionsLabel;
        private Label _invalidMovementsLabel;
        private Label _fpsLabel;
        private Label _timeLabel;
        private Label _cyclesLabel;
        private Label _stepTimeLabel;
        private Label _messagesSentLabel;
        private Label _messagesDeliveredLabel;

        private VisualElement _fullscreenButton;
        private VisualElement _quitButton;
        private VisualElement _quitPopup;
        private Button _confirmQuitButton;
        private Button _cancelQuitButton;

        private Clickable _runClickable;
        private Clickable _pauseClickable;
        private Clickable _stopClickable;
        private Clickable _fullscreenClickable;
        private Clickable _quitClickable;

        private string _scenarioPath;
        private string _behaviorPath;

        private void Awake()
        {
            if (simulation == null)
                simulation = FindFirstObjectByType<SimulationController>();
        }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            _runButton = root.Q<VisualElement>("runButton");
            _pauseButton = root.Q<VisualElement>("pauseButton");
            _stopButton = root.Q<VisualElement>("stopButton");
            _scenarioButton = root.Q<Button>("scenarioButton");
            _behaviorButton = root.Q<Button>("behaviorButton");
            _loadButton = root.Q<Button>("loadButton");
            _scenarioPathLabel = root.Q<Label>("scenarioPathLabel");
            _behaviorPathLabel = root.Q<Label>("behaviorPathLabel");
            _statusLabel = root.Q<Label>("statusLabel");

            if (_scenarioPathLabel != null && simulation != null) _scenarioPathLabel.text = simulation.scenarioFileName;
            if (_behaviorPathLabel != null && simulation != null) _behaviorPathLabel.text = simulation.behaviorFileName;

            _movementsLabel = root.Q<Label>("movementsLabel");
            _collisionsLabel = root.Q<Label>("collisionsLabel");
            _invalidMovementsLabel = root.Q<Label>("invalidMovementsLabel");
            _fpsLabel = root.Q<Label>("fpsLabel");
            _timeLabel = root.Q<Label>("timeLabel");
            _cyclesLabel = root.Q<Label>("cyclesLabel");
            _stepTimeLabel = root.Q<Label>("stepTimeLabel");
            _messagesSentLabel = root.Q<Label>("messagesSentLabel");
            _messagesDeliveredLabel = root.Q<Label>("messagesDeliveredLabel");

            _fullscreenButton = root.Q<VisualElement>("fullscreenButton");
            _quitButton = root.Q<VisualElement>("quitButton");
            _quitPopup = root.Q<VisualElement>("quitPopup");
            _confirmQuitButton = root.Q<Button>("confirmQuitButton");
            _cancelQuitButton = root.Q<Button>("cancelQuitButton");

            if (_runButton != null) _runButton.AddManipulator(_runClickable = new Clickable(OnRun));
            if (_pauseButton != null) _pauseButton.AddManipulator(_pauseClickable = new Clickable(OnPause));
            if (_stopButton != null) _stopButton.AddManipulator(_stopClickable = new Clickable(OnStop));
            
            if (_fullscreenButton != null) _fullscreenButton.AddManipulator(_fullscreenClickable = new Clickable(OnToggleFullscreen));
            if (_quitButton != null) _quitButton.AddManipulator(_quitClickable = new Clickable(OnShowQuitPopup));
            if (_confirmQuitButton != null) _confirmQuitButton.clicked += OnConfirmQuit;
            if (_cancelQuitButton != null) _cancelQuitButton.clicked += OnCancelQuit;

            if (_scenarioButton != null) _scenarioButton.clicked += OnPickScenario;
            if (_behaviorButton != null) _behaviorButton.clicked += OnPickBehavior;
            if (_loadButton != null) _loadButton.clicked += OnLoad;
        }

        private void Start()
        {
            if (simulation != null)
            {
                if (simulation.HasData)
                    SetStatus("Simulação carregada (padrão).");
                else
                    SetStatus("Falha ao carregar padrões. Verifique StreamingAssets.");
            }
        }

        private void OnDisable()
        {
            if (_runButton != null && _runClickable != null) _runButton.RemoveManipulator(_runClickable);
            if (_pauseButton != null && _pauseClickable != null) _pauseButton.RemoveManipulator(_pauseClickable);
            if (_stopButton != null && _stopClickable != null) _stopButton.RemoveManipulator(_stopClickable);
            
            if (_fullscreenButton != null && _fullscreenClickable != null) _fullscreenButton.RemoveManipulator(_fullscreenClickable);
            if (_quitButton != null && _quitClickable != null) _quitButton.RemoveManipulator(_quitClickable);
            if (_confirmQuitButton != null) _confirmQuitButton.clicked -= OnConfirmQuit;
            if (_cancelQuitButton != null) _cancelQuitButton.clicked -= OnCancelQuit;

            if (_scenarioButton != null) _scenarioButton.clicked -= OnPickScenario;
            if (_behaviorButton != null) _behaviorButton.clicked -= OnPickBehavior;
            if (_loadButton != null) _loadButton.clicked -= OnLoad;
        }

        private void OnToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        private void OnShowQuitPopup()
        {
            if (_quitPopup != null)
                _quitPopup.style.display = DisplayStyle.Flex;
        }

        private void OnConfirmQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnCancelQuit()
        {
            if (_quitPopup != null)
                _quitPopup.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (simulation == null) return;

            if (_movementsLabel != null) _movementsLabel.text = $"Movimentos: {simulation.TotalMovements}";
            if (_collisionsLabel != null) _collisionsLabel.text = $"Colisões: {simulation.TotalCollisions}";
            if (_invalidMovementsLabel != null) _invalidMovementsLabel.text = $"Movimentos inválidos: {simulation.TotalInvalidMovements}";
            if (_fpsLabel != null) _fpsLabel.text = $"FPS: {Mathf.RoundToInt(simulation.FrameRate)}";
            if (_timeLabel != null) _timeLabel.text = $"Tempo decorrido: {simulation.SimulationTime:F1}s";
            if (_cyclesLabel != null) _cyclesLabel.text = $"Ciclos: {simulation.CycleCount}";
            if (_stepTimeLabel != null) _stepTimeLabel.text = $"Tempo por ciclo: {simulation.AverageStepTimeMs:F2}ms";
            if (_messagesSentLabel != null) _messagesSentLabel.text = $"Mensagens enviadas: {simulation.TotalMessagesSent}";
            if (_messagesDeliveredLabel != null) _messagesDeliveredLabel.text = $"Mensagens recebidas: {simulation.TotalMessagesDelivered}";
        }

        // ---------- Simulation controls ----------

        private void OnRun()
        {
            if (simulation == null) return;
            simulation.Run();
            SetStatus(simulation.IsFinished
                ? "Simulação encerrada. Use \"Encerrar\" para resetar."
                : "Rodando...");
        }

        private void OnPause()
        {
            if (simulation == null) return;
            simulation.Pause();
            SetStatus("Pausado.");
        }

        private void OnStop()
        {
            if (simulation == null) return;
            simulation.ResetSimulation();
            SetStatus("Resetado para o início.");
        }

        // ---------- File pickers ----------

        private void OnPickScenario()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de cenário (JSON)");
            if (string.IsNullOrEmpty(path))
                return;

            _scenarioPath = path;
            if (_scenarioPathLabel != null)
                _scenarioPathLabel.text = Path.GetFileName(path);
        }

        private void OnPickBehavior()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de comportamento (JSON)");
            if (string.IsNullOrEmpty(path))
                return;

            _behaviorPath = path;
            if (_behaviorPathLabel != null)
                _behaviorPathLabel.text = Path.GetFileName(path);
        }

        private void OnLoad()
        {
            if (simulation == null) return;

            string scenarioPath = string.IsNullOrEmpty(_scenarioPath)
                ? Path.Combine(Application.streamingAssetsPath, simulation.scenarioFileName)
                : _scenarioPath;

            string behaviorPath = string.IsNullOrEmpty(_behaviorPath)
                ? Path.Combine(Application.streamingAssetsPath, simulation.behaviorFileName)
                : _behaviorPath;

            bool ok = simulation.LoadSimulation(scenarioPath, behaviorPath, false);
            SetStatus(ok
                ? "Simulação carregada. Pressione \"Rodar\"."
                : "Falha ao carregar. Verifique os arquivos JSON.");
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
                _statusLabel.text = message;
        }
    }
}
