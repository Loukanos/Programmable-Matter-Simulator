using System.Collections.Generic;
using UnityEngine;

namespace ParticleSim.Core
{
    public class Particle
    {
        private readonly Dictionary<string, object> _internalState = new();
        private readonly List<Message> _inbox = new();

        public int Id { get; }
        public string Type { get; }
        public Vector3Int GridPosition { get; private set; }
        public Vector3Int InitialGridPosition { get; }

        public IReadOnlyList<Message> Inbox => _inbox;

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
