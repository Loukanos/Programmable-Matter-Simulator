using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>What a function argument is allowed to be.</summary>
    public enum ArgumentKind
    {
        /// <summary>One of the six face directions, quoted or bare (e.g. 'X+' or X+).</summary>
        Direction,
        /// <summary>The name of an internal state variable.</summary>
        StateKey,
        /// <summary>==, !=, &gt;, &lt;, &gt;= or &lt;=, quoted or bare.</summary>
        RelationalOperator,
        /// <summary>A numeric, boolean or textual literal.</summary>
        Literal,
        /// <summary>A quoted hexadecimal color such as '#RRGGBB'.</summary>
        HexColor,
    }

    /// <summary>Where a function may legally appear.</summary>
    public enum FunctionRole
    {
        /// <summary>Usable inside a block's "condition".</summary>
        Condition,
        /// <summary>Usable inside a block's "actions".</summary>
        Action,
    }

    public enum EventKeyKind { OnInit, OnStep, OnTouch, OnMessage, Unknown }

    /// <summary>
    /// One declarative-language function: its role and every argument list
    /// shape it accepts (sendMessage is the only one with two shapes, since
    /// its direction argument is optional).
    /// </summary>
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

        public ArgumentKind[] OverloadFor(int count)
        {
            foreach (ArgumentKind[] overload in Overloads)
                if (overload.Length == count)
                    return overload;
            return null;
        }

        public string DescribeAcceptedCounts()
        {
            var counts = new List<string>();
            foreach (ArgumentKind[] overload in Overloads)
                counts.Add(overload.Length.ToString());
            return string.Join(" ou ", counts);
        }
    }

    /// <summary>
    /// A parsed behavior-file event key: "onStep", "onTouch", "onTouch(X+)",
    /// "onMessage(Y-)" and so on.
    /// </summary>
    public struct EventKey
    {
        public EventKeyKind Kind;
        /// <summary>True when the key carried a "(...)" part at all, even an empty or invalid one.</summary>
        public bool HasDirectionPart;
        /// <summary>The text inside the parentheses, or null when there was none.</summary>
        public string Direction;
        public string RawName;
    }

    /// <summary>
    /// The validation-side view of the declarative language: function
    /// signatures (role plus accepted argument lists), the event-key shapes and
    /// the hex-color pattern — the things only the validator needs.
    ///
    /// The language's *vocabulary* — the six directions, the relational and
    /// logical operators, the boolean literals and the function names — is not
    /// restated here: it lives in <see cref="LanguageVocabulary"/> in Core, so
    /// that this module and SimulationEngine's interpreter read the same
    /// definitions without either depending on the other.
    /// </summary>
    public static class Grammar
    {
        // "onTouch", "onTouch(X+)", "onMessage( 'Y-' )" — name plus an optional
        // parenthesized parameter. Quotes around the parameter are tolerated to
        // match how the rest of the language treats direction literals.
        private static readonly Regex EventKeyRegex =
            new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(\s*(.*?)\s*\))?\s*$", RegexOptions.Compiled);

        private static readonly Regex HexColorRegex =
            new Regex(@"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

        private static readonly Dictionary<string, FunctionSignature> Functions = BuildFunctions();

        private static Dictionary<string, FunctionSignature> BuildFunctions()
        {
            var functions = new Dictionary<string, FunctionSignature>(StringComparer.OrdinalIgnoreCase);

            void Add(string name, FunctionRole role, params ArgumentKind[][] overloads) =>
                functions[name] = new FunctionSignature { Name = name, Role = role, Overloads = overloads };

            Add(LanguageVocabulary.HasNeighbor, FunctionRole.Condition,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.CheckState, FunctionRole.Condition,
                new[] { ArgumentKind.StateKey, ArgumentKind.RelationalOperator, ArgumentKind.Literal });
            Add(LanguageVocabulary.CheckMessage, FunctionRole.Condition,
                new[] { ArgumentKind.Literal });

            Add(LanguageVocabulary.Move, FunctionRole.Action,
                new[] { ArgumentKind.Direction });
            Add(LanguageVocabulary.SetState, FunctionRole.Action,
                new[] { ArgumentKind.StateKey, ArgumentKind.Literal });
            Add(LanguageVocabulary.SetColor, FunctionRole.Action,
                new[] { ArgumentKind.HexColor });
            Add(LanguageVocabulary.Wait, FunctionRole.Action,
                Array.Empty<ArgumentKind>());
            Add(LanguageVocabulary.SendMessage, FunctionRole.Action,
                new[] { ArgumentKind.Literal },
                new[] { ArgumentKind.Direction, ArgumentKind.Literal });

            return functions;
        }

        public static bool IsHexColor(string value) =>
            !string.IsNullOrWhiteSpace(value) && HexColorRegex.IsMatch(value.Trim());

        public static bool TryGetFunction(string name, out FunctionSignature signature)
        {
            signature = null;
            return !string.IsNullOrWhiteSpace(name) && Functions.TryGetValue(name.Trim(), out signature);
        }

        /// <summary>Names of every function valid in the given role, for error messages.</summary>
        public static string DescribeFunctions(FunctionRole role)
        {
            var names = new List<string>();
            foreach (FunctionSignature signature in Functions.Values)
                if (signature.Role == role)
                    names.Add(signature.Name);
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        /// <summary>
        /// Recognizes a behavior-file event key. Returns Kind = Unknown when
        /// the key is not one of onInit/onStep/onTouch/onMessage; the caller
        /// decides whether a direction part is legal for that kind (onInit and
        /// onStep take none).
        /// </summary>
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

        /// <summary>Event keys accepted by the behavior file, for error messages.</summary>
        public static string DescribeEventKeys() =>
            "onInit, onStep, onTouch, onTouch(DIR), onMessage, onMessage(DIR)";
    }
}
