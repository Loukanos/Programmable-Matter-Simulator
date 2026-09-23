using System;
using System.Collections.Generic;

namespace ParticleSim.Core
{
    [Serializable]
    public class ParticleData
    {
        public int id;
        public string type;
        public int[] position;
    }

    [Serializable]
    public class ScenarioData
    {
        public int[] gridSize;
        public int maxCycles = 0;
        public int idleCyclesLimit = 5;
        public List<ParticleData> particles = new();
    }
}
