namespace ParticleSim.MetricsCalculator
{
    public class MetricsCalculator
    {
        public int TotalMovements { get; private set; }
        public float SimulationTime { get; private set; }
        public int CurrentCycle { get; private set; }
 
        private readonly SimulationEngine.SimulationEngine _engine;
 
        public MetricsCalculator(SimulationEngine.SimulationEngine engine)
        {
            _engine = engine;
            _engine.OnParticleMoved += (_, __) => TotalMovements++;
            _engine.OnCycleCompleted += (cycle, _) => CurrentCycle = cycle;
        }
 
        /// <summary>Acumula o tempo decorrido de simulação. Deve ser chamado a cada frame pela UI.</summary>
        public void Tick(float deltaTime) => SimulationTime += deltaTime;
 
        public void Reset()
        {
            TotalMovements = 0;
            SimulationTime = 0f;
            CurrentCycle = 0;
        }
    }
}