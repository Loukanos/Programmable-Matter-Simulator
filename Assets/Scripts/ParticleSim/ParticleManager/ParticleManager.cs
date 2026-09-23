using System.Collections.Generic;
using ParticleSim.Core;
using ParticleSim.InputValidation;
using UnityEngine;

namespace ParticleSim.ParticleManager
{
    public class ParticleManager : IGridQuery
    {
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Dictionary<Vector3Int, Particle> _gridLookup = new Dictionary<Vector3Int, Particle>();
 
        public Vector3Int GridSize { get; private set; }

        public IReadOnlyList<Particle> Particles => _particles;
 
        public void BuildFromScenario(ValidatedSimulation simulation)
        {
            ScenarioData scenario = simulation.Scenario;

            _particles.Clear();
            _gridLookup.Clear();

            GridSize = new Vector3Int(scenario.gridSize[0], scenario.gridSize[1], scenario.gridSize[2]);

            foreach (ParticleData data in scenario.particles)
            {
                var position = new Vector3Int(data.position[0], data.position[1], data.position[2]);
                _particles.Add(new Particle(data.id, data.type, position));
            }

            RefreshGridLookup();
        }
 
        public void RefreshGridLookup()
        {
            _gridLookup.Clear();
            foreach (Particle p in _particles)
                _gridLookup[p.GridPosition] = p;
        }
 
        public bool IsOccupied(Vector3Int position) => _gridLookup.ContainsKey(position);

        public bool TryGetParticleAt(Vector3Int position, out Particle particle) =>
            _gridLookup.TryGetValue(position, out particle);
 
        public Vector3Int ApplyBoundary(Vector3Int position)
        {
            position.x = Mathf.Clamp(position.x, 0, GridSize.x - 1);
            position.y = Mathf.Clamp(position.y, 0, GridSize.y - 1);
            position.z = Mathf.Clamp(position.z, 0, GridSize.z - 1);
            return position;
        }

        public bool IsInBounds(Vector3Int position) =>
            position.x >= 0 && position.x < GridSize.x &&
            position.y >= 0 && position.y < GridSize.y &&
            position.z >= 0 && position.z < GridSize.z;

        public void ResetParticles()
        {
            foreach (Particle p in _particles)
                p.ResetToInitial();
            RefreshGridLookup();
        }
    }
}
