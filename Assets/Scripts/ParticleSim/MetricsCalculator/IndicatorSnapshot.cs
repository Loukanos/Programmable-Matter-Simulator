using System;

namespace ParticleSim.MetricsCalculator
{
    public enum SimulationRunState { Running, Paused, Finished }

    public sealed class IndicatorSnapshot
    {
        public DateTime Timestamp;
        public string ScenarioFile;
        public string BehaviorFile;
        public SimulationRunState State;
        public string FinishReason;
        public int ParticleCount;
        public int Cycles;
        public int? LastMovementCycle;
        public int Movements;
        public int Collisions;
        public int InvalidMovements;
        public int MessagesSent;
        public int MessagesDelivered;
        public double SimulationTimeSeconds;
        public double AverageStepTimeMs;
        public double FrameRate;
    }
}
