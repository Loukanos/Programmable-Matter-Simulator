using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>USS class marking a file slot with nothing selected.</summary>
        private const string EmptyFileClass = "file-name--empty";

        private const string NoFileLabel = "(nenhum arquivo)";

        private const string MissingScenarioMessage =
            "Nenhum arquivo de cenário selecionado. Use \"+ Carregar arquivo\" para escolher um.";

        private const string MissingBehaviorMessage =
            "Nenhum arquivo de comportamento selecionado. Use \"+ Carregar arquivo\" para escolher um.";

        /// <summary>0-255 components, so the palette below reads like the hex
        /// values a colour is usually picked as rather than as float triplets.</summary>
        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        /// <summary>
        /// Palette offered by the colour picker. Kept here, in one place,
        /// rather than as a USS class per colour plus a matching C# Color —
        /// the swatch buttons are built from this array, so adding or removing
        /// a colour is a single edit with nothing else to keep in sync.
        /// Laid out as rows of five (neutrals, reds, yellows, greens, blues)
        /// purely so the grid reads as organised; the grid itself just wraps,
        /// so the count doesn't have to stay a multiple of five.
        /// </summary>
        private static readonly Color[] SwatchPalette =
        {
            Rgb(233, 236, 239), Rgb(173, 181, 189), Rgb(108, 117, 125), Rgb(73, 80, 87),    Rgb(43, 48, 53),
            Rgb(217, 83, 79),   Rgb(232, 110, 90),  Rgb(232, 110, 160), Rgb(199, 78, 189),  Rgb(150, 55, 70),
            Rgb(232, 140, 58),  Rgb(240, 173, 78),  Rgb(232, 206, 72),  Rgb(201, 164, 58),  Rgb(150, 150, 60),
            Rgb(150, 205, 70),  Rgb(92, 196, 110),  Rgb(56, 178, 133),  Rgb(45, 168, 160),  Rgb(40, 140, 150),
            Rgb(74, 200, 232),  Rgb(88, 160, 220),  Rgb(77, 126, 168),  Rgb(88, 96, 190),   Rgb(150, 100, 210),
        };

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

        /// <summary>
        /// Undo actions for the click-to-type editors. Those are wired with
        /// closures (one per slider, capturing its own label/field/scale), so
        /// unlike the `clicked` handlers there's no named method to pass to
        /// Unregister — each registration pushes the matching removal here and
        /// <see cref="OnDisable"/> runs them, keeping teardown symmetric.
        /// </summary>
        private readonly List<Action> _valueEditorCleanup = new List<Action>();

        private Button _particleColorButton;
        private Button _gridColorButton;
        private VisualElement _colorPopup;
        private VisualElement _colorPopupSwatches;
        private Label _colorPopupTitle;
        private Button _colorPopupCancelButton;

        /// <summary>Which setting the shared colour popup is currently editing.</summary>
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
        private Label _timeLabel;
        private Label _cyclesLabel;
        private Label _stepTimeLabel;
        private Label _messagesSentLabel;
        private Label _messagesDeliveredLabel;

        /// <summary>
        /// The file each slot will load, or null when the slot was cleared with
        /// its ✕. These are seeded with the StreamingAssets defaults at bind
        /// time rather than left null and defaulted at load time, which is what
        /// lets null mean "deliberately removed" — clearing a slot genuinely
        /// empties it instead of quietly restoring the default file.
        /// </summary>
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

            // Seeded to match what SimulationController itself loads on Start.
            _scenarioPath = DefaultPathOf(simulation.scenarioFileName);
            _behaviorPath = DefaultPathOf(simulation.behaviorFileName);
            RefreshScenarioRow();
            RefreshBehaviorRow();

            InitializeParameterControls();
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

            _stepIntervalSlider.RegisterValueChangedCallback(OnStepIntervalChanged);
            _cubeFillSlider.RegisterValueChangedCallback(OnCubeFillChanged);
            _moveDurationSlider.RegisterValueChangedCallback(OnMoveDurationChanged);
            _showGridBoundsToggle.RegisterValueChangedCallback(OnShowGridBoundsChanged);

            _particleColorButton.clicked += OnOpenParticleColorPicker;
            _gridColorButton.clicked += OnOpenGridColorPicker;
            _colorPopupCancelButton.clicked += CloseColorPicker;
            _colorPopup.RegisterCallback<ClickEvent>(OnColorPopupBackgroundClick);

            // displayScale converts between what the slider stores and what the
            // reader types: the two durations are shown in the same unit they're
            // stored in, while cube fill is shown as a percentage, so typing
            // "73" there means 0.73.
            WireValueEditor(_stepIntervalValue, _stepIntervalInput, _stepIntervalSlider, 1f);
            WireValueEditor(_cubeFillValue, _cubeFillInput, _cubeFillSlider, 100f);
            WireValueEditor(_moveDurationValue, _moveDurationInput, _moveDurationSlider, 1f);
        }

        /// <summary>
        /// Makes a slider's readout click-to-edit: clicking the label swaps in
        /// a text field holding the raw number, and Enter (or clicking away)
        /// writes it back. The value is applied by assigning to the slider
        /// rather than by calling the SimulationController directly, so typing
        /// goes through the exact same path as dragging — including the
        /// slider's own clamping to its range, and the change handler that
        /// refreshes the label.
        /// </summary>
        private void WireValueEditor(Label display, TextField input, Slider slider, float displayScale)
        {
            EventCallback<ClickEvent> onDisplayClick = _ =>
            {
                input.SetValueWithoutNotify((slider.value * displayScale).ToString("0.##", CultureInfo.InvariantCulture));
                display.AddToClassList(HiddenClass);
                input.RemoveFromClassList(HiddenClass);

                // Scheduled rather than immediate: the field is being shown in
                // this same frame, and focusing an element whose layout hasn't
                // been resolved yet doesn't take.
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

        /// <summary>
        /// Parses what was typed and hands it to the slider. Accepts both
        /// decimal separators — the UI is in Portuguese, so "0,5" is at least
        /// as likely to be typed as "0.5", and rejecting it would read as the
        /// field being broken. Unparseable text is simply discarded, leaving
        /// the previous value untouched.
        /// </summary>
        private void CommitValueEdit(Label display, TextField input, Slider slider, float displayScale)
        {
            string typed = input.value.Trim().Replace(',', '.');
            if (float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                slider.value = parsed / displayScale;

            input.AddToClassList(HiddenClass);
            display.RemoveFromClassList(HiddenClass);
        }

        /// <summary>
        /// Seeds the parameter controls from the values the
        /// <see cref="SimulationController"/> currently holds (its serialized
        /// defaults, or whatever the Inspector was set to), and builds the two
        /// colour pickers. Uses SetValueWithoutNotify so seeding doesn't fire
        /// the change handlers and write the values straight back.
        /// </summary>
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

        /// <summary>
        /// Fills the shared colour popup from <see cref="SwatchPalette"/>. One
        /// grid serves both settings — which one a pick applies to is decided
        /// by <see cref="_colorTarget"/> when the popup opens — so adding a
        /// colour to the palette is a single array entry with nothing else to
        /// update, and the grid wraps to however many there are.
        /// </summary>
        private void BuildSwatches()
        {
            // Cleared first: OnEnable can run more than once over a component's
            // life, and without this each cycle would stack another full grid.
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

        /// <summary>
        /// The colour buttons are swatches, so they show the chosen hue at full
        /// opacity even when the value itself is translucent (the grid
        /// wireframe's is) — a half-transparent button would read as a
        /// different colour against the panel rather than as the colour in use.
        /// </summary>
        private static Color Opaque(Color color)
        {
            return new Color(color.r, color.g, color.b, 1f);
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

        // ---------- Live parameters ----------
        // Each of these applies immediately, mid-run, through a
        // SimulationController setter that re-applies the value to what's
        // already on screen. None of them touch simulation state, so none can
        // change a scenario's outcome — they're presentation and pacing only.
        // (stepInterval changes *when* the next cycle runs, never what it does.)

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

        /// <summary>
        /// Closes the picker when the click landed on the dimmed backdrop
        /// rather than inside the dialog. The target check is what keeps a
        /// click on a swatch (which bubbles up to this same overlay) from
        /// being treated as a click-outside.
        /// </summary>
        private void OnColorPopupBackgroundClick(ClickEvent evt)
        {
            if (evt.target == _colorPopup)
                CloseColorPicker();
        }

        /// <summary>
        /// Applies the picked colour to whichever setting opened the popup,
        /// then closes it. For the grid wireframe only the RGB is replaced —
        /// its configured alpha is kept, since that transparency is part of how
        /// the outline reads as a background element rather than competing with
        /// the particles.
        /// </summary>
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

        // Both of these really are in seconds: stepInterval is compared against
        // a timer accumulating Time.deltaTime in SimulationController.Update,
        // and moveAnimationDuration divides an equally deltaTime-fed timer in
        // ParticleView.Update. Zero is valid and safe at both ends — the step
        // loop is an `if`, not a `while`, so a zero interval means "one cycle
        // per frame" rather than a runaway loop, and a zero animation makes
        // ParticleView place the cube directly on its target cell. Zero reads
        // as nothing useful spelled "0,00 s", so each end gets a word.
        private void UpdateStepIntervalValue(float seconds) =>
            _stepIntervalValue.text = seconds <= 0f ? "1 ciclo/quadro" : $"{seconds:F2} s";

        private void UpdateMoveDurationValue(float seconds) =>
            _moveDurationValue.text = seconds <= 0f ? "instantâneo" : $"{seconds:F2} s";

        private void UpdateCubeFillValue(float fill) => _cubeFillValue.text = $"{Mathf.RoundToInt(fill * 100f)}%";

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

        /// <summary>
        /// Shows the selected file's name, or the empty-slot placeholder when
        /// it was cleared. The ✕ is disabled while a slot is already empty,
        /// so the control reads as "there is nothing here to remove" rather
        /// than looking available but doing nothing.
        /// </summary>
        private static void RefreshFileRow(Label label, Button clearButton, string path)
        {
            bool hasFile = !string.IsNullOrEmpty(path);

            label.text = hasFile ? Path.GetFileName(path) : NoFileLabel;
            label.EnableInClassList(EmptyFileClass, !hasFile);
            clearButton.SetEnabled(hasFile);
        }

        /// <summary>
        /// A cleared slot has no file to load, so loading is refused before the
        /// controller is involved at all and the reason is written into that
        /// slot's own error box — the same surface a validation failure uses,
        /// so "you removed this file" and "this file is malformed" show up in
        /// the same place. The other box is cleared rather than left showing a
        /// stale validation error, since nothing was actually re-read.
        /// </summary>
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
