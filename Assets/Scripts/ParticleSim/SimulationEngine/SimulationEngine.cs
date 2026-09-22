using System;
using System.Collections.Generic;
using System.Globalization;
using ParticleSim.Core;
using UnityEngine;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Motor de Simulação: processa os ciclos discretos (coleta de
    /// intenções, resolução de conflitos, aplicação de ações),
    /// interpretando as regras declarativas de cada tipo de partícula.
    ///
    /// Depende do ParticleManager **concretamente**, não via IGridQuery: além
    /// das três consultas da interface, usa Particles (enumerar a população a
    /// cada ciclo) e RefreshGridLookup (reconstruir o índice espacial no
    /// início do ciclo), que não estão nela. Isso é o que a Figura 7 do TCC
    /// especifica — "o Motor de Simulação depende do Gerenciador de
    /// Partículas" — e está registrado aqui porque a versão anterior deste
    /// comentário afirmava o contrário ("apenas através da interface
    /// IGridQuery"), o que levou uma auditoria a concluir errado a direção
    /// real da dependência.
    ///
    /// Onde IGridQuery de fato atua é uma camada abaixo: é o que o
    /// *interpretador* (ConditionEvaluator/ValueResolver, via
    /// EvaluationContext.Grid) enxerga, e por isso a linguagem declarativa
    /// nunca alcança a população inteira — só consulta célula a célula.
    ///
    /// Nunca referencia ParticleSim.UI. Movimento e mudança de cor são
    /// comunicados por eventos, para que a camada de visualização se atualize
    /// sem gerar dependência reversa.
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

        // Per-cycle scratch state for computing each particle's MoveOutcome
        // at the end of Step() — cleared at the top of every Step() call.
        // Populated at the exact points OnInvalidMovement/OnCollision already
        // fire, so the public events and this bookkeeping never disagree.
        private readonly HashSet<Particle> _invalidMovesThisCycle = new();
        private readonly HashSet<Particle> _collisionBlockedThisCycle = new();

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
        /// <summary>Fired once per sendMessage(...) action execution that created zero Messages — a directed send with no neighbor there (or whose direction expression didn't resolve), or a broadcast reaching none of the six faces.</summary>
        public event Action<Particle> OnMessageSendFailed;
        /// <summary>Fired when a Message becomes visible in its recipient's Inbox, one cycle after it was sent.</summary>
        public event Action<Message> OnMessageDelivered;
        /// <summary>Fired once per particle whose valid move() intent (desired position differed from current) was cancelled by collision resolution because of another particle — either a shared-destination race or a target cell held by a non-mover.</summary>
        public event Action<Particle> OnCollision;
        /// <summary>Fired once per particle whose move() could not be applied for a reason other than collision — the direction led outside the grid, or (direction-as-variable) a dynamic direction expression did not resolve to one of the six valid directions.</summary>
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
                    // onMessage first, then onTouch, then onStep — the
                    // particle perceives what arrived (messages delivered by
                    // DeliverPendingMessages above, at the very start of this
                    // Step()) before deciding what to do, rather than
                    // deciding first and only perceiving afterward. This is
                    // what lets a message delivered this cycle influence
                    // this same cycle's movement decision: onMessage may
                    // update state (e.g. setState('stopped', true)) that
                    // onStep's own condition reads moments later, in the
                    // same Step() call — see CLAUDE.md's "Notable
                    // non-obvious things" for why the order used to be the
                    // other way around and what that cost.
                    foreach (OnMessageEvent messageEvent in behavior.onMessage)
                    {
                        // Once per matching message, not once per event —
                        // see ConditionEvaluator.MatchingMessages and
                        // EvaluationContext.CurrentMessage. A cycle with no
                        // matching message simply yields no iterations here,
                        // the same net effect as the old existence gate.
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
            // Ociosidade é resetada por qualquer atividade capaz de modificar o
            // ambiente: movimento efetivamente aplicado (pós-resolução de
            // colisões), setState ou setColor executados com sucesso — não é
            // necessário comparar valor novo vs. anterior (seção 2.2.3 do TCC).
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

            // A move was attempted and neither blocking mechanism fired, so
            // it must have succeeded — defensive fallback only.
            return MoveOutcome.Moved;
        }

        /// <summary>
        /// <paramref name="currentMessage"/> is non-null only when processing
        /// one pass of an onMessage/onMessage(DIR) event for one specific
        /// inbox message (see the Step() call site and
        /// EvaluationContext.CurrentMessage) — null for onInit/onStep/
        /// onTouch, where there is no single message a block's evaluation
        /// is "about".
        /// </summary>
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
                        // Nenhuma ação: mantém posição e estado inalterados.
                    }
                    else if (action.FunctionName == LanguageVocabulary.Move)
                    {
                        // Movimentos declarados em onInit não deslocam a partícula
                        // (também barrado em tempo de carregamento por
                        // BehaviorLoader). Apenas o primeiro move() por
                        // partícula por ciclo é aplicado; os demais, em
                        // qualquer bloco/evento subsequente do mesmo ciclo,
                        // são descartados.
                        if (!isOnInit && !moveConsumed)
                        {
                            position = ApplyMove(particle, action.Arguments, position, ctx);
                            moveConsumed = true;
                        }
                    }
                    else if (action.FunctionName == LanguageVocabulary.SetState)
                    {
                        if (ApplySetState(particle, action.Arguments, ctx))
                            anyActivity = true;
                    }
                    else if (action.FunctionName == LanguageVocabulary.SetColor)
                    {
                        // O hex já foi validado no carregamento — TryParseHtmlString
                        // sempre deve ter sucesso aqui.
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
                        // Inalcançável depois que um arquivo passa na validação —
                        // Grammar só registra estas cinco como FunctionRole.Action.
                        Debug.LogWarning($"[SimulationEngine] Ação não reconhecida: '{action.FunctionName}'");
                    }
                }
            }
            return position;
        }

        /// <summary>
        /// Resolves move(dir)'s direction (literal, or state(...)/opposite(...))
        /// and applies it. An unresolvable direction is treated exactly like a
        /// move that would leave the grid — cancelled in place,
        /// OnInvalidMovement fires, and the move slot for this cycle is still
        /// considered consumed by the caller (moveConsumed is set by
        /// ProcessActions regardless of which branch runs here).
        /// </summary>
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

        /// <summary>
        /// Aplica setState('key', value): quando o valor é um literal
        /// numérico não citado prefixado com +/- (ex.: +5, -3.2), é sempre um
        /// incremento/decremento relativo (delegado a ApplyStateIncrement);
        /// qualquer outra forma — literal absoluto ou uma chamada aninhada a
        /// qualquer função FunctionRole.Value (as 9 funções de leitura, mais
        /// hasNeighbor/checkMessage como funções booleanas de papel duplo) —
        /// é atribuição absoluta. Um valor citado nunca lexa como TokenKind.Number, e uma
        /// chamada aninhada nunca é um LiteralValueNode, então nenhuma das
        /// duas é tratada como incremento — mesma regra de antes, agora
        /// verificada pelo tipo do nó/token em vez de inspecionar aspas
        /// manualmente.
        /// </summary>
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

        /// <summary>
        /// Processes sendMessage's 4 shapes: broadcast (tag) / directed (dir,
        /// tag) / broadcast with payload (tag, payload) / directed with
        /// payload (dir, tag, payload). The payload slot (in either 2- or
        /// 3-argument form) accepts a literal number or any FunctionRole.
        /// Value function, resolved here on the sender's side, once, at send
        /// time — only the resulting number ever travels on the Message;
        /// the recipient's messageValue() has no way to know, and doesn't
        /// need to, whether that number came from a literal or from e.g.
        /// myId()/state(...) on the sender. A direction expression that
        /// fails to resolve (state(...) reading a missing/wrong-type key —
        /// a malformed literal direction is already impossible, Grammar's
        /// validation rejects it at load time) is treated exactly like "no
        /// neighbor there": it still counts as a send attempt via
        /// OnMessageSendFailed.
        /// </summary>
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

        /// <summary>
        /// sendMessage's argument-0 shape decides directed vs broadcast the
        /// same way InputValidation.ExpressionValidator.ResolveOverload
        /// already decided it at load time — both call the shared
        /// Core.LanguageVocabulary.IsDirectionShaped, so the two can never
        /// drift apart into disagreeing about which overload a call
        /// matched. 1 argument is always broadcast (no ambiguity); 3
        /// arguments is always directed-with-payload (no ambiguity); only 2
        /// arguments is ambiguous between directed-no-payload
        /// (`sendMessage(dir, tag)`) and broadcast-with-payload
        /// (`sendMessage(tag, payload)`), and argument 0's *shape* — not its
        /// resolved value — breaks the tie: it wins as directed whenever it
        /// looks like a direction, i.e. a literal direction token/quoted
        /// direction string, or a nested state(...)/opposite(...) call.
        ///
        /// Concrete edge case this implies: `sendMessage('X+', 5)` is always
        /// parsed as directed(dir='X+', tag=5) — a message with content 5
        /// sent only to the neighbor on the X+ face — never as
        /// broadcast(tag='X+', payload=5), even though the author might have
        /// meant "broadcast the tag 'X+' with payload 5". There is no way to
        /// spell that second meaning with 2 arguments; the deliberate
        /// resolution keeps the pre-existing 2-argument meaning
        /// (`sendMessage(dir, tag)`) unchanged for every direction-shaped
        /// first argument, which is what "no valid file's behavior may
        /// change" requires — broadcast-with-payload is new, so this
        /// particular tag/payload combination (a tag equal to one of the six
        /// direction labels, together with a payload) simply isn't
        /// expressible; nothing pre-existing could have relied on it.
        /// </summary>
        private static bool IsDirected(ValueNode[] args)
        {
            if (args.Length == 1) return false;
            if (args.Length == 3) return true;
            return LanguageVocabulary.IsDirectionShaped(args[0]);
        }

        /// <summary>
        /// Resolves sendMessage's optional payload. A literal payload is
        /// always numeric already — ArgumentKind.NumericValue's literal
        /// branch rejects anything else at load time — but a computed
        /// payload (any FunctionRole.Value function) can resolve to a
        /// non-numeric value at runtime (e.g. moveResult() returning a
        /// string token, or a missing state(...) key returning null): treated
        /// as "no payload" (null), the same graceful fallback ValueResolver
        /// already uses for every other not-statically-checkable computed
        /// value, never a thrown exception.
        /// </summary>
        private static double? ResolvePayload(ValueNode payloadNode, EvaluationContext ctx)
        {
            if (payloadNode == null) return null;
            object resolved = ValueResolver.Resolve(payloadNode, ctx);
            return ConditionEvaluator.IsNumeric(resolved)
                ? Convert.ToDouble(resolved, CultureInfo.InvariantCulture)
                : (double?)null;
        }

        /// <summary>
        /// With wasDirected and a resolved direction, sends to the neighbor
        /// occupying that face only (silently does nothing if unoccupied);
        /// with wasDirected but no resolved direction, the send simply
        /// failed (equivalent to no neighbor there). Without a direction at
        /// all, broadcasts a distinct Message to every currently-occupied
        /// neighbor across the six faces. Returns whether at least one
        /// Message was actually created (used to count as activity for the
        /// idle-cycle criterion); fires OnMessageSendFailed once whenever it
        /// returns false, since every remaining failure mode here is a
        /// legitimate "reached nobody" outcome the TCC counts as an attempt.
        /// </summary>
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
            // direction here is always one of the six valid labels — either
            // validator-guaranteed (a literal) or already confirmed resolved
            // by the caller (a dynamic expression), or one of the broadcast
            // loop's own labels.
            LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta);

            // Not ApplyBoundary here — see ConditionEvaluator.HasNeighbor's
            // comment: off-grid is "no neighbor", never clamped onto the
            // sender's own cell.
            Vector3Int neighborPos = sender.GridPosition + delta;
            if (!_particleManager.IsInBounds(neighborPos) || !_particleManager.TryGetParticleAt(neighborPos, out Particle recipient))
                return false;

            // The Message stores the direction of the sender AS SEEN BY the
            // recipient (the opposite of the direction it was sent with), so
            // that recipient.onMessage(dir) can match against it.
            string originDirection = LanguageVocabulary.OppositeDirection(direction);
            var message = new Message(sender, recipient, content, originDirection, payload);

            if (!_pendingMessages.TryGetValue(recipient, out List<Message> queue))
                _pendingMessages[recipient] = queue = new List<Message>();
            queue.Add(message);

            OnMessageSent?.Invoke(message);
            return true;
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
