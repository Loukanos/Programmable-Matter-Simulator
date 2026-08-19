using System;
using System.Collections.Generic;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Motor de Simulação: processa os ciclos discretos (coleta de
    /// intenções, resolução de conflitos, aplicação de ações),
    /// interpretando as regras declarativas de cada tipo de partícula.
    /// Depende do ParticleManager apenas através da interface IGridQuery e
    /// da lista de partículas — nunca referencia ParticleSim.UI.
    /// Movimento e mudança de cor são comunicados por eventos, para que a
    /// camada de visualização se atualize sem gerar dependência reversa.
    /// </summary>
    public class SimulationEngine
    {
        private readonly ParticleManager.ParticleManager _particleManager;
        private Dictionary<string, TypeBehavior> _behaviors;
 
        public int CurrentCycle { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsRunning { get; private set; }
 
        public int MaxCycles { get; set; }
        public int IdleCyclesLimit { get; set; } = 5;
 
        private int _idleCycles;
 
        /// <summary>Disparado ao final de cada ciclo, com o número do ciclo e se houve movimento.</summary>
        public event Action<int, bool> OnCycleCompleted;
        /// <summary>Disparado quando uma partícula muda de posição (já aplicada ao modelo).</summary>
        public event Action<Particle, Vector3Int> OnParticleMoved;
        /// <summary>Disparado quando uma ação setColor é processada para uma partícula.</summary>
        public event Action<Particle, Color> OnColorChanged;
        /// <summary>Disparado quando a simulação encerra por um critério de parada.</summary>
        public event Action<string> OnSimulationFinished;
 
        public SimulationEngine(ParticleManager.ParticleManager particleManager)
        {
            _particleManager = particleManager;
        }
 
        public void SetBehaviors(Dictionary<string, TypeBehavior> behaviors) => _behaviors = behaviors;
 
        public void Reset()
        {
            CurrentCycle = 0;
            _idleCycles = 0;
            IsFinished = false;
            IsRunning = false;
        }
 
        public void ExecuteOnInit()
        {
            _particleManager.RefreshGridLookup();
            foreach (Particle particle in _particleManager.Particles)
            {
                if (_behaviors != null && _behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
                    ProcessActions(particle, behavior.onInit, isOnInit: true);
            }
        }
 
        public void Run() { if (!IsFinished) IsRunning = true; }
        public void Pause() => IsRunning = false;
 
        /// <summary>Avança a simulação em exatamente um ciclo discreto.</summary>
        public void Step()
        {
            if (IsFinished || !IsRunning) return;
 
            _particleManager.RefreshGridLookup();
 
            var desiredPositions = new Dictionary<Particle, Vector3Int>();
            foreach (Particle particle in _particleManager.Particles)
            {
                desiredPositions[particle] = _behaviors != null &&
                    _behaviors.TryGetValue(particle.Type, out TypeBehavior behavior)
                        ? ProcessActions(particle, behavior.onStep, isOnInit: false)
                        : particle.GridPosition;
            }
 
            Dictionary<Particle, Vector3Int> finalPositions = ResolveCollisions(desiredPositions);
 
            bool anyMoved = false;
            foreach (var pair in finalPositions)
            {
                if (pair.Value != pair.Key.GridPosition)
                {
                    pair.Key.SetGridPosition(pair.Value);
                    OnParticleMoved?.Invoke(pair.Key, pair.Value);
                    anyMoved = true;
                }
            }
 
            CurrentCycle++;
            _idleCycles = anyMoved ? 0 : _idleCycles + 1;
 
            OnCycleCompleted?.Invoke(CurrentCycle, anyMoved);
            CheckStopConditions();
        }
 
        private Vector3Int ProcessActions(Particle particle, List<ActionBlock> blocks, bool isOnInit)
        {
            Vector3Int position = particle.GridPosition;
            if (blocks == null) return position;
 
            foreach (ActionBlock block in blocks)
            {
                if (block?.actions == null) continue;
                if (!ConditionEvaluator.Evaluate(block.condition, particle, _particleManager)) continue;
 
                foreach (string action in block.actions)
                {
                    if (string.IsNullOrWhiteSpace(action)) continue;
 
                    if (MoveAction.TryParse(action, out Vector3Int delta))
                    {
                        // Movimentos declarados em onInit não deslocam a partícula
                        // (mesmo comportamento do SimulationManager original).
                        if (!isOnInit)
                            position = _particleManager.ApplyBoundary(position + delta);
                    }
                    else if (SetStateAction.TryParse(action, out string key, out object value))
                    {
                        particle.SetInternalState(key, value);
                    }
                    else if (SetColorAction.TryParse(action, out Color color))
                    {
                        OnColorChanged?.Invoke(particle, color);
                    }
                    else
                    {
                        Debug.LogWarning($"[SimulationEngine] Ação não reconhecida: '{action}'");
                    }
                }
            }
            return position;
        }
 
        private Dictionary<Particle, Vector3Int> ResolveCollisions(Dictionary<Particle, Vector3Int> desiredPositions)
        {
            var finalPositions = new Dictionary<Particle, Vector3Int>();
            var willMove = new Dictionary<Particle, bool>();
 
            foreach (Particle p in _particleManager.Particles)
                willMove[p] = desiredPositions[p] != p.GridPosition;
 
            // Duas partículas não podem disputar a mesma célula de destino.
            var requestCount = new Dictionary<Vector3Int, int>();
            foreach (Vector3Int pos in desiredPositions.Values)
            {
                if (!requestCount.ContainsKey(pos)) requestCount[pos] = 0;
                requestCount[pos]++;
            }
            foreach (Particle p in _particleManager.Particles)
                if (willMove[p] && requestCount[desiredPositions[p]] > 1)
                    willMove[p] = false;
 
            // Uma partícula não pode mover para a célula de outra que ficou parada.
            bool changed;
            do
            {
                changed = false;
                foreach (Particle p in _particleManager.Particles)
                {
                    if (!willMove[p]) continue;
                    Vector3Int target = desiredPositions[p];
                    if (_particleManager.TryGetParticleAt(target, out Particle occupant) && !willMove[occupant])
                    {
                        willMove[p] = false;
                        changed = true;
                    }
                }
            } while (changed);
 
            foreach (Particle p in _particleManager.Particles)
                finalPositions[p] = willMove[p] ? desiredPositions[p] : p.GridPosition;
 
            return finalPositions;
        }
 
        private void CheckStopConditions()
        {
            if (MaxCycles > 0 && CurrentCycle >= MaxCycles)
            {
                Finish($"atingiu o limite de {MaxCycles} ciclos");
                return;
            }
            if (IdleCyclesLimit > 0 && _idleCycles >= IdleCyclesLimit)
            {
                Finish($"sem movimento por {_idleCycles} ciclo(s)");
            }
        }
 
        private void Finish(string reason)
        {
            IsFinished = true;
            IsRunning = false;
            OnSimulationFinished?.Invoke(reason);
        }
    }
}