using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>What a function argument is allowed to be.</summary>
    public enum ArgumentKind
    {
        /// <summary>
        /// One of the six face directions, quoted or bare (e.g. 'X+' or X+),
        /// or a nested call to state(...)/opposite(...) (recursively) — the
        /// direction-as-variable extension.
        /// </summary>
        Direction,
        /// <summary>The name of an internal state variable — always a plain literal, never computed.</summary>
        StateKey,
        /// <summary>==, !=, &gt;, &lt;, &gt;= or &lt;=, quoted or bare — always a plain literal.</summary>
        RelationalOperator,
        /// <summary>A numeric, boolean or textual literal — always a plain literal (checkMessage's tag, sendMessage's tag).</summary>
        Literal,
        /// <summary>A quoted hexadecimal color such as '#RRGGBB' — always a plain literal.</summary>
        HexColor,
        /// <summary>
        /// A numeric literal, or a nested call to any FunctionRole.Value
        /// function — sendMessage's optional payload. Unlike the general
        /// Value kind, a literal here must actually be numeric (checked
        /// statically, same as before); a nested call is accepted
        /// syntactically but isn't statically known to produce a number —
        /// see SimulationEngine.SimulationEngine.ResolvePayload for the
        /// runtime fallback when it doesn't.
        /// </summary>
        NumericValue,
        /// <summary>
        /// A literal of any type, or a nested call to any FunctionRole.Value
        /// function — the 7 pure "read" functions (state/opposite/
        /// messageValue/myId/countNeighbors/canMove/moveResult) plus the
        /// boolean-producing condition functions that are also dual-role
        /// (hasNeighbor, checkMessage). checkState's comparison value and
        /// setState's assigned value.
        /// </summary>
        Value,
    }

    /// <summary>
    /// Where a function may legally appear. [Flags]: a function can hold more
    /// than one role at once. Every boolean-producing function — hasNeighbor,
    /// checkMessage, canMove — is registered as Condition | Value, so it can
    /// be used both directly ("condition": "canMove('X+')") and nested inside
    /// checkState/setState ("checkState('k', '==', canMove('X+'))"), evaluated
    /// by the exact same code either way (see SimulationEngine.
    /// ConditionEvaluator.EvaluateHasNeighbor/EvaluateCheckMessage and
    /// ValueResolver.ResolveCanMove, each called from both dispatch sites).
    /// checkState itself is the one deliberate exception: Condition-only,
    /// never nestable as a value, even though it also produces a boolean —
    /// nesting checkState inside checkState was never requested and adds
    /// nothing checkState's own generalized operand doesn't already cover.
    /// Any new boolean-producing function should default to
    /// Condition | Value here, matching this convention, rather than being
    /// decided case by case.
    /// </summary>
    [Flags]
    public enum FunctionRole
    {
        /// <summary>Usable inside a block's "condition", directly.</summary>
        Condition = 1,
        /// <summary>Usable inside a block's "actions".</summary>
        Action = 2,
        /// <summary>Usable as a nested argument in a Direction or Value slot.</summary>
        Value = 4,
    }

    public enum EventKeyKind { OnInit, OnStep, OnTouch, OnMessage, Unknown }

    /// <summary>
    /// One declarative-language function: its role and every argument list
    /// shape it accepts. sendMessage is the one function with more than one
    /// shape sharing the same argument count (2: directed-no-payload vs
    /// broadcast-with-payload) — see ExpressionValidator.ResolveOverload,
    /// which is what actually picks between same-length candidates by
    /// inspecting argument 0's shape; every other function here has at most
    /// one overload per length, so <see cref="OverloadsFor"/> returning more
    /// than one entry is specific to sendMessage.
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

        /// <summary>Every overload accepting exactly this many arguments — usually 0 or 1, but sendMessage can have 2 (see class doc comment).</summary>
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
        /// <summary>Reserved top-level behavior.json key for named conditions — a sibling of the particle-type entries, not a type itself.</summary>
        public const string DefinitionsKey = "definitions";

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

            // hasNeighbor/checkMessage/canMove are boolean-producing and dual-role
            // (Condition | Value) — see the FunctionRole doc comment above.
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
            // 4 shapes: broadcast (tag) / directed (dir, tag) / broadcast with
            // payload (tag, payload) / directed with payload (dir, tag,
            // payload). The two 2-argument shapes share a length — resolved
            // by argument 0's shape, not by count alone (see
            // ExpressionValidator.ResolveOverload and SimulationEngine.
            // SimulationEngine.IsDirected, which re-derives the same
            // decision at runtime from the same ValueNode shape).
            Add(LanguageVocabulary.SendMessage, FunctionRole.Action,
                new[] { ArgumentKind.Literal },
                new[] { ArgumentKind.Direction, ArgumentKind.Literal },
                new[] { ArgumentKind.Literal, ArgumentKind.NumericValue },
                new[] { ArgumentKind.Direction, ArgumentKind.Literal, ArgumentKind.NumericValue });

            // Value-producing ("read") functions — usable only as a nested
            // argument in a Direction or Value slot, never standalone.
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

        /// <summary>Names of every function valid in the given role (a dual-role function is listed under both), for error messages.</summary>
        public static string DescribeFunctions(FunctionRole role)
        {
            var names = new List<string>();
            foreach (FunctionSignature signature in Functions.Values)
                if (signature.Role.HasFlag(role))
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
