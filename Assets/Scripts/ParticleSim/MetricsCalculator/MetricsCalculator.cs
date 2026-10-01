using System;

namespace ParticleSim.MetricsCalculator
{
    public class MetricsCalculator
    {
        public int TotalMovements { get; private set; }
        public float SimulationTime { get; private set; }
        public int CurrentCycle { get; private set; }
        public int? LastMovementCycle { get; private set; }
        public int TotalCollisions { get; private set; }
        public int TotalInvalidMovements { get; private set; }
        public int TotalMessagesSent { get; private set; }
        public int TotalMessagesDelivered { get; private set; }
        public float FrameRate { get; private set; }
        public string FinishReason { get; private set; }

        private double _totalStepTimeSeconds;

        public double AverageStepTimeMs => CurrentCycle > 0 ? _totalStepTimeSeconds / CurrentCycle * 1000.0 : 0.0;

        private float _fpsTimer;
        private int _frameCount;

        private readonly SimulationEngine.SimulationEngine _engine;

        public MetricsCalculator(SimulationEngine.SimulationEngine engine)
        {
            _engine = engine;
            _engine.OnParticleMoved += (_, __) => TotalMovements++;
            _engine.OnCycleCompleted += (cycle, anyMoved) =>
            {
                CurrentCycle = cycle;
                if (anyMoved) LastMovementCycle = cycle;
            };
            _engine.OnCollision += _ => TotalCollisions++;
            _engine.OnInvalidMovement += _ => TotalInvalidMovements++;
            _engine.OnMessageSent += _ => TotalMessagesSent++;
            _engine.OnMessageSendFailed += _ => TotalMessagesSent++;
            _engine.OnMessageDelivered += _ => TotalMessagesDelivered++;
            _engine.OnSimulationFinished += reason => FinishReason = reason;
        }

        public IndicatorSnapshot Capture(DateTime timestamp, string scenarioFile, string behaviorFile, int particleCount) => new IndicatorSnapshot
        {
            Timestamp = timestamp,
            ScenarioFile = scenarioFile,
            BehaviorFile = behaviorFile,
            State = _engine.IsFinished ? SimulationRunState.Finished
                : _engine.IsRunning ? SimulationRunState.Running
                : SimulationRunState.Paused,
            FinishReason = FinishReason,
            ParticleCount = particleCount,
            Cycles = _engine.CurrentCycle,
            LastMovementCycle = LastMovementCycle,
            Movements = TotalMovements,
            Collisions = TotalCollisions,
            InvalidMovements = TotalInvalidMovements,
            MessagesSent = TotalMessagesSent,
            MessagesDelivered = TotalMessagesDelivered,
            SimulationTimeSeconds = SimulationTime,
            AverageStepTimeMs = AverageStepTimeMs,
            FrameRate = FrameRate,
        };

        public void Tick(float deltaTime) => SimulationTime += deltaTime;

        public void RecordStepDuration(double seconds) => _totalStepTimeSeconds += seconds;

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
            LastMovementCycle = null;
            TotalCollisions = 0;
            TotalInvalidMovements = 0;
            TotalMessagesSent = 0;
            TotalMessagesDelivered = 0;
            _totalStepTimeSeconds = 0;
            FrameRate = 0f;
            _fpsTimer = 0f;
            _frameCount = 0;
            FinishReason = null;
        }
    }
}
