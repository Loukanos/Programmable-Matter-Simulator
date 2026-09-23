using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    public static class ConditionEvaluator
    {
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

            Debug.LogWarning($"[ConditionEvaluator] Função de condição desconhecida: {call.FunctionName}");
            return false;
        }

        // An unresolved direction means no neighbor; passing null to HasNeighbor would check all six faces instead.
        public static bool EvaluateHasNeighbor(ValueNode[] args, EvaluationContext ctx)
        {
            string direction = ValueResolver.ResolveDirection(args[0], ctx);
            return direction != null && HasNeighbor(ctx.Particle, ctx.Grid, direction);
        }

        public static bool HasNeighbor(Particle particle, IGridQuery grid, string direction = null)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                foreach (Vector3Int d in LanguageVocabulary.DirectionDeltas)
                {
                    Vector3Int neighborPos = particle.GridPosition + d;
                    if (grid.IsInBounds(neighborPos) && grid.IsOccupied(neighborPos))
                        return true;
                }
                return false;
            }

            if (!LanguageVocabulary.TryGetDirectionDelta(direction, out Vector3Int delta))
            {
                Debug.LogWarning($"[ConditionEvaluator] Direção de vizinhança desconhecida: {direction}");
                return false;
            }

            // Not ApplyBoundary: clamping an off-grid cell lands on this particle's own cell, making it its own neighbor.
            Vector3Int target = particle.GridPosition + delta;
            return grid.IsInBounds(target) && grid.IsOccupied(target);
        }

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
