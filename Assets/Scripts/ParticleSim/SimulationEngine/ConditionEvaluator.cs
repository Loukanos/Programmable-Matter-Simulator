using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Evaluates a condition AST (hasNeighbor, checkState, checkMessage,
    /// canMove, And/Or/Not) for a particle. The AST is built once at load time
    /// by InputValidation.ExpressionValidator (with every named-condition
    /// reference already spliced into its real tree by
    /// InputValidation.DefinitionResolver — this class never sees a
    /// ConditionReferenceNode); this class only ever walks it, never
    /// re-parses a string — see CLAUDE.md.
    ///
    /// EvaluateHasNeighbor/EvaluateCheckMessage are public because they are
    /// also the canonical implementation when hasNeighbor/checkMessage are
    /// used nested inside checkState/setState instead of as a direct
    /// condition — SimulationEngine.ValueResolver.ResolveCall calls back into
    /// them for exactly that case, so the evaluation logic is identical in
    /// both roles by construction, not by keeping two implementations in
    /// sync. canMove is the mirror image: its canonical implementation
    /// (ValueResolver.ResolveCanMove) lives on the ValueResolver side, and
    /// EvaluateFunctionCall below calls into that when canMove is used as a
    /// direct condition.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>A null node means an absent/empty "condition", which always evaluates true.</summary>
        public static bool Evaluate(ConditionNode node, EvaluationContext ctx)
        {
            if (node == null) return true;

            switch (node)
            {
                case OrNode orNode:
                    foreach (ConditionNode operand in orNode.Operands)
                        if (Evaluate(operand, ctx))
                            return true;
                    return false;

                case AndNode andNode:
                    foreach (ConditionNode operand in andNode.Operands)
                        if (!Evaluate(operand, ctx))
                            return false;
                    return true;

                case NotNode notNode:
                    return !Evaluate(notNode.Operand, ctx);

                case BoolLiteralNode boolNode:
                    return boolNode.Value;

                case FunctionCallNode call:
                    return EvaluateFunctionCall(call, ctx);

                default:
                    // Includes ConditionReferenceNode — should never reach here
                    // post-BehaviorLoader, which resolves every reference
                    // before storing ActionBlock.ParsedCondition.
                    Debug.LogWarning($"[ConditionEvaluator] Nó de condição inesperado: {node.GetType().Name}");
                    return false;
            }
        }

        private static bool EvaluateFunctionCall(FunctionCallNode call, EvaluationContext ctx)
        {
            if (call.FunctionName == LanguageVocabulary.HasNeighbor)
                return EvaluateHasNeighbor(call.Arguments, ctx);
            if (call.FunctionName == LanguageVocabulary.CheckState)
                return EvaluateCheckState(call.Arguments, ctx);
            if (call.FunctionName == LanguageVocabulary.CheckMessage)
                return EvaluateCheckMessage(call.Arguments, ctx);
            if (call.FunctionName == LanguageVocabulary.CanMove)
                return ValueResolver.ResolveCanMove(call.Arguments, ctx);

            // Unreachable once a file has passed validation — Grammar only
            // registers these four as FunctionRole.Condition — kept as a
            // defensive fallback rather than an exception, matching this
            // codebase's general preference for not crashing the simulation.
            Debug.LogWarning($"[ConditionEvaluator] Função de condição desconhecida: {call.FunctionName}");
            return false;
        }

        // hasNeighbor always takes exactly one direction argument in the
        // expression grammar (unlike the onTouch(DIR) event key, which may
        // omit it — see HasNeighbor below). When that argument is a
        // direction-as-variable expression that fails to resolve, this
        // returns false directly rather than calling HasNeighbor with a null
        // direction, which would otherwise mean "check all six faces" — the
        // bare-onTouch convention, not what an unresolvable direction means
        // here (TCC extension: behaves as "no neighbor").
        public static bool EvaluateHasNeighbor(ValueNode[] args, EvaluationContext ctx)
        {
            string direction = ValueResolver.ResolveDirection(args[0], ctx);
            return direction != null && HasNeighbor(ctx.Particle, ctx.Grid, direction);
        }

        /// <summary>
        /// Verifica contato com um vizinho. Com <paramref name="direction"/>
        /// informado (X+, X-, Y+, Y-, Z+ ou Z-), checa apenas aquela face;
        /// caso contrário, checa as seis faces (usado por onTouch sem
        /// direção específica). Recebe uma string diretamente (não um nó da
        /// AST) porque a direção de onTouch(DIR) vem da chave do evento JSON
        /// (Grammar.ParseEventKey), nunca fez parte da gramática de expressão.
        /// </summary>
        public static bool HasNeighbor(Particle particle, IGridQuery grid, string direction = null)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                foreach (Vector3Int d in LanguageVocabulary.DirectionDeltas)
                {
                    Vector3Int neighborPos = particle.GridPosition + d;
                    if (grid.IsInBounds(neighborPos) && grid.TryGetParticleAt(neighborPos, out _))
                        return true;
                }
                return false;
            }

            if (!LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
            {
                Debug.LogWarning($"[ConditionEvaluator] Direção de vizinhança desconhecida: {direction}");
                return false;
            }

            // Note: intentionally NOT grid.ApplyBoundary — that clamps a
            // beyond-the-edge position back onto the particle's OWN cell (see
            // ApplyBoundary's doc comment), which would make TryGetParticleAt
            // find the particle itself and report a false neighbor at every
            // grid edge. Off-grid simply means no neighbor there.
            Vector3Int target = particle.GridPosition + delta;
            return grid.IsInBounds(target) && grid.TryGetParticleAt(target, out _);
        }

        /// <summary>
        /// Every message in the particle's current-cycle inbox that an
        /// onMessage/onMessage(DIR) event should react to: with
        /// <paramref name="direction"/> given, only messages whose origin
        /// direction (Message.Direction) matches; otherwise (bare
        /// onMessage), every message. SimulationEngine.Step() runs an
        /// event's blocks once per message this yields — never once per
        /// event regardless of count — specifically so that several
        /// messages arriving in the same cycle (e.g. the same tag from
        /// several different senders) are each evaluated on their own pass;
        /// an empty result is the "no matching message" case, equivalent to
        /// the old existence-gate skipping the event entirely. Unlike
        /// HasNeighbor/onTouch, a "touch" is a single yes/no per direction
        /// (a grid cell holds at most one particle), so onTouch has no
        /// equivalent multiplicity concern and stays a simple gate.
        /// </summary>
        public static IEnumerable<Message> MatchingMessages(Particle particle, string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                foreach (Message message in particle.Inbox)
                    yield return message;
                yield break;
            }

            string normalized = LanguageVocabulary.NormalizeDirection(direction);
            foreach (Message message in particle.Inbox)
                if (string.Equals(message.Direction, normalized, StringComparison.OrdinalIgnoreCase))
                    yield return message;
        }

        private static bool EvaluateCheckState(ValueNode[] args, EvaluationContext ctx)
        {
            string key = LiteralText(args[0]);
            string op = LiteralText(args[1]);
            object current = ctx.Particle.GetInternalState(key);
            object target = ValueResolver.Resolve(args[2], ctx);

            if (op == LanguageVocabulary.Equal) return AreEqual(current, target);
            if (op == LanguageVocabulary.NotEqual) return !AreEqual(current, target);
            if (LanguageVocabulary.IsMagnitudeOperator(op)) return CompareValues(current, target, op);

            Debug.LogWarning($"[ConditionEvaluator] Operador de comparação desconhecido: {op}");
            return false;
        }

        // checkMessage has no operator parameter like checkState: it is
        // always a simple equality comparison against Message.Content.
        //
        // Inside an onMessage/onMessage(DIR) block, ctx.CurrentMessage is
        // set to the one specific message this pass is about (see
        // MatchingMessages/EvaluationContext) — checkMessage checks only
        // that message, not the whole inbox, which is what lets N same-tag
        // messages from N different senders each get their own independent
        // pass instead of every pass seeing whichever one happens to be
        // first in Inbox order. Outside an onMessage block (onInit/onStep/
        // onTouch, or checkMessage nested as a value from one of those),
        // CurrentMessage is null and checkMessage falls back to "does any
        // message in the inbox match" — an existence check, since there is
        // no single message this evaluation is about. Either way, the
        // first match becomes ctx.MatchedMessage, for messageValue() to read
        // later in this same block's actions — a later checkMessage call
        // that also matches (e.g. the other side of an And) never
        // overwrites it (moot inside onMessage, where every checkMessage in
        // the same pass necessarily checks the same CurrentMessage).
        public static bool EvaluateCheckMessage(ValueNode[] args, EvaluationContext ctx)
        {
            object target = ValueResolver.Resolve(args[0], ctx);

            if (ctx.CurrentMessage != null)
            {
                if (!AreEqual(ctx.CurrentMessage.Content, target)) return false;
                ctx.MatchedMessage ??= ctx.CurrentMessage;
                return true;
            }

            foreach (Message message in ctx.Particle.Inbox)
            {
                if (!AreEqual(message.Content, target)) continue;
                ctx.MatchedMessage ??= message;
                return true;
            }
            return false;
        }

        /// <summary>The token text of an argument slot that validation guarantees is always a plain literal (StateKey/RelationalOperator), never a nested call.</summary>
        private static string LiteralText(ValueNode node) => ((LiteralValueNode)node).Token.Value;

        private static bool AreEqual(object current, object target)
        {
            if (current == null) return target == null;
            if (target == null) return false;

            if (IsNumeric(current) && IsNumeric(target))
                return Math.Abs(Convert.ToDouble(current, CultureInfo.InvariantCulture) - Convert.ToDouble(target, CultureInfo.InvariantCulture)) < 1e-6;

            if (current.Equals(target)) return true;

            return current.ToString().Trim().Equals(target.ToString().Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // >, <, >=, <= comparam numericamente quando ambos os lados são
        // números; caso contrário caem para comparação lexicográfica de
        // strings (ordinal, case-insensitive).
        private static bool CompareValues(object current, object target, string op)
        {
            if (current == null || target == null) return false;

            int comparison = IsNumeric(current) && IsNumeric(target)
                ? Convert.ToDouble(current, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDouble(target, CultureInfo.InvariantCulture))
                : string.Compare(current.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase);

            switch (op)
            {
                case ">": return comparison > 0;
                case "<": return comparison < 0;
                case ">=": return comparison >= 0;
                case "<=": return comparison <= 0;
                default: return false;
            }
        }

        public static object ParseValue(string valStr)
        {
            if (string.IsNullOrEmpty(valStr)) return null;
            valStr = valStr.Trim().Trim('\'', '\"');

            if (bool.TryParse(valStr, out bool b)) return b;
            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;

            return valStr;
        }

        public static bool IsNumeric(object obj) =>
            obj is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}
