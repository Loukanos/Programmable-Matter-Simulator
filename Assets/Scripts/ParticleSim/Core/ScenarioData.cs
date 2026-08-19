using System;
using System.Collections.Generic;

namespace ParticleSim.Core
{
    /// <summary>
    /// Single particle entry as defined in the scenario JSON file.
    /// </summary>
    [Serializable]
    public class ParticleData
    {
        public int id;
        public string type;
        public int[] position;
    }

    /// <summary>
    /// Root structure of a scenario JSON file:
    /// grid dimensions plus the list of particles to spawn.
    /// </summary>
    [Serializable]
    public class ScenarioData
    {
        public int[] gridSize;

        /// <summary>
        /// Hard cap on the number of simulation cycles. When reached, the
        /// simulation stops and particles remain at their final position.
        /// 0 (or negative) means "no hard cap".
        /// </summary>
        public int maxCycles = 0;

        /// <summary>
        /// Stop the simulation after this many consecutive cycles in which no
        /// particle changed position (e.g. everyone reached a boundary).
        /// 0 (or negative) disables this check.
        /// </summary>
        public int idleCyclesLimit = 5;

        public List<ParticleData> particles = new();
    }
}
