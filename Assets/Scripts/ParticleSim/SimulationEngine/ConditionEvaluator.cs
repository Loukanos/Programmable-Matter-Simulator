using System;
using System.Globalization;
using UnityEngine;
using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Evaluates a condition AST (hasNeighbor, checkState, checkMessage,
    /// And/Or/Not) for a particle, consulting the grade only through
    /// IGridQuery — never knows the concrete ParticleManager nor any
    /// ParticleSim.UI class. The AST is built once at load time by
    /// InputValidation.ExpressionValidator; this class only ever walks it,
    /// never re-parses a string — see CLAUDE.md.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>A null node means an absent/empty "condition", which always evaluates true.</summary>
        public static bool Evaluate(ConditionNode node, Particle particle, IGridQuery grid)
        {
            if (node == null) return true;

            switch (node)
            {
                case OrNode orNode:
                    foreach (ConditionNode operand in orNode.Operands)
                        if (Evaluate(operand, particle, grid))
                            return true;
                    return false;

                case AndNode andNode:
                    foreach (ConditionNode operand in andNode.Operands)
                        if (!Evaluate(operand, particle, grid))
                            return false;
                    return true;

                case NotNode notNode:
                    return !Evaluate(notNode.Operand, particle, grid);

                case BoolLiteralNode boolNode:
                    return boolNode.Value;

                case FunctionCallNode call:
                    return EvaluateFunctionCall(call, particle, grid);

                default:
                    Debug.LogWarning($"[ConditionEvaluator] Nó de condição desconhecido: {node.GetType().Name}");
                    return false;
            }
        }

        private static bool EvaluateFunctionCall(FunctionCallNode call, Particle particle, IGridQuery grid)
        {
            if (call.FunctionName == LanguageVocabulary.HasNeighbor)
                return HasNeighbor(particle, grid, call.Arguments.Length > 0 ? call.Arguments[0].Value : null);
            if (call.FunctionName == LanguageVocabulary.CheckState)
                return EvaluateCheckState(call.Arguments, particle);
            if (call.FunctionName == LanguageVocabulary.CheckMessage)
                return EvaluateCheckMessage(call.Arguments, particle);

            // Unreachable once a file has passed validation — Grammar only
            // registers these three as FunctionRole.Condition — kept as a
            // defensive fallback rather than an exception, matching this
            // codebase's general preference for not crashing the simulation.
            Debug.LogWarning($"[ConditionEvaluator] Função de condição desconhecida: {call.FunctionName}");
            return false;
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
        /// Checks the particle's current-cycle inbox. With
        /// <paramref name="direction"/> given, restricts to messages whose
        /// origin direction (Message.Direction) matches; otherwise (bare
        /// onMessage), any message in the inbox triggers it. Same
        /// raw-string convention as HasNeighbor, for the same reason.
        /// </summary>
        public static bool HasMessage(Particle particle, string direction = null)
        {
            if (string.IsNullOrWhiteSpace(direction))
                return particle.Inbox.Count > 0;

            string normalized = LanguageVocabulary.NormalizeDirection(direction);
            foreach (Message message in particle.Inbox)
                if (string.Equals(message.Direction, normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool EvaluateCheckState(Token[] args, Particle particle)
        {
            string key = args[0].Value;
            string op = args[1].Value;
            object current = particle.GetInternalState(key);
            object target = ParseValue(args[2].Value);

            if (op == LanguageVocabulary.Equal) return AreEqual(current, target);
            if (op == LanguageVocabulary.NotEqual) return !AreEqual(current, target);
            if (LanguageVocabulary.IsMagnitudeOperator(op)) return CompareValues(current, target, op);

            Debug.LogWarning($"[ConditionEvaluator] Operador de comparação desconhecido: {op}");
            return false;
        }

        // checkMessage has no operator parameter like checkState: it is
        // always a simple equality comparison against Message.Content.
        private static bool EvaluateCheckMessage(Token[] args, Particle particle)
        {
            object target = ParseValue(args[0].Value);
            foreach (Message message in particle.Inbox)
                if (AreEqual(message.Content, target))
                    return true;
            return false;
        }

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
