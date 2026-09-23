using System;
using System.Collections.Generic;
using System.Globalization;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
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

        private readonly Dictionary<Particle, List<Message>> _pendingMessages = new();

        private readonly HashSet<Particle> _invalidMovesThisCycle = new();
        private readonly HashSet<Particle> _collisionBlockedThisCycle = new();

        public event Action<int, bool> OnCycleCompleted;
        public event Action<Particle, Vector3Int> OnParticleMoved;
        public event Action<Particle, Color> OnColorChanged;
        public event Action<string> OnSimulationFinished;
        public event Action<Message> OnMessageSent;
        public event Action<Particle> OnMessageSendFailed;
        public event Action<Message> OnMessageDelivered;
        public event Action<Particle> OnCollision;
        public event Action<Particle> OnInvalidMovement;

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
            _pendingMessages.Clear();
        }

        public void ExecuteOnInit()
        {
            _particleManager.RefreshGridLookup();
            foreach (Particle particle in _particleManager.Particles)
            {
                if (_behaviors != null && _behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
                {
                    bool moveConsumed = false;
                    bool anyActivity = false;
                    ProcessActions(particle, behavior.onInit, isOnInit: true, ref moveConsumed, ref anyActivity);
                }
            }
        }

        public void Run() { if (!IsFinished) IsRunning = true; }
        public void Pause() => IsRunning = false;

        public void Step()
        {
            if (IsFinished || !IsRunning) return;

            DeliverPendingMessages();
            _particleManager.RefreshGridLookup();
            _invalidMovesThisCycle.Clear();
            _collisionBlockedThisCycle.Clear();

            var desiredPositions = new Dictionary<Particle, Vector3Int>();
            var attemptedMove = new Dictionary<Particle, bool>();
            bool anyActivity = false;
            foreach (Particle particle in _particleManager.Particles)
            {
                Vector3Int position = particle.GridPosition;
                bool moveConsumed = false;
                if (_behaviors != null && _behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
                {
                    // Order matters: onMessage runs before onTouch and onStep so a message can affect this same cycle's movement.
                    foreach (OnMessageEvent messageEvent in behavior.onMessage)
                    {
                        foreach (Message message in ConditionEvaluator.MatchingMessages(particle, messageEvent.direction))
                            position = ProcessActions(particle, messageEvent.blocks, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position, currentMessage: message);
                    }

                    foreach (OnTouchEvent touchEvent in behavior.onTouch)
                    {
                        if (!ConditionEvaluator.HasNeighbor(particle, _particleManager, touchEvent.direction)) continue;
                        position = ProcessActions(particle, touchEvent.blocks, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position);
                    }

                    position = ProcessActions(particle, behavior.onStep, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position);
                }
                desiredPositions[particle] = position;
                attemptedMove[particle] = moveConsumed;
            }

            Dictionary<Particle, Vector3Int> finalPositions = ResolveCollisions(desiredPositions);

            bool anyMoved = false;
            var actuallyMoved = new HashSet<Particle>();
            foreach (var pair in finalPositions)
            {
                if (pair.Value != pair.Key.GridPosition)
                {
                    pair.Key.SetGridPosition(pair.Value);
                    OnParticleMoved?.Invoke(pair.Key, pair.Value);
                    anyMoved = true;
                    actuallyMoved.Add(pair.Key);
                }
            }

            foreach (Particle particle in _particleManager.Particles)
                particle.SetLastMoveResult(DetermineMoveOutcome(particle, attemptedMove, actuallyMoved));

            CurrentCycle++;
            _idleCycles = (anyMoved || anyActivity) ? 0 : _idleCycles + 1;

            OnCycleCompleted?.Invoke(CurrentCycle, anyMoved);
            CheckStopConditions();
        }

        private MoveOutcome DetermineMoveOutcome(Particle particle, Dictionary<Particle, bool> attemptedMove, HashSet<Particle> actuallyMoved)
        {
            if (!attemptedMove.TryGetValue(particle, out bool attempted) || !attempted)
                return MoveOutcome.NotAttempted;
            if (actuallyMoved.Contains(particle))
                return MoveOutcome.Moved;
            if (_invalidMovesThisCycle.Contains(particle))
                return MoveOutcome.Invalid;
            if (_collisionBlockedThisCycle.Contains(particle))
                return MoveOutcome.BlockedByCollision;

            return MoveOutcome.Moved;
        }

        private Vector3Int ProcessActions(Particle particle, List<ActionBlock> blocks, bool isOnInit,
            ref bool moveConsumed, ref bool anyActivity, Vector3Int? startPosition = null, Message currentMessage = null)
        {
            Vector3Int position = startPosition ?? particle.GridPosition;
            if (blocks == null) return position;

            foreach (ActionBlock block in blocks)
            {
                if (block?.ParsedActions == null) continue;

                var ctx = new EvaluationContext { Particle = particle, Grid = _particleManager, CurrentMessage = currentMessage };
                if (!ConditionEvaluator.Evaluate(block.ParsedCondition, ctx)) continue;

                foreach (ActionNode action in block.ParsedActions)
                {
                    if (action == null) continue;

                    if (action.FunctionName == LanguageVocabulary.Wait)
                    {
                    }
                    else if (action.FunctionName == LanguageVocabulary.Move)
                    {
                        if (!isOnInit && !moveConsumed)
                        {
                            position = ApplyMove(particle, action.Arguments, position, ctx);
                            moveConsumed = true;
                        }
                    }
                    else if (action.FunctionName == LanguageVocabulary.SetState)
                    {
                        // Applied now, not deferred to phase 3, on purpose: onStep must see what onMessage set this cycle. See CLAUDE.md.
                        if (ApplySetState(particle, action.Arguments, ctx))
                            anyActivity = true;
                    }
                    else if (action.FunctionName == LanguageVocabulary.SetColor)
                    {
                        string hex = ((LiteralValueNode)action.Arguments[0]).Token.Value;
                        if (ColorUtility.TryParseHtmlString(hex, out Color color))
                        {
                            OnColorChanged?.Invoke(particle, color);
                            anyActivity = true;
                        }
                    }
                    else if (action.FunctionName == LanguageVocabulary.SendMessage)
                    {
                        if (ApplySendMessage(particle, action.Arguments, ctx))
                            anyActivity = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SimulationEngine] Unrecognized action: '{action.FunctionName}'");
                    }
                }
            }
            return position;
        }

        private Vector3Int ApplyMove(Particle particle, ValueNode[] args, Vector3Int position, EvaluationContext ctx)
        {
            string direction = ValueResolver.ResolveDirection(args[0], ctx);
            if (direction == null || !LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
            {
                OnInvalidMovement?.Invoke(particle);
                _invalidMovesThisCycle.Add(particle);
                return position;
            }

            Vector3Int desired = position + delta;
            Vector3Int bounded = _particleManager.ApplyBoundary(desired);
            if (bounded != desired)
            {
                OnInvalidMovement?.Invoke(particle);
                _invalidMovesThisCycle.Add(particle);
            }
            return bounded;
        }

        private static bool ApplySetState(Particle particle, ValueNode[] args, EvaluationContext ctx)
        {
            string key = ((LiteralValueNode)args[0]).Token.Value;
            ValueNode valueNode = args[1];

            if (valueNode is LiteralValueNode literal
                && literal.Token.Kind == TokenKind.Number
                && literal.Token.Text.Length > 0
                && (literal.Token.Text[0] == '+' || literal.Token.Text[0] == '-'))
            {
                double delta = double.Parse(literal.Token.Text, System.Globalization.CultureInfo.InvariantCulture);
                return ApplyStateIncrement(particle, key, delta);
            }

            particle.SetInternalState(key, ValueResolver.Resolve(valueNode, ctx));
            return true;
        }

        private static bool ApplyStateIncrement(Particle particle, string key, double delta)
        {
            object current = particle.GetInternalState(key);
            double baseValue = 0.0;

            if (current != null)
            {
                if (!ConditionEvaluator.IsNumeric(current))
                {
                    Debug.LogWarning($"[SimulationEngine] setState increment/decrement ignored: '{key}' holds a non-numeric value ({current}).");
                    return false;
                }
                baseValue = Convert.ToDouble(current, System.Globalization.CultureInfo.InvariantCulture);
            }

            particle.SetInternalState(key, baseValue + delta);
            return true;
        }

        private bool ApplySendMessage(Particle particle, ValueNode[] args, EvaluationContext ctx)
        {
            bool directed = IsDirected(args);
            object content = ValueResolver.Resolve(args[directed ? 1 : 0], ctx);
            ValueNode payloadNode = directed
                ? (args.Length >= 3 ? args[2] : null)
                : (args.Length >= 2 ? args[1] : null);
            double? payload = ResolvePayload(payloadNode, ctx);

            if (!directed)
                return SendMessage(particle, direction: null, wasDirected: false, content, payload);

            string direction = ValueResolver.ResolveDirection(args[0], ctx);
            return SendMessage(particle, direction, wasDirected: true, content, payload);
        }

        private static bool IsDirected(ValueNode[] args)
        {
            if (args.Length == 1) return false;
            if (args.Length == 3) return true;
            return LanguageVocabulary.IsDirectionShaped(args[0]);
        }

        private static double? ResolvePayload(ValueNode payloadNode, EvaluationContext ctx)
        {
            if (payloadNode == null) return null;
            object resolved = ValueResolver.Resolve(payloadNode, ctx);
            return ConditionEvaluator.IsNumeric(resolved)
                ? Convert.ToDouble(resolved, CultureInfo.InvariantCulture)
                : (double?)null;
        }

        private bool SendMessage(Particle particle, string direction, bool wasDirected, object content, double? payload)
        {
            bool anySent;

            if (wasDirected)
            {
                anySent = direction != null && TrySendTo(particle, direction, content, payload);
            }
            else
            {
                anySent = false;
                foreach (string dir in LanguageVocabulary.Directions)
                    anySent |= TrySendTo(particle, dir, content, payload);
            }

            if (!anySent)
                OnMessageSendFailed?.Invoke(particle);

            return anySent;
        }

        private bool TrySendTo(Particle sender, string direction, object content, double? payload)
        {
            LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta);

            // Not ApplyBoundary: clamping an off-grid cell lands on the sender's own cell, so it would message itself.
            Vector3Int neighborPos = sender.GridPosition + delta;
            if (!_particleManager.IsInBounds(neighborPos) || !_particleManager.TryGetParticleAt(neighborPos, out Particle recipient))
                return false;

            string originDirection = LanguageVocabulary.OppositeDirection(direction);
            var message = new Message(sender, recipient, content, originDirection, payload);

            if (!_pendingMessages.TryGetValue(recipient, out List<Message> queue))
                _pendingMessages[recipient] = queue = new List<Message>();
            queue.Add(message);

            OnMessageSent?.Invoke(message);
            return true;
        }

        private void DeliverPendingMessages()
        {
            foreach (Particle particle in _particleManager.Particles)
                particle.ClearInbox();

            foreach (KeyValuePair<Particle, List<Message>> pending in _pendingMessages)
            {
                foreach (Message message in pending.Value)
                {
                    pending.Key.ReceiveMessage(message);
                    OnMessageDelivered?.Invoke(message);
                }
            }

            _pendingMessages.Clear();
        }

        private Dictionary<Particle, Vector3Int> ResolveCollisions(Dictionary<Particle, Vector3Int> desiredPositions)
        {
            var finalPositions = new Dictionary<Particle, Vector3Int>();
            var willMove = new Dictionary<Particle, bool>();

            foreach (Particle p in _particleManager.Particles)
                willMove[p] = desiredPositions[p] != p.GridPosition;

            var hadMoveIntent = new Dictionary<Particle, bool>(willMove);

            var requestCount = new Dictionary<Vector3Int, int>();
            foreach (Vector3Int pos in desiredPositions.Values)
            {
                if (!requestCount.ContainsKey(pos)) requestCount[pos] = 0;
                requestCount[pos]++;
            }
            foreach (Particle p in _particleManager.Particles)
                if (willMove[p] && requestCount[desiredPositions[p]] > 1)
                    willMove[p] = false;

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
            {
                if (hadMoveIntent[p] && !willMove[p])
                {
                    OnCollision?.Invoke(p);
                    _collisionBlockedThisCycle.Add(p);
                }
                finalPositions[p] = willMove[p] ? desiredPositions[p] : p.GridPosition;
            }

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
