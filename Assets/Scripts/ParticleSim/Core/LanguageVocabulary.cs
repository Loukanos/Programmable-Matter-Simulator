using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParticleSim.Core
{
    /// <summary>
    /// The single source of truth for the declarative language's vocabulary:
    /// the six direction constants (and their grid deltas), the relational and
    /// logical operators, and the function names.
    ///
    /// It lives in Core precisely so that both sides of the language can share
    /// it without inverting the module dependencies: `InputValidation` reads it
    /// to decide whether a file is well formed, and `SimulationEngine` reads it
    /// to execute what the file says. Neither module depends on the other, and
    /// the vocabulary is written down exactly once.
    ///
    /// What is *not* here is anything only one side needs: argument-kind
    /// signatures, event-key shapes and the hex-color pattern are validation
    /// concerns and stay in `InputValidation.Grammar`; how a condition is
    /// evaluated or an action applied stays in `SimulationEngine`.
    /// </summary>
    public static class LanguageVocabulary
    {
        // ---- Directions ----

        public const string XPlus = "X+";
        public const string XMinus = "X-";
        public const string YPlus = "Y+";
        public const string YMinus = "Y-";
        public const string ZPlus = "Z+";
        public const string ZMinus = "Z-";

        /// <summary>The six face directions, in a stable order.</summary>
        public static readonly string[] Directions = { XPlus, XMinus, YPlus, YMinus, ZPlus, ZMinus };

        /// <summary>The grid deltas of <see cref="Directions"/>, in the same order.</summary>
        public static readonly Vector3Int[] DirectionDeltas =
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };

        private static readonly Dictionary<string, Vector3Int> DeltaByDirection = BuildDeltaLookup();
        private static readonly Dictionary<string, string> OppositeByDirection = BuildOppositeLookup();

        private static Dictionary<string, Vector3Int> BuildDeltaLookup()
        {
            var lookup = new Dictionary<string, Vector3Int>(StringComparer.Ordinal);
            for (int i = 0; i < Directions.Length; i++)
                lookup[Directions[i]] = DirectionDeltas[i];
            return lookup;
        }

        private static Dictionary<string, string> BuildOppositeLookup()
        {
            // The six directions are three opposing pairs laid out consecutively
            // in Directions, so each entry pairs with its neighbour.
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Directions.Length; i += 2)
            {
                lookup[Directions[i]] = Directions[i + 1];
                lookup[Directions[i + 1]] = Directions[i];
            }
            return lookup;
        }

        /// <summary>
        /// Normalizes a direction as written in the behavior file: the language
        /// accepts it quoted or bare, in any case, so 'X+', "X+", X+ and x+ all
        /// mean the same direction.
        /// </summary>
        public static string NormalizeDirection(string value) =>
            value?.Trim().Trim('\'', '"').Trim().ToUpperInvariant();

        public static bool IsDirection(string value)
        {
            string normalized = NormalizeDirection(value);
            return !string.IsNullOrEmpty(normalized) && DeltaByDirection.ContainsKey(normalized);
        }

        /// <summary>Grid displacement of a direction, or false when it is not one of the six.</summary>
        public static bool TryGetDirectionDelta(string value, out Vector3Int delta)
        {
            string normalized = NormalizeDirection(value);
            if (!string.IsNullOrEmpty(normalized) && DeltaByDirection.TryGetValue(normalized, out delta))
                return true;

            delta = Vector3Int.zero;
            return false;
        }

        /// <summary>
        /// The opposite direction (X+ ↔ X-, Y+ ↔ Y-, Z+ ↔ Z-), or null when the
        /// input is not a valid direction.
        /// </summary>
        public static string OppositeDirection(string value)
        {
            string normalized = NormalizeDirection(value);
            if (!string.IsNullOrEmpty(normalized) && OppositeByDirection.TryGetValue(normalized, out string opposite))
                return opposite;
            return null;
        }

        public static string DescribeDirections() => string.Join(", ", Directions);

        // ---- Relational operators ----

        public const string Equal = "==";
        public const string NotEqual = "!=";
        public const string Greater = ">";
        public const string Less = "<";
        public const string GreaterOrEqual = ">=";
        public const string LessOrEqual = "<=";

        public static readonly string[] RelationalOperators =
            { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual };

        /// <summary>The operators that compare magnitude, which the TCC restricts to numeric operands.</summary>
        public static readonly string[] MagnitudeOperators =
            { Greater, Less, GreaterOrEqual, LessOrEqual };

        private static readonly HashSet<string> RelationalOperatorSet =
            new HashSet<string>(RelationalOperators, StringComparer.Ordinal);

        private static readonly HashSet<string> MagnitudeOperatorSet =
            new HashSet<string>(MagnitudeOperators, StringComparer.Ordinal);

        /// <summary>
        /// Exact match, deliberately without trimming: the lexer tests raw
        /// two-character slices, so tolerating whitespace here would make "&gt; "
        /// read as a single two-character operator. Callers holding quoted text
        /// trim it themselves before asking.
        /// </summary>
        public static bool IsRelationalOperator(string value) =>
            value != null && RelationalOperatorSet.Contains(value);

        public static bool IsMagnitudeOperator(string value) =>
            value != null && MagnitudeOperatorSet.Contains(value);

        public static string DescribeRelationalOperators() => string.Join(", ", RelationalOperators);

        // ---- Logical combinators ----

        public const string And = "And";
        public const string Or = "Or";
        public const string Not = "Not";

        public static readonly string[] LogicalKeywords = { And, Or, Not };

        private static readonly HashSet<string> LogicalKeywordSet =
            new HashSet<string>(LogicalKeywords, StringComparer.OrdinalIgnoreCase);

        public static bool IsLogicalKeyword(string value) =>
            !string.IsNullOrWhiteSpace(value) && LogicalKeywordSet.Contains(value.Trim());

        // ---- Literals ----

        public const string True = "true";
        public const string False = "false";

        public static bool IsBooleanLiteral(string value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Trim().Equals(True, StringComparison.OrdinalIgnoreCase)
                || value.Trim().Equals(False, StringComparison.OrdinalIgnoreCase));

        // ---- Function names ----

        public const string HasNeighbor = "hasNeighbor";
        public const string CheckState = "checkState";
        public const string CheckMessage = "checkMessage";

        public const string Move = "move";
        public const string SetState = "setState";
        public const string SetColor = "setColor";
        public const string Wait = "wait";
        public const string SendMessage = "sendMessage";
    }
}
