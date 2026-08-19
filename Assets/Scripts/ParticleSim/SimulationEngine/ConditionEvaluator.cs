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
            arg = arg.Trim('\'', '\"');

            Vector3Int delta;
            switch (arg.ToUpperInvariant())
            {
                case "X+": delta = new Vector3Int(1, 0, 0); break;
                case "X-": delta = new Vector3Int(-1, 0, 0); break;
                case "Y+": delta = new Vector3Int(0, 1, 0); break;
                case "Y-": delta = new Vector3Int(0, -1, 0); break;
                case "Z+": delta = new Vector3Int(0, 0, 1); break;
                case "Z-": delta = new Vector3Int(0, 0, -1); break;
                default:
                    Debug.LogWarning($"[ConditionEvaluator] Direção de vizinhança desconhecida: {arg}");
                    return false;
            }

            Vector3Int neighborPos = grid.ApplyBoundary(particle.GridPosition + delta);
            return grid.TryGetParticleAt(neighborPos, out _);
        }

        private static bool EvaluateCheckState(string expr, Particle particle)
        {
            string[] args = ExtractArguments(expr);
            if (args.Length < 3) return false;

            string key = args[0];
            string op = args[1];
            object current = particle.GetInternalState(key);
            object target = ParseValue(args[2]);

            if (op != "==") return false;
            if (current == null) return target == null;
            if (target == null) return false;

            if (IsNumeric(current) && IsNumeric(target))
                return Math.Abs(Convert.ToDouble(current, CultureInfo.InvariantCulture) - Convert.ToDouble(target, CultureInfo.InvariantCulture)) < 1e-6;

            if (current.Equals(target)) return true;

            return current.ToString().Trim().Equals(target.ToString().Trim(), StringComparison.OrdinalIgnoreCase);
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