using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParticleSim.Core
{
    public static class LanguageVocabulary
    {
        public const string XPlus = "X+";
        public const string XMinus = "X-";
        public const string YPlus = "Y+";
        public const string YMinus = "Y-";
        public const string ZPlus = "Z+";
        public const string ZMinus = "Z-";

        // Order matters: opposite directions must stay adjacent, and DirectionDeltas follows this order.
        public static readonly string[] Directions = { XPlus, XMinus, YPlus, YMinus, ZPlus, ZMinus };

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
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Directions.Length; i += 2)
            {
                lookup[Directions[i]] = Directions[i + 1];
                lookup[Directions[i + 1]] = Directions[i];
            }
            return lookup;
        }

        public static string NormalizeDirection(string value) =>
            value?.Trim().Trim('\'', '"').Trim().ToUpperInvariant();

        public static bool IsDirection(string value)
        {
            string normalized = NormalizeDirection(value);
            return !string.IsNullOrEmpty(normalized) && DeltaByDirection.ContainsKey(normalized);
        }

        public static bool IsDirectionShaped(ValueNode node)
        {
            if (node is ValueCallNode call)
                return ProducesDirection(call.FunctionName);
            if (node is not LiteralValueNode literal)
                return false;

            Token token = literal.Token;
            return token.Kind == TokenKind.Direction
                || (token.Kind == TokenKind.QuotedString && IsDirection(token.Value));
        }

        public static bool TryGetDirectionDelta(string value, out Vector3Int delta)
        {
            string normalized = NormalizeDirection(value);
            if (!string.IsNullOrEmpty(normalized) && DeltaByDirection.TryGetValue(normalized, out delta))
                return true;

            delta = Vector3Int.zero;
            return false;
        }

        public static string OppositeDirection(string value)
        {
            string normalized = NormalizeDirection(value);
            if (!string.IsNullOrEmpty(normalized) && OppositeByDirection.TryGetValue(normalized, out string opposite))
                return opposite;
            return null;
        }

        public static string DescribeDirections() => string.Join(", ", Directions);

        public const string Equal = "==";
        public const string NotEqual = "!=";
        public const string Greater = ">";
        public const string Less = "<";
        public const string GreaterOrEqual = ">=";
        public const string LessOrEqual = "<=";

        public static readonly string[] RelationalOperators =
            { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual };

        public static readonly string[] MagnitudeOperators =
            { Greater, Less, GreaterOrEqual, LessOrEqual };

        private static readonly HashSet<string> RelationalOperatorSet =
            new HashSet<string>(RelationalOperators, StringComparer.Ordinal);

        private static readonly HashSet<string> MagnitudeOperatorSet =
            new HashSet<string>(MagnitudeOperators, StringComparer.Ordinal);

        // No trimming on purpose: the lexer tests raw two-character slices, so "> " must not match.
        public static bool IsRelationalOperator(string value) =>
            value != null && RelationalOperatorSet.Contains(value);

        public static bool IsMagnitudeOperator(string value) =>
            value != null && MagnitudeOperatorSet.Contains(value);

        public static string DescribeRelationalOperators() => string.Join(", ", RelationalOperators);

        public const string And = "And";
        public const string Or = "Or";
        public const string Not = "Not";

        public static readonly string[] LogicalKeywords = { And, Or, Not };

        private static readonly HashSet<string> LogicalKeywordSet =
            new HashSet<string>(LogicalKeywords, StringComparer.OrdinalIgnoreCase);

        public static bool IsLogicalKeyword(string value) =>
            !string.IsNullOrWhiteSpace(value) && LogicalKeywordSet.Contains(value.Trim());

        public const string True = "true";
        public const string False = "false";

        public static bool IsBooleanLiteral(string value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Trim().Equals(True, StringComparison.OrdinalIgnoreCase)
                || value.Trim().Equals(False, StringComparison.OrdinalIgnoreCase));

        public const string HasNeighbor = "hasNeighbor";
        public const string CheckState = "checkState";
        public const string CheckMessage = "checkMessage";

        public const string Move = "move";
        public const string SetState = "setState";
        public const string SetColor = "setColor";
        public const string Wait = "wait";
        public const string SendMessage = "sendMessage";

        public const string State = "state";
        public const string Opposite = "opposite";
        public const string MessageValue = "messageValue";
        public const string MyId = "myId";
        public const string CountNeighbors = "countNeighbors";
        public const string CanMove = "canMove";
        public const string MoveResult = "moveResult";
        public const string MessageDirection = "messageDirection";

        public static bool ProducesDirection(string functionName) =>
            functionName == State || functionName == Opposite || functionName == MessageDirection;

        private static readonly Dictionary<MoveOutcome, string> MoveOutcomeTokens = new()
        {
            [MoveOutcome.Moved] = "moved",
            [MoveOutcome.BlockedByCollision] = "blockedByCollision",
            [MoveOutcome.Invalid] = "invalid",
            [MoveOutcome.NotAttempted] = "notAttempted",
        };

        public static string DescribeMoveOutcome(MoveOutcome outcome) => MoveOutcomeTokens[outcome];
    }
}
