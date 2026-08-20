using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Interpreta as condições da linguagem declarativa (hasNeighbor,
    /// checkState, operadores lógicos And/Or/Not) para uma partícula,
    /// consultando a grade apenas através de IGridQuery — nunca conhece o
    /// ParticleManager concreto nem qualquer classe de ParticleSim.UI.
    /// </summary>
    public static class ConditionEvaluator
    {
        public static bool Evaluate(string condition, Particle particle, IGridQuery grid)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            return EvaluateOr(condition.Trim(), particle, grid);
        }

        private static bool EvaluateOr(string expr, Particle particle, IGridQuery grid)
        {
            foreach (string part in SplitByOperator(expr, "Or"))
                if (EvaluateAnd(part.Trim(), particle, grid))
                    return true;
            return false;
        }

        private static bool EvaluateAnd(string expr, Particle particle, IGridQuery grid)
        {
            foreach (string part in SplitByOperator(expr, "And"))
                if (!EvaluateNot(part.Trim(), particle, grid))
                    return false;
            return true;
        }

        private static bool EvaluateNot(string expr, Particle particle, IGridQuery grid)
        {
            if (expr.StartsWith("Not ", StringComparison.OrdinalIgnoreCase))
                return !EvaluateNot(expr.Substring(4).Trim(), particle, grid);
            return EvaluatePrimary(expr, particle, grid);
        }

        private static bool EvaluatePrimary(string expr, Particle particle, IGridQuery grid)
        {
            if (expr.StartsWith("hasNeighbor", StringComparison.OrdinalIgnoreCase))
                return EvaluateHasNeighbor(expr, particle, grid);
            if (expr.StartsWith("checkState", StringComparison.OrdinalIgnoreCase))
                return EvaluateCheckState(expr, particle);

            if (expr.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (expr.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

            Debug.LogWarning($"[ConditionEvaluator] Expressão desconhecida: {expr}");
            return false;
        }

        private static bool EvaluateHasNeighbor(string expr, Particle particle, IGridQuery grid)
        {
            string arg = ExtractArgument(expr);
            if (string.IsNullOrEmpty(arg)) return false;
            return HasNeighbor(particle, grid, arg);
        }

        private static readonly Vector3Int[] AllDirectionDeltas =
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };

        /// <summary>
        /// Verifica contato com um vizinho. Com <paramref name="direction"/>
        /// informado (X+, X-, Y+, Y-, Z+ ou Z-), checa apenas aquela face;
        /// caso contrário, checa as seis faces (usado por onTouch sem
        /// direção específica).
        /// </summary>
        public static bool HasNeighbor(Particle particle, IGridQuery grid, string direction = null)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                foreach (Vector3Int d in AllDirectionDeltas)
                    if (grid.TryGetParticleAt(grid.ApplyBoundary(particle.GridPosition + d), out _))
                        return true;
                return false;
            }

            if (!TryParseDirection(direction, out Vector3Int delta))
            {
                Debug.LogWarning($"[ConditionEvaluator] Direção de vizinhança desconhecida: {direction}");
                return false;
            }

            Vector3Int neighborPos = grid.ApplyBoundary(particle.GridPosition + delta);
            return grid.TryGetParticleAt(neighborPos, out _);
        }

        private static bool TryParseDirection(string arg, out Vector3Int delta)
        {
            switch (arg.Trim('\'', '\"').ToUpperInvariant())
            {
                case "X+": delta = new Vector3Int(1, 0, 0); return true;
                case "X-": delta = new Vector3Int(-1, 0, 0); return true;
                case "Y+": delta = new Vector3Int(0, 1, 0); return true;
                case "Y-": delta = new Vector3Int(0, -1, 0); return true;
                case "Z+": delta = new Vector3Int(0, 0, 1); return true;
                case "Z-": delta = new Vector3Int(0, 0, -1); return true;
                default: delta = Vector3Int.zero; return false;
            }
        }

        private static bool EvaluateCheckState(string expr, Particle particle)
        {
            string[] args = ExtractArguments(expr);
            if (args.Length < 3) return false;

            string key = args[0];
            string op = args[1];
            object current = particle.GetInternalState(key);
            object target = ParseValue(args[2]);

            switch (op)
            {
                case "==": return AreEqual(current, target);
                case "!=": return !AreEqual(current, target);
                case ">":
                case "<":
                case ">=":
                case "<=":
                    return CompareValues(current, target, op);
                default:
                    Debug.LogWarning($"[ConditionEvaluator] Operador de comparação desconhecido: {op}");
                    return false;
            }
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

        private static string[] SplitByOperator(string expr, string op) =>
            Regex.Split(expr, $@"\s*\b{op}\b\s*", RegexOptions.IgnoreCase);

        private static string ExtractArgument(string expr)
        {
            int open = expr.IndexOf('(');
            int close = expr.LastIndexOf(')');
            if (open < 0 || close < 0 || close <= open) return null;
            return expr.Substring(open + 1, close - open - 1).Trim();
        }

        private static string[] ExtractArguments(string expr)
        {
            string inner = ExtractArgument(expr);
            if (inner == null) return Array.Empty<string>();

            var args = new List<string>();
            bool inQuotes = false;
            char quoteChar = '\0';
            int start = 0;

            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if ((c == '\'' || c == '\"') && !inQuotes) { inQuotes = true; quoteChar = c; }
                else if (c == quoteChar && inQuotes) { inQuotes = false; quoteChar = '\0'; }
                else if (c == ',' && !inQuotes)
                {
                    args.Add(inner.Substring(start, i - start).Trim().Trim('\'', '\"'));
                    start = i + 1;
                }
            }
            args.Add(inner.Substring(start).Trim().Trim('\'', '\"'));
            return args.ToArray();
        }

        public static object ParseValue(string valStr)
        {
            if (string.IsNullOrEmpty(valStr)) return null;
            valStr = valStr.Trim().Trim('\'', '\"');

            if (bool.TryParse(valStr, out bool b)) return b;
            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;

            return valStr;
        }

        private static bool IsNumeric(object obj) =>
            obj is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}