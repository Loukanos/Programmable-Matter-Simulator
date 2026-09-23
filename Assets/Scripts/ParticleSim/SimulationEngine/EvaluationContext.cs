using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    public class EvaluationContext
    {
        public Particle Particle;
        public IGridQuery Grid;

        public Message MatchedMessage;

        public Message CurrentMessage;
    }
}
