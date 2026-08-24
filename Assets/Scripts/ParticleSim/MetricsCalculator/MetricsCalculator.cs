namespace ParticleSim.MetricsCalculator
{
    /// <summary>
    /// Owns every indicator from the TCC's Quadro 7. Everything derived
    /// purely from SimulationEngine events is accumulated by subscribing in
    /// the constructor; the two indicators that depend on wall-clock/frame
    /// timing (SimulationTime, FrameRate, average step time) are instead fed
    /// externally by SimulationController once per frame/step, since only it
    /// knows real elapsed time and run state.
    /// </summary>
    public class MetricsCalculator
    {
        public int TotalMovements { get; private set; }
        public float SimulationTime { get; private set; }
        public int CurrentCycle { get; private set; }
        public int TotalCollisions { get; private set; }
        public int TotalInvalidMovements { get; private set; }
        public int TotalMessagesSent { get; private set; }
        public int TotalMessagesDelivered { get; private set; }
        public float FrameRate { get; private set; }

        private double _totalStepTimeSeconds;

        /// <summary>Average real (wall-clock) time a Step() call took to run, across every cycle executed so far.</summary>
        public double AverageStepTimeMs => CurrentCycle > 0 ? _totalStepTimeSeconds / CurrentCycle * 1000.0 : 0.0;

        private float _fpsTimer;
        private int _frameCount;

        private readonly SimulationEngine.SimulationEngine _engine;

        public MetricsCalculator(SimulationEngine.SimulationEngine engine)
        {
            _engine = engine;
            _engine.OnParticleMoved += (_, __) => TotalMovements++;
            _engine.OnCycleCompleted += (cycle, _) => CurrentCycle = cycle;
            _engine.OnCollision += _ => TotalCollisions++;
            _engine.OnInvalidMovement += _ => TotalInvalidMovements++;
            _engine.OnMessageSent += _ => TotalMessagesSent++;
            _engine.OnMessageDelivered += _ => TotalMessagesDelivered++;
        }

        /// <summary>Acumula o tempo decorrido de simulação. Deve ser chamado a cada frame pela UI.</summary>
        public void Tick(float deltaTime) => SimulationTime += deltaTime;

        /// <summary>Records how long one Step() call actually took to run, to compute the average-per-cycle indicator.</summary>
        public void RecordStepDuration(double seconds) => _totalStepTimeSeconds += seconds;

        /// <summary>Accumulates a 0.5s window of frame count to derive FrameRate. Called every frame regardless of run state, since it reflects render rate, not simulation activity.</summary>
        public void UpdateFrameRate(float unscaledDeltaTime)
        {
            _frameCount++;
            _fpsTimer += unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                FrameRate = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer = 0f;
            }
        }

        public void Reset()
        {
            TotalMovements = 0;
            SimulationTime = 0f;
            CurrentCycle = 0;
            TotalCollisions = 0;
            TotalInvalidMovements = 0;
            TotalMessagesSent = 0;
            TotalMessagesDelivered = 0;
            _totalStepTimeSeconds = 0;
            FrameRate = 0f;
            _fpsTimer = 0f;
            _frameCount = 0;
        }
    }
}
