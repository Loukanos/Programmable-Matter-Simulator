using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ParticleSim.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class SimulationUIController : MonoBehaviour
    {
        private const string HiddenClass = "hidden";

        private const string EmptyFileClass = "file-name--empty";

        private const string NoFileLabel = "(nenhum arquivo)";

        private const string MissingScenarioMessage =
            "Nenhum arquivo de cenário selecionado. Use \"+ Carregar arquivo\" para escolher um.";

        private const string MissingBehaviorMessage =
            "Nenhum arquivo de comportamento selecionado. Use \"+ Carregar arquivo\" para escolher um.";

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        private static readonly Color[] SwatchPalette =
        {
            Rgb(233, 236, 239), Rgb(173, 181, 189), Rgb(108, 117, 125), Rgb(73, 80, 87),    Rgb(43, 48, 53),
            Rgb(217, 83, 79),   Rgb(232, 110, 90),  Rgb(232, 110, 160), Rgb(199, 78, 189),  Rgb(150, 55, 70),
            Rgb(232, 140, 58),  Rgb(240, 173, 78),  Rgb(232, 206, 72),  Rgb(201, 164, 58),  Rgb(150, 150, 60),
            Rgb(150, 205, 70),  Rgb(92, 196, 110),  Rgb(56, 178, 133),  Rgb(45, 168, 160),  Rgb(40, 140, 150),
            Rgb(74, 200, 232),  Rgb(88, 160, 220),  Rgb(77, 126, 168),  Rgb(88, 96, 190),   Rgb(150, 100, 210),
        };

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
        private Button _exportIndicatorsButton;
        private Button _confirmQuitButton;
        private Button _cancelQuitButton;

        private VisualElement _filePanel;
        private VisualElement _quitPopup;
        private ScrollView _scenarioErrorBox;
        private ScrollView _behaviorErrorBox;

        private Slider _stepIntervalSlider;
        private Slider _cubeFillSlider;
        private Slider _moveDurationSlider;
        private Toggle _showGridBoundsToggle;
        private Label _stepIntervalValue;
        private Label _cubeFillValue;
        private Label _moveDurationValue;
        private TextField _stepIntervalInput;
        private TextField _cubeFillInput;
        private TextField _moveDurationInput;

        private readonly List<Action> _valueEditorCleanup = new List<Action>();

        private Button _particleColorButton;
        private Button _gridColorButton;
        private VisualElement _colorPopup;
        private VisualElement _colorPopupSwatches;
        private Label _colorPopupTitle;
        private Button _colorPopupCancelButton;

        private enum ColorTarget { None, Particle, GridBounds }

        private ColorTarget _colorTarget;

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
        private Label _averageFpsLabel;
        private Label _timeLabel;
        private Label _cyclesLabel;
        private Label _lastMovementCycleLabel;
        private Label _stepTimeLabel;
        private Label _messagesSentLabel;
        private Label _messagesDeliveredLabel;

        private string _scenarioPath;
        private string _behaviorPath;
        private float _statsTimer;

        private bool _bound;

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
                Debug.LogError($"[{nameof(SimulationUIController)}] No {nameof(SimulationController)} in the scene. UI disabled.", this);
                enabled = false;
                return;
            }

            if (!Bind())
            {
                Debug.LogError($"[{nameof(SimulationUIController)}] SimulationUI.uxml does not match this script (see the errors above). UI disabled.", this);
                enabled = false;
                return;
            }

            Subscribe();
            _bound = true;

            _scenarioPath = DefaultPathOf(simulation.scenarioFileName);
            _behaviorPath = DefaultPathOf(simulation.behaviorFileName);
            RefreshScenarioRow();
            RefreshBehaviorRow();

            InitializeParameterControls();
            RefreshExportButton();
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
            _exportIndicatorsButton.clicked -= OnExportIndicators;
            simulation.OnSimulationFinished -= ShowFinishReason;

            _stepIntervalSlider.UnregisterValueChangedCallback(OnStepIntervalChanged);
            _cubeFillSlider.UnregisterValueChangedCallback(OnCubeFillChanged);
            _moveDurationSlider.UnregisterValueChangedCallback(OnMoveDurationChanged);
            _showGridBoundsToggle.UnregisterValueChangedCallback(OnShowGridBoundsChanged);

            _particleColorButton.clicked -= OnOpenParticleColorPicker;
            _gridColorButton.clicked -= OnOpenGridColorPicker;
            _colorPopupCancelButton.clicked -= CloseColorPicker;
            _colorPopup.UnregisterCallback<ClickEvent>(OnColorPopupBackgroundClick);

            foreach (Action cleanup in _valueEditorCleanup) cleanup();
            _valueEditorCleanup.Clear();

            _bound = false;
        }

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
            _exportIndicatorsButton = Find<Button>(root, "exportIndicatorsButton");
            _confirmQuitButton = Find<Button>(root, "confirmQuitButton");
            _cancelQuitButton = Find<Button>(root, "cancelQuitButton");

            _filePanel = Find<VisualElement>(root, "filePanel");
            _quitPopup = Find<VisualElement>(root, "quitPopup");
            _scenarioErrorBox = Find<ScrollView>(root, "scenarioErrorBox");
            _behaviorErrorBox = Find<ScrollView>(root, "behaviorErrorBox");

            _stepIntervalSlider = Find<Slider>(root, "stepIntervalSlider");
            _cubeFillSlider = Find<Slider>(root, "cubeFillSlider");
            _moveDurationSlider = Find<Slider>(root, "moveDurationSlider");
            _showGridBoundsToggle = Find<Toggle>(root, "showGridBoundsToggle");
            _particleColorButton = Find<Button>(root, "particleColorButton");
            _gridColorButton = Find<Button>(root, "gridColorButton");
            _colorPopup = Find<VisualElement>(root, "colorPopup");
            _colorPopupSwatches = Find<VisualElement>(root, "colorPopupSwatches");
            _colorPopupTitle = Find<Label>(root, "colorPopupTitle");
            _colorPopupCancelButton = Find<Button>(root, "colorPopupCancelButton");
            _stepIntervalValue = Find<Label>(root, "stepIntervalValue");
            _cubeFillValue = Find<Label>(root, "cubeFillValue");
            _moveDurationValue = Find<Label>(root, "moveDurationValue");
            _stepIntervalInput = Find<TextField>(root, "stepIntervalInput");
            _cubeFillInput = Find<TextField>(root, "cubeFillInput");
            _moveDurationInput = Find<TextField>(root, "moveDurationInput");

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
            _averageFpsLabel = Find<Label>(root, "averageFpsLabel");
            _timeLabel = Find<Label>(root, "timeLabel");
            _cyclesLabel = Find<Label>(root, "cyclesLabel");
            _lastMovementCycleLabel = Find<Label>(root, "lastMovementCycleLabel");
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
                Debug.LogError($"[{nameof(SimulationUIController)}] '{elementName}' ({typeof(T).Name}) not found in SimulationUI.uxml.", this);
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
            _exportIndicatorsButton.clicked += OnExportIndicators;
            simulation.OnSimulationFinished += ShowFinishReason;

            _stepIntervalSlider.RegisterValueChangedCallback(OnStepIntervalChanged);
            _cubeFillSlider.RegisterValueChangedCallback(OnCubeFillChanged);
            _moveDurationSlider.RegisterValueChangedCallback(OnMoveDurationChanged);
            _showGridBoundsToggle.RegisterValueChangedCallback(OnShowGridBoundsChanged);

            _particleColorButton.clicked += OnOpenParticleColorPicker;
            _gridColorButton.clicked += OnOpenGridColorPicker;
            _colorPopupCancelButton.clicked += CloseColorPicker;
            _colorPopup.RegisterCallback<ClickEvent>(OnColorPopupBackgroundClick);

            WireValueEditor(_stepIntervalValue, _stepIntervalInput, _stepIntervalSlider, 1f);
            WireValueEditor(_cubeFillValue, _cubeFillInput, _cubeFillSlider, 100f);
            WireValueEditor(_moveDurationValue, _moveDurationInput, _moveDurationSlider, 1f);
        }

        private void WireValueEditor(Label display, TextField input, Slider slider, float displayScale)
        {
            EventCallback<ClickEvent> onDisplayClick = _ =>
            {
                input.SetValueWithoutNotify((slider.value * displayScale).ToString("0.##", CultureInfo.InvariantCulture));
                display.AddToClassList(HiddenClass);
                input.RemoveFromClassList(HiddenClass);

                // Deferred: focusing an element in the same frame it is shown doesn't take.
                input.schedule.Execute(() => input.Focus());
            };

            EventCallback<KeyDownEvent> onKeyDown = evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CommitValueEdit(display, input, slider, displayScale);
            };

            EventCallback<FocusOutEvent> onFocusOut = _ => CommitValueEdit(display, input, slider, displayScale);

            display.RegisterCallback(onDisplayClick);
            input.RegisterCallback(onKeyDown);
            input.RegisterCallback(onFocusOut);

            _valueEditorCleanup.Add(() =>
            {
                display.UnregisterCallback(onDisplayClick);
                input.UnregisterCallback(onKeyDown);
                input.UnregisterCallback(onFocusOut);
            });
        }

        private void CommitValueEdit(Label display, TextField input, Slider slider, float displayScale)
        {
            string typed = input.value.Trim().Replace(',', '.');
            if (float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                slider.value = parsed / displayScale;

            input.AddToClassList(HiddenClass);
            display.RemoveFromClassList(HiddenClass);
        }

        private void InitializeParameterControls()
        {
            _stepIntervalSlider.SetValueWithoutNotify(simulation.stepInterval);
            _cubeFillSlider.SetValueWithoutNotify(simulation.cubeFill);
            _moveDurationSlider.SetValueWithoutNotify(simulation.moveAnimationDuration);
            _showGridBoundsToggle.SetValueWithoutNotify(simulation.showGridBounds);

            UpdateStepIntervalValue(simulation.stepInterval);
            UpdateCubeFillValue(simulation.cubeFill);
            UpdateMoveDurationValue(simulation.moveAnimationDuration);

            _particleColorButton.style.backgroundColor = Opaque(simulation.particleColor);
            _gridColorButton.style.backgroundColor = Opaque(simulation.gridBoundsColor);

            BuildSwatches();
        }

        private void BuildSwatches()
        {
            // OnEnable can run more than once; without this each run stacks another full grid.
            _colorPopupSwatches.Clear();

            foreach (Color color in SwatchPalette)
            {
                Color swatchColor = color;
                var swatch = new Button(() => OnPickColor(swatchColor));
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = swatchColor;
                _colorPopupSwatches.Add(swatch);
            }
        }

        private static Color Opaque(Color color)
        {
            return new Color(color.r, color.g, color.b, 1f);
        }

        private void OnToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        private void OnReframe()
        {
            simulation.FrameCamera();
            SetStatus("Câmera reenquadrada.");
        }

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

        private void OnStepIntervalChanged(ChangeEvent<float> evt)
        {
            simulation.SetStepInterval(evt.newValue);
            UpdateStepIntervalValue(evt.newValue);
        }

        private void OnCubeFillChanged(ChangeEvent<float> evt)
        {
            simulation.SetCubeFill(evt.newValue);
            UpdateCubeFillValue(evt.newValue);
        }

        private void OnMoveDurationChanged(ChangeEvent<float> evt)
        {
            simulation.SetMoveAnimationDuration(evt.newValue);
            UpdateMoveDurationValue(evt.newValue);
        }

        private void OnShowGridBoundsChanged(ChangeEvent<bool> evt)
        {
            simulation.SetShowGridBounds(evt.newValue);
        }

        private void OnOpenParticleColorPicker() => OpenColorPicker(ColorTarget.Particle, "Cor das partículas");

        private void OnOpenGridColorPicker() => OpenColorPicker(ColorTarget.GridBounds, "Cor dos limites da grade");

        private void OpenColorPicker(ColorTarget target, string title)
        {
            _colorTarget = target;
            _colorPopupTitle.text = title;
            _colorPopup.RemoveFromClassList(HiddenClass);
        }

        private void CloseColorPicker()
        {
            _colorTarget = ColorTarget.None;
            _colorPopup.AddToClassList(HiddenClass);
        }

        private void OnColorPopupBackgroundClick(ClickEvent evt)
        {
            if (evt.target == _colorPopup)
                CloseColorPicker();
        }

        private void OnPickColor(Color color)
        {
            switch (_colorTarget)
            {
                case ColorTarget.Particle:
                    simulation.SetParticleColor(color);
                    _particleColorButton.style.backgroundColor = Opaque(color);
                    break;

                case ColorTarget.GridBounds:
                    float alpha = simulation.gridBoundsColor.a;
                    simulation.SetGridBoundsColor(new Color(color.r, color.g, color.b, alpha));
                    _gridColorButton.style.backgroundColor = Opaque(color);
                    break;
            }

            CloseColorPicker();
        }

        private void UpdateStepIntervalValue(float seconds) =>
            _stepIntervalValue.text = seconds <= 0f ? "1 ciclo/quadro" : $"{seconds:F2} s";

        private void UpdateMoveDurationValue(float seconds) =>
            _moveDurationValue.text = seconds <= 0f ? "instantâneo" : $"{seconds:F2} s";

        private void UpdateCubeFillValue(float fill) => _cubeFillValue.text = $"{Mathf.RoundToInt(fill * 100f)}%";

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
            _averageFpsLabel.text = $"FPS médio: {simulation.AverageFrameRate?.ToString("F1") ?? "—"}";
            _timeLabel.text = $"Tempo decorrido: {simulation.SimulationTime:F1}s";
            _cyclesLabel.text = $"Ciclos: {simulation.CycleCount}";
            _lastMovementCycleLabel.text = $"Ciclo do último movimento: {simulation.LastMovementCycle?.ToString() ?? "—"}";
            _stepTimeLabel.text = $"Tempo por ciclo: {simulation.AverageStepTimeMs:F2}ms";
            _messagesSentLabel.text = $"Mensagens enviadas: {simulation.TotalMessagesSent}";
            _messagesDeliveredLabel.text = $"Mensagens recebidas: {simulation.TotalMessagesDelivered}";
            RefreshExportButton();
        }

        private void RefreshExportButton()
        {
            if (_exportIndicatorsButton.enabledSelf != simulation.HasData)
                _exportIndicatorsButton.SetEnabled(simulation.HasData);
        }

        private void OnExportIndicators()
        {
            // Snapshot first: the row must describe the moment of the click, not of the file choice.
            MetricsCalculator.IndicatorSnapshot snapshot = simulation.CaptureIndicators();
            string suggestedName = MetricsCalculator.IndicatorsCsv.SuggestedFileName(snapshot.ScenarioFile, snapshot.BehaviorFile, snapshot.Timestamp);
            string path = StandaloneFileDialog.SaveFile("Baixar indicadores (CSV)", suggestedName,
                "Planilha CSV\0*.csv\0Todos os arquivos\0*.*\0\0", "csv");
            if (path == null) return;

            SetStatus(MetricsCalculator.IndicatorsCsv.TryAppend(path, snapshot, out string error)
                ? $"Indicadores gravados em {path}"
                : $"Não foi possível gravar os indicadores: {error}");
        }

        private void OnRun()
        {
            simulation.Run();
            SetStatus(simulation.IsFinished
                ? "Simulação encerrada. Use \"Encerrar\" para resetar."
                : "Rodando...");
        }

        private void ShowFinishReason(string reason) => SetStatus($"Simulação encerrada: {reason}.");

        private void OnPause()
        {
            if (simulation.IsFinished) return;
            simulation.Pause();
            SetStatus("Pausado.");
        }

        private void OnStop()
        {
            simulation.ResetSimulation();
            SetStatus("Resetado para o início.");
        }

        private static string DefaultPathOf(string fileName) =>
            Path.Combine(Application.streamingAssetsPath, fileName);

        private void OnPickScenario()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de cenário (JSON)");
            if (string.IsNullOrEmpty(path)) return;

            _scenarioPath = path;
            RefreshScenarioRow();
        }

        private void OnPickBehavior()
        {
            string path = StandaloneFileDialog.OpenFile("Selecionar arquivo de comportamento (JSON)");
            if (string.IsNullOrEmpty(path)) return;

            _behaviorPath = path;
            RefreshBehaviorRow();
        }

        private void OnClearScenario()
        {
            _scenarioPath = null;
            RefreshScenarioRow();
        }

        private void OnClearBehavior()
        {
            _behaviorPath = null;
            RefreshBehaviorRow();
        }

        private void RefreshScenarioRow() =>
            RefreshFileRow(_scenarioPathLabel, _scenarioXButton, _scenarioPath);

        private void RefreshBehaviorRow() =>
            RefreshFileRow(_behaviorPathLabel, _behaviorXButton, _behaviorPath);

        private static void RefreshFileRow(Label label, Button clearButton, string path)
        {
            bool hasFile = !string.IsNullOrEmpty(path);

            label.text = hasFile ? Path.GetFileName(path) : NoFileLabel;
            label.EnableInClassList(EmptyFileClass, !hasFile);
            clearButton.SetEnabled(hasFile);
        }

        private void OnLoad()
        {
            bool scenarioMissing = string.IsNullOrEmpty(_scenarioPath);
            bool behaviorMissing = string.IsNullOrEmpty(_behaviorPath);

            if (scenarioMissing || behaviorMissing)
            {
                ApplyError(_scenarioErrorBox, _scenarioErrorLabel, scenarioMissing ? MissingScenarioMessage : null);
                ApplyError(_behaviorErrorBox, _behaviorErrorLabel, behaviorMissing ? MissingBehaviorMessage : null);
                SetStatus("Falta selecionar arquivo. Verifique os erros abaixo.");
                return;
            }

            bool ok = simulation.LoadSimulation(_scenarioPath, _behaviorPath, false);
            SetStatus(ok
                ? "Simulação carregada. Pressione \"Rodar\"."
                : "Falha ao carregar. Verifique os erros abaixo.");

            ShowLoadErrors();
        }

        private void SetStatus(string message)
        {
            _statusLabel.text = message;
        }

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
