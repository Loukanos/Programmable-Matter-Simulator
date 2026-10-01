using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public enum ArgumentKind
    {
        Direction,
        StateKey,
        RelationalOperator,
        Literal,
        HexColor,
        NumericValue,
        Value,
    }

    [Flags]
    public enum FunctionRole
    {
        Condition = 1,
        Action = 2,
        Value = 4,
    }

    public enum EventKeyKind { OnInit, OnStep, OnTouch, OnMessage, Unknown }

    public class FunctionSignature
    {
        public string Name;
        public FunctionRole Role;
        public ArgumentKind[][] Overloads;

        public bool AcceptsArgumentCount(int count)
        {
            foreach (ArgumentKind[] overload in Overloads)
                if (overload.Length == count)
                    return true;
            return false;
        }

        public ArgumentKind[][] OverloadsFor(int count)
        {
            var matches = new List<ArgumentKind[]>();
            foreach (ArgumentKind[] overload in Overloads)
                if (overload.Length == count)
                    matches.Add(overload);
            return matches.ToArray();
        }

        public string DescribeAcceptedCounts()
        {
            var counts = new List<int>();
            foreach (ArgumentKind[] overload in Overloads)
                if (!counts.Contains(overload.Length))
                    counts.Add(overload.Length);
            counts.Sort();
            var text = new List<string>();
            foreach (int count in counts) text.Add(count.ToString());
            return string.Join(" ou ", text);
        }
    }

    public struct EventKey
    {
        public EventKeyKind Kind;
        public bool HasDirectionPart;
        public string Direction;
        public string RawName;
    }

    public static class Grammar
    {
        public const string DefinitionsKey = "definitions";

        // Matches "onTouch", "onTouch(X+)", "onMessage( 'Y-' )": a name plus an optional parenthesized parameter.
        private static readonly Regex EventKeyRegex =
            new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(\s*(.*?)\s*\))?\s*$", RegexOptions.Compiled);

        private static readonly Regex HexColorRegex =
            new Regex(@"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

        private static readonly Dictionary<string, FunctionSignature> Functions = BuildFunctions();

        private static readonly string[] NumericValueFunctions =
            { LanguageVocabulary.State, LanguageVocabulary.MessageValue, LanguageVocabulary.MyId, LanguageVocabulary.CountNeighbors };

        private static Dictionary<string, FunctionSignature> BuildFunctions()
        {
            var functions = new Dictionary<string, FunctionSignature>(StringComparer.OrdinalIgnoreCase);

            void Add(string name, FunctionRole role, params ArgumentKind[][] overloads) =>
                functions[name] = new FunctionSignature { Name = name, Role = role, Overloads = overloads };

            Add(LanguageVocabulary.HasNeighbor, FunctionRole.Condition | FunctionRole.Value,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.CheckState, FunctionRole.Condition,
                new[] { ArgumentKind.StateKey, ArgumentKind.RelationalOperator, ArgumentKind.Value });
            Add(LanguageVocabulary.CheckMessage, FunctionRole.Condition | FunctionRole.Value,
                new[] { ArgumentKind.Literal });

            Add(LanguageVocabulary.Move, FunctionRole.Action,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.SetState, FunctionRole.Action,
                new[] { ArgumentKind.StateKey, ArgumentKind.Value });
            Add(LanguageVocabulary.SetColor, FunctionRole.Action,
                new[] { ArgumentKind.HexColor });
            Add(LanguageVocabulary.Wait, FunctionRole.Action,
                Array.Empty<ArgumentKind>());
            Add(LanguageVocabulary.SendMessage, FunctionRole.Action,
                new[] { ArgumentKind.Literal },
                new[] { ArgumentKind.Direction, ArgumentKind.Literal },
                new[] { ArgumentKind.Literal, ArgumentKind.NumericValue },
                new[] { ArgumentKind.Direction, ArgumentKind.Literal, ArgumentKind.NumericValue });

            Add(LanguageVocabulary.State, FunctionRole.Value,
                new[] { ArgumentKind.StateKey });
            Add(LanguageVocabulary.Opposite, FunctionRole.Value,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.MessageValue, FunctionRole.Value,
                Array.Empty<ArgumentKind>());
            Add(LanguageVocabulary.MyId, FunctionRole.Value,
                Array.Empty<ArgumentKind>());
            Add(LanguageVocabulary.CountNeighbors, FunctionRole.Value,
                Array.Empty<ArgumentKind>());
            Add(LanguageVocabulary.CanMove, FunctionRole.Condition | FunctionRole.Value,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.MoveResult, FunctionRole.Value,
                Array.Empty<ArgumentKind>());

            return functions;
        }

        public static bool IsHexColor(string value) =>
            !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value.Trim());

        public static bool TryGetFunction(string name, out FunctionSignature signature)
        {
            signature = null;
            return !string.IsNullOrWhiteSpace(name) && Functions.TryGetValue(name.Trim(), out signature);
        }

        public static string DescribeFunctions(FunctionRole role)
        {
            var names = new List<string>();
            foreach (FunctionSignature signature in Functions.Values)
                if (signature.Role.HasFlag(role))
                    names.Add(signature.Name);
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        public static bool CanProduceNumber(string functionName) =>
            Array.Exists(NumericValueFunctions, name => string.Equals(name, functionName, StringComparison.OrdinalIgnoreCase));

        public static string DescribeNumericValueFunctions() => string.Join(", ", NumericValueFunctions);

        public static EventKey ParseEventKey(string rawKey)
        {
            var key = new EventKey { Kind = EventKeyKind.Unknown, RawName = rawKey };
            if (string.IsNullOrWhiteSpace(rawKey)) return key;

            Match match = EventKeyRegex.Match(rawKey);
            if (!match.Success) return key;

            key.RawName = match.Groups[1].Value;
            key.HasDirectionPart = match.Groups[2].Success;

            if (key.HasDirectionPart)
            {
                string inner = match.Groups[2].Value.Trim().Trim('\'', '"').Trim();
                key.Direction = string.IsNullOrWhiteSpace(inner) ? null : inner;
            }

            if (key.RawName.Equals("onInit", StringComparison.OrdinalIgnoreCase)) key.Kind = EventKeyKind.OnInit;
            else if (key.RawName.Equals("onStep", StringComparison.OrdinalIgnoreCase)) key.Kind = EventKeyKind.OnStep;
            else if (key.RawName.Equals("onTouch", StringComparison.OrdinalIgnoreCase)) key.Kind = EventKeyKind.OnTouch;
            else if (key.RawName.Equals("onMessage", StringComparison.OrdinalIgnoreCase)) key.Kind = EventKeyKind.OnMessage;

            return key;
        }

        public static string DescribeEventKeys() =>
            "onInit, onStep, onTouch, onTouch(DIR), onMessage, onMessage(DIR)";
    }
}
