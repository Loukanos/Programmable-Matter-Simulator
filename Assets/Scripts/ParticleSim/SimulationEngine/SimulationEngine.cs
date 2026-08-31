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

        // Messages queued by sendMessage this cycle, keyed by recipient, not
        // yet visible in the recipient's Inbox. Delivered (moved into each
        // recipient's Inbox) at the start of the NEXT Step() call — delivery
        // is always one full cycle after the send, never same-cycle.
        private readonly Dictionary<Particle, List<Message>> _pendingMessages = new();

        /// <summary>Disparado ao final de cada ciclo, com o número do ciclo e se houve movimento.</summary>
        public event Action<int, bool> OnCycleCompleted;
        /// <summary>Disparado quando uma partícula muda de posição (já aplicada ao modelo).</summary>
        public event Action<Particle, Vector3Int> OnParticleMoved;
        /// <summary>Disparado quando uma ação setColor é processada para uma partícula.</summary>
        public event Action<Particle, Color> OnColorChanged;
        /// <summary>Disparado quando a simulação encerra por um critério de parada.</summary>
        public event Action<string> OnSimulationFinished;
        /// <summary>Fired when sendMessage creates a Message, queuing it for delivery (not yet visible to the recipient).</summary>
        public event Action<Message> OnMessageSent;
        /// <summary>Fired once per sendMessage(...) action execution that created zero Messages — a directed send with no neighbor there, or a broadcast reaching none of the six faces. Distinct from OnMessageSent: a partially-successful broadcast fires only OnMessageSent (once per neighbor reached), never this.</summary>
        public event Action<Particle> OnMessageSendFailed;
        /// <summary>Fired when a Message becomes visible in its recipient's Inbox, one cycle after it was sent.</summary>
        public event Action<Message> OnMessageDelivered;
        /// <summary>Fired once per particle whose valid move() intent (desired position differed from current) was cancelled by collision resolution because of another particle — either a shared-destination race or a target cell held by a non-mover.</summary>
        public event Action<Particle> OnCollision;
        /// <summary>Fired once per particle whose move() would have taken it outside the grid — the movement is cancelled and the particle stays at its original position.</summary>
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
 
        /// <summary>Avança a simulação em exatamente um ciclo discreto.</summary>
        public void Step()
        {
            if (IsFinished || !IsRunning) return;

            DeliverPendingMessages();
            _particleManager.RefreshGridLookup();
 
            var desiredPositions = new Dictionary<Particle, Vector3Int>();
            bool anyActivity = false;
            foreach (Particle particle in _particleManager.Particles)
            {
                Vector3Int position = particle.GridPosition;
                bool moveConsumed = false;
                if (_behaviors != null && _behaviors.TryGetValue(particle.Type, out TypeBehavior behavior))
                {
                    position = ProcessActions(particle, behavior.onStep, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position);

                    foreach (OnTouchEvent touchEvent in behavior.onTouch)
                    {
                        if (!ConditionEvaluator.HasNeighbor(particle, _particleManager, touchEvent.direction)) continue;
                        position = ProcessActions(particle, touchEvent.blocks, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position);
                    }

                    foreach (OnMessageEvent messageEvent in behavior.onMessage)
                    {
                        if (!ConditionEvaluator.HasMessage(particle, messageEvent.direction)) continue;
                        position = ProcessActions(particle, messageEvent.blocks, isOnInit: false, ref moveConsumed, ref anyActivity, startPosition: position);
                    }
                }
                desiredPositions[particle] = position;
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
            // Ociosidade é resetada por qualquer atividade capaz de modificar o
            // ambiente: movimento efetivamente aplicado (pós-resolução de
            // colisões), setState ou setColor executados com sucesso — não é
            // necessário comparar valor novo vs. anterior (seção 2.2.3 do TCC).
            _idleCycles = (anyMoved || anyActivity) ? 0 : _idleCycles + 1;
 
            OnCycleCompleted?.Invoke(CurrentCycle, anyMoved);
            CheckStopConditions();
        }
 
        private Vector3Int ProcessActions(Particle particle, List<ActionBlock> blocks, bool isOnInit,
            ref bool moveConsumed, ref bool anyActivity, Vector3Int? startPosition = null)
        {
            Vector3Int position = startPosition ?? particle.GridPosition;
            if (blocks == null) return position;

            foreach (ActionBlock block in blocks)
            {
                if (block?.actions == null) continue;
                if (!ConditionEvaluator.Evaluate(block.condition, particle, _particleManager)) continue;

                foreach (string action in block.actions)
                {
                    if (string.IsNullOrWhiteSpace(action)) continue;

                    if (WaitAction.IsWait(action))
                    {
                        // Nenhuma ação: mantém posição e estado inalterados.
                    }
                    else if (MoveAction.TryParse(action, out Vector3Int delta))
                    {
                        // Movimentos declarados em onInit não deslocam a partícula
                        // (também barrado em tempo de carregamento por
                        // BehaviorLoader). Apenas o primeiro move() por
                        // partícula por ciclo é aplicado; os demais, em
                        // qualquer bloco/evento subsequente do mesmo ciclo,
                        // são descartados.
                        if (!isOnInit && !moveConsumed)
                        {
                            Vector3Int desired = position + delta;
                            Vector3Int bounded = _particleManager.ApplyBoundary(desired);
                            if (bounded != desired)
                                OnInvalidMovement?.Invoke(particle);
                            position = bounded;
                            moveConsumed = true;
                        }
                    }
                    else if (SetStateAction.TryParse(action, out string key, out object value, out bool isIncrement))
                    {
                        if (isIncrement)
                        {
                            if (ApplyStateIncrement(particle, key, (double)value))
                                anyActivity = true;
                        }
                        else
                        {
                            particle.SetInternalState(key, value);
                            anyActivity = true;
                        }
                    }
                    else if (SetColorAction.TryParse(action, out Color color))
                    {
                        OnColorChanged?.Invoke(particle, color);
                        anyActivity = true;
                    }
                    else if (SendMessageAction.TryParse(action, out string sendDirection, out object content))
                    {
                        if (SendMessage(particle, sendDirection, content))
                            anyActivity = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SimulationEngine] Ação não reconhecida: '{action}'");
                    }
                }
            }
            return position;
        }

        /// <summary>
        /// Aplica um setState('key', +N/-N): soma delta ao valor numérico
        /// atual de key (tratando ausência ou valor não-numérico como 0
        /// para permitir inicializar contadores direto), exceto quando key
        /// já existe com um valor não-numérico — nesse caso a ação é
        /// ignorada com um aviso, sem derrubar a simulação. Retorna se o
        /// incremento foi de fato aplicado (usado para contabilizar
        /// atividade no critério de ociosidade).
        /// </summary>
        private static bool ApplyStateIncrement(Particle particle, string key, double delta)
        {
            object current = particle.GetInternalState(key);
            double baseValue = 0.0;

            if (current != null)
            {
                if (!ConditionEvaluator.IsNumeric(current))
                {
                    Debug.LogWarning($"[SimulationEngine] setState incremento/decremento ignorado: '{key}' possui valor não-numérico ({current}).");
                    return false;
                }
                baseValue = Convert.ToDouble(current, System.Globalization.CultureInfo.InvariantCulture);
            }

            particle.SetInternalState(key, baseValue + delta);
            return true;
        }

        /// <summary>Result of a single-direction send attempt — distinguishes a malformed direction string (an authoring error in behavior.json, not a communication event) from a well-formed direction that simply found no one there.</summary>
        private enum SendResult { Sent, NoNeighbor, InvalidDirection }

        /// <summary>
        /// Processes sendMessage(dir?, content): with a direction, sends only
        /// to the neighbor occupying that face (no error if there is none,
        /// same convention as hasNeighbor returning false); without a
        /// direction, broadcasts a distinct Message to every occupied
        /// neighbor across the six faces. Fires OnMessageSendFailed exactly
        /// once when the action executed with a well-formed direction but
        /// reached nobody at all — a partially-successful broadcast does not
        /// fire it (it already counted the neighbors it did reach via
        /// OnMessageSent), and neither does a malformed direction string
        /// (that's a behavior.json authoring error, not a communication
        /// attempt — it stays a Debug.LogWarning only, never counted).
        /// Returns whether at least one Message was actually created (used to
        /// count as activity for the idle-cycle criterion).
        /// </summary>
        private bool SendMessage(Particle sender, string direction, object content)
        {
            bool anySent;
            bool anyWellFormed;

            if (!string.IsNullOrWhiteSpace(direction))
            {
                SendResult result = TrySendTo(sender, direction, content);
                anySent = result == SendResult.Sent;
                anyWellFormed = result != SendResult.InvalidDirection;
            }
            else
            {
                anySent = false;
                // The six broadcast labels are always well-formed by construction.
                anyWellFormed = true;
                foreach (string dir in LanguageVocabulary.Directions)
                    if (TrySendTo(sender, dir, content) == SendResult.Sent)
                        anySent = true;
            }

            if (!anySent && anyWellFormed)
                OnMessageSendFailed?.Invoke(sender);

            return anySent;
        }

        private SendResult TrySendTo(Particle sender, string direction, object content)
        {
            if (!LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
            {
                Debug.LogWarning($"[SimulationEngine] sendMessage: unknown direction '{direction}'.");
                return SendResult.InvalidDirection;
            }

            // Not ApplyBoundary here — see ConditionEvaluator.HasNeighbor's
            // comment: off-grid is "no neighbor", never clamped onto the
            // sender's own cell.
            Vector3Int neighborPos = sender.GridPosition + delta;
            if (!_particleManager.IsInBounds(neighborPos) || !_particleManager.TryGetParticleAt(neighborPos, out Particle recipient))
                return SendResult.NoNeighbor;

            // The Message stores the direction of the sender AS SEEN BY the
            // recipient (the opposite of the direction it was sent with), so
            // that recipient.onMessage(dir) can match against it.
            string originDirection = LanguageVocabulary.OppositeDirection(direction);
            var message = new Message(sender, recipient, content, originDirection);

            if (!_pendingMessages.TryGetValue(recipient, out List<Message> queue))
                _pendingMessages[recipient] = queue = new List<Message>();
            queue.Add(message);

            OnMessageSent?.Invoke(message);
            return SendResult.Sent;
        }

        /// <summary>
        /// Start-of-cycle message handoff: clears every particle's inbox
        /// from the previous cycle, then moves everything queued by last
        /// cycle's sendMessage calls into the matching recipient's Inbox.
        /// This is what makes delivery land exactly one cycle after the
        /// send — never the same cycle it was queued in.
        /// </summary>
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

            // Snapshot of which particles had a genuine move intent, taken
            // before any conflict resolution runs below. Compared against
            // willMove's final state to detect collisions: a particle only
            // counts if it actually wanted to move and got blocked because of
            // another particle (shared-destination race or a stationary
            // occupant), never a particle that simply didn't try to move.
            var hadMoveIntent = new Dictionary<Particle, bool>(willMove);

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
            {
                if (hadMoveIntent[p] && !willMove[p])
                    OnCollision?.Invoke(p);
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