using System.Collections.Generic;
using UnityEngine;

namespace ParticleSim.Core
{
    /// <summary>
    /// Pure particle model used by the simulation core.
    /// No MonoBehaviour dependency.
    /// </summary>
    public class Particle
    {
        private readonly Dictionary<string, object> _internalState = new();
        private readonly List<Message> _inbox = new();

        public int Id { get; }
        public string Type { get; }
        public Vector3Int GridPosition { get; private set; }
        public Vector3Int InitialGridPosition { get; }

        // Deliberadamente não existe um acessor para o dicionário de estado
        // inteiro. O estado interno só é alcançável chave a chave, via
        // GetInternalState, que é como a RN09 ("cada partícula acessa apenas
        // seu próprio estado interno") se sustenta: sem uma view do dicionário
        // completo, não há por onde alguém enumerar o estado de outra
        // partícula caso um dia chegue a ter a referência dela.

        /// <summary>Messages delivered at the start of the current cycle (see SimulationEngine.DeliverPendingMessages).</summary>
        public IReadOnlyList<Message> Inbox => _inbox;

        /// <summary>
        /// Outcome of this particle's move() attempt in the *previous* cycle
        /// (exposed to the declarative language via moveResult()) — set once
        /// per particle at the end of every SimulationEngine.Step(), so it
        /// always reflects the cycle before the one currently being processed.
        /// </summary>
        public MoveOutcome LastMoveResult { get; private set; } = MoveOutcome.NotAttempted;

        public Particle(int id, string type, Vector3Int gridPosition)
        {
            Id = id;
            Type = type;
            GridPosition = gridPosition;
            InitialGridPosition = gridPosition;
        }

        public void SetGridPosition(Vector3Int gridPosition)
        {
            GridPosition = gridPosition;
        }

        public void SetInternalState(string key, object value)
        {
            _internalState[key] = value;
        }

        public object GetInternalState(string key)
        {
            return _internalState.GetValueOrDefault(key);
        }

        /// <summary>Discards the previous cycle's inbox (called at the start of each cycle, before delivering pending messages).</summary>
        public void ClearInbox()
        {
            _inbox.Clear();
        }

        public void ReceiveMessage(Message message)
        {
            _inbox.Add(message);
        }

        public void SetLastMoveResult(MoveOutcome outcome)
        {
            LastMoveResult = outcome;
        }

        public void ResetToInitial()
        {
            GridPosition = InitialGridPosition;
            _internalState.Clear();
            _inbox.Clear();
            LastMoveResult = MoveOutcome.NotAttempted;
        }
    }
}
