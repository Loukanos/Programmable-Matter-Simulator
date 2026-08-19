using System.Collections.Generic;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.ParticleManager
{
    /// <summary>
    /// Módulo Gerenciador de Partículas: instancia as partículas na grade
    /// (lattice) a partir dos dados de cenário já validados, mantém a
    /// estrutura de ocupação espacial e resolve consultas de vizinhança e
    /// de contorno. Não conhece GameObjects, materiais, cor ou qualquer
    /// classe de ParticleSim.UI — a interpretação de regras é papel do
    /// Motor de Simulação, que consome esta classe apenas através da
    /// interface IGridQuery.
    /// </summary>
    public class ParticleManager : IGridQuery
    {
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Dictionary<Vector3Int, Particle> _gridLookup = new Dictionary<Vector3Int, Particle>();
 
        public Vector3Int GridSize { get; private set; }
        public BoundaryMode BoundaryMode { get; set; } = BoundaryMode.Wrap;
 
        public IReadOnlyList<Particle> Particles => _particles;
 
        /// <summary>
        /// Limpa o estado atual e instancia uma nova população de
        /// partículas a partir dos dados do cenário validado.
        /// </summary>
        public void BuildFromScenario(ScenarioData scenario)
        {
            _particles.Clear();
            _gridLookup.Clear();
 
            GridSize = new Vector3Int(scenario.gridSize[0], scenario.gridSize[1], scenario.gridSize[2]);
 
            foreach (ParticleData data in scenario.particles)
            {
                if (data.position == null || data.position.Length < 3)
                {
                    Debug.LogWarning($"[ParticleManager] Partícula {data.id} tem posição inválida e foi ignorada.");
                    continue;
                }
 
                var position = new Vector3Int(data.position[0], data.position[1], data.position[2]);
                _particles.Add(new Particle(data.id, data.type, position));
            }
 
            RefreshGridLookup();
        }
 
        /// <summary>Reconstrói o índice espacial a partir da posição atual de cada partícula.</summary>
        public void RefreshGridLookup()
        {
            _gridLookup.Clear();
            foreach (Particle p in _particles)
                _gridLookup[p.GridPosition] = p;
        }
 
        public bool TryGetParticleAt(Vector3Int position, out Particle particle) =>
            _gridLookup.TryGetValue(position, out particle);
 
        public Vector3Int ApplyBoundary(Vector3Int position)
        {
            switch (BoundaryMode)
            {
                case BoundaryMode.Clamp:
                    position.x = Mathf.Clamp(position.x, 0, GridSize.x - 1);
                    position.y = Mathf.Clamp(position.y, 0, GridSize.y - 1);
                    position.z = Mathf.Clamp(position.z, 0, GridSize.z - 1);
                    break;
                case BoundaryMode.Wrap:
                    position.x = Wrap(position.x, GridSize.x);
                    position.y = Wrap(position.y, GridSize.y);
                    position.z = Wrap(position.z, GridSize.z);
                    break;
                case BoundaryMode.None:
                default:
                    break;
            }
            return position;
        }
 
        private static int Wrap(int value, int size)
        {
            if (size <= 0) return value;
            return ((value % size) + size) % size;
        }
 
        /// <summary>Restaura todas as partículas à posição e estado iniciais.</summary>
        public void ResetParticles()
        {
            foreach (Particle p in _particles)
                p.ResetToInitial();
            RefreshGridLookup();
        }
    }
}