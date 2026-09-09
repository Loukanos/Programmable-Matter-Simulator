using System;
using System.Collections.Generic;
using System.Globalization;

using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Syntactic and semantic validation phases (RF04/RNF08) for a single
    /// condition or action string, on top of the token stream produced by
    /// <see cref="ExpressionLexer"/>. As a side effect of validating, also
    /// builds the AST (<see cref="ConditionNode"/>/<see cref="ActionNode"/>,
    /// in Core) that SimulationEngine evaluates — parsing happens here, once,
    /// never again at runtime.
    ///
    /// The condition grammar mirrors the interpreter's precedence — Not binds
    /// tighter than And, which binds tighter than Or, and there is no
    /// parenthesized grouping, so '(' only ever opens a call:
    ///
    ///   Or    := And ( 'Or' And )*
    ///   And   := Not ( 'And' Not )*
    ///   Not   := 'Not'* Term
    ///   Term  := 'true' | 'false' | ConditionReference | Call
    ///   Call  := Identifier '(' ( Argument ( ',' Argument )* )? ')'
    ///   Argument := Token | Identifier '(' ( Argument ( ',' Argument )* )? ')'
    ///
    /// A ConditionReference is a bare identifier (not followed by '(') that
    /// names an entry in the file's "definitions" block — resolved into that
    /// definition's own tree by InputValidation.DefinitionResolver, after
    /// validation, before SimulationEngine ever sees it (see
    /// Core.ConditionReferenceNode). An Argument may itself be a nested call
    /// to any FunctionRole.Value function — recursively, so
    /// opposite(state('dir')) is valid — which is why argument collection can
    /// no longer assume "one token per argument". This includes the 7
    /// pure "read" functions (state/opposite/messageValue/myId/
    /// countNeighbors/canMove/moveResult) plus the two boolean-producing
    /// condition functions that are also dual-role (hasNeighbor,
    /// checkMessage — see Grammar.FunctionRole); checkState is the
    /// deliberate exception that stays Condition-only and can never be
    /// nested.
    ///
    /// An action is a single Call (plus the bare 'wait' form).
    ///
    /// Only the first problem inside one expression is reported: later errors
    /// in the same string are usually cascades of the first, and the file is
    /// rejected either way. Errors across different expressions are all
    /// collected (see <see cref="ValidationResult"/>).
    /// </summary>
    public static class ExpressionValidator
    {
        /// <summary>
        /// Validates a block's "condition" and builds its AST. An absent or
        /// empty condition is valid — it always evaluates true — and yields a
        /// null node (SimulationEngine.ConditionEvaluator treats null the
        /// same way). <paramref name="definitionNames"/> is the set of names
        /// declared in the file's "definitions" block (empty/null when there
        /// is none) — a bare identifier not matching a known function is
        /// accepted as a reference only when it is in this set.
        /// </summary>
        public static bool ValidateCondition(string expression, string context, ValidationResult result, ISet<string> definitionNames, out ConditionNode node)
        {
            node = null;
            if (string.IsNullOrWhiteSpace(expression)) return true;

            if (!ExpressionLexer.TryTokenize(expression, out List<Token> tokens, out string lexicalError))
            {
                result.AddError($"{context}: condição \"{expression}\" — {lexicalError}.");
                return false;
            }

            var parser = new Parser(tokens, definitionNames);
            node = parser.ParseConditionRoot();
            if (node == null)
            {
                result.AddError($"{context}: condição \"{expression}\" — {parser.Error}.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Validates one entry of a block's "actions" and builds its AST.
        /// <paramref name="isOnInit"/> enables the semantic rule that
        /// movement is not allowed at init time. Actions never reference
        /// named conditions, so there is no definitionNames parameter here.
        /// </summary>
        public static bool ValidateAction(string action, string context, bool isOnInit, ValidationResult result, out ActionNode node)
        {
            node = null;

            if (string.IsNullOrWhiteSpace(action))
            {
                result.AddError($"{context}: ação vazia.");
                return false;
            }

            if (!ExpressionLexer.TryTokenize(action, out List<Token> tokens, out string lexicalError))
            {
                result.AddError($"{context}: ação \"{action}\" — {lexicalError}.");
                return false;
            }

            var parser = new Parser(tokens, null);
            node = parser.ParseActionRoot(isOnInit);
            if (node == null)
            {
                result.AddError($"{context}: ação \"{action}\" — {parser.Error}.");
                return false;
            }
            return true;
        }

        /// <summary>Recursive-descent parser/validator over one expression's tokens. Returns the built node on success, null on failure (see Error).</summary>
        private class Parser
        {
            private readonly List<Token> _tokens;
            private readonly ISet<string> _definitionNames;
            private int _index;

            public string Error { get; private set; }

            public Parser(List<Token> tokens, ISet<string> definitionNames)
            {
                _tokens = tokens;
                _definitionNames = definitionNames ?? new HashSet<string>();
            }

            private Token Current => _index < _tokens.Count ? _tokens[_index] : null;
            private bool AtEnd => _index >= _tokens.Count;
            private void Advance() => _index++;

            private ConditionNode Fail(string message)
            {
                Error = message;
                return null;
            }

            public ConditionNode ParseConditionRoot()
            {
                ConditionNode node = ParseOr();
                if (node == null) return null;
                if (!AtEnd) return Fail($"token inesperado '{Current.Text}' após o fim da condição");
                return node;
            }

            public ActionNode ParseActionRoot(bool isOnInit)
            {
                Token first = Current;
                if (first == null) { Fail("ação vazia"); return null; }
                if (first.Kind != TokenKind.Identifier) { Fail($"esperava o nome de uma ação, encontrado '{first.Text}'"); return null; }

                FunctionCallNode call = ParseFunctionCall(FunctionRole.Action, out FunctionSignature signature);
                if (call == null) return null;
                if (!AtEnd) { Fail($"token inesperado '{Current.Text}' após o fim da ação"); return null; }

                if (isOnInit && string.Equals(signature.Name, LanguageVocabulary.Move, StringComparison.OrdinalIgnoreCase))
                {
                    Fail("ações de movimento não são permitidas em onInit");
                    return null;
                }

                return new ActionNode { FunctionName = signature.Name, Arguments = call.Arguments };
            }

            private ConditionNode ParseOr()
            {
                ConditionNode first = ParseAnd();
                if (first == null) return null;

                var node = new OrNode();
                node.Operands.Add(first);
                while (CurrentIsKeyword(LanguageVocabulary.Or))
                {
                    Advance();
                    ConditionNode next = ParseAnd();
                    if (next == null) return null;
                    node.Operands.Add(next);
                }
                return node.Operands.Count == 1 ? node.Operands[0] : node;
            }

            private ConditionNode ParseAnd()
            {
                ConditionNode first = ParseNot();
                if (first == null) return null;

                var node = new AndNode();
                node.Operands.Add(first);
                while (CurrentIsKeyword(LanguageVocabulary.And))
                {
                    Advance();
                    ConditionNode next = ParseNot();
                    if (next == null) return null;
                    node.Operands.Add(next);
                }
                return node.Operands.Count == 1 ? node.Operands[0] : node;
            }

            private ConditionNode ParseNot()
            {
                int negations = 0;
                while (CurrentIsKeyword(LanguageVocabulary.Not)) { Advance(); negations++; }

                ConditionNode term = ParseConditionTerm();
                if (term == null) return null;

                for (int i = 0; i < negations; i++)
                    term = new NotNode { Operand = term };
                return term;
            }

            private bool CurrentIsKeyword(string keyword)
            {
                Token token = Current;
                return token != null
                    && token.Kind == TokenKind.Identifier
                    && string.Equals(token.Value, keyword, StringComparison.OrdinalIgnoreCase);
            }

            private bool NextIsLParen() =>
                _index + 1 < _tokens.Count && _tokens[_index + 1].Kind == TokenKind.LParen;

            private ConditionNode ParseConditionTerm()
            {
                Token token = Current;
                if (token == null) return Fail("fim inesperado da condição");

                if (token.Kind != TokenKind.Identifier)
                    return Fail($"esperava uma condição, encontrado '{token.Text}'");

                if (LanguageVocabulary.IsBooleanLiteral(token.Value))
                {
                    Advance();
                    return new BoolLiteralNode { Value = string.Equals(token.Value, LanguageVocabulary.True, StringComparison.OrdinalIgnoreCase) };
                }

                if (LanguageVocabulary.IsLogicalKeyword(token.Value))
                    return Fail($"operador lógico '{token.Text}' fora de lugar");

                // A bare identifier (no '(' following) names a condição
                // nomeada, not a function call — functions always require
                // parentheses in condition position (none of hasNeighbor/
                // checkState/checkMessage has a zero-argument form).
                if (!NextIsLParen() && _definitionNames.Contains(token.Value))
                {
                    Advance();
                    return new ConditionReferenceNode { Name = token.Value };
                }

                return ParseFunctionCall(FunctionRole.Condition, out _);
            }

            private FunctionCallNode ParseFunctionCall(FunctionRole expectedRole, out FunctionSignature signature)
            {
                signature = null;

                Token nameToken = Current;
                if (nameToken == null || nameToken.Kind != TokenKind.Identifier)
                {
                    Fail("esperava o nome de uma função");
                    return null;
                }

                if (!Grammar.TryGetFunction(nameToken.Value, out signature))
                {
                    string hint = expectedRole == FunctionRole.Condition && _definitionNames.Count > 0
                        ? " nem o nome de uma condição definida em 'definitions'"
                        : "";
                    Fail($"função desconhecida '{nameToken.Text}'{hint} (reconhecidas em {RoleName(expectedRole)}: {Grammar.DescribeFunctions(expectedRole)})");
                    return null;
                }

                if (!signature.Role.HasFlag(expectedRole))
                {
                    Fail($"'{signature.Name}' é uma função de {RoleName(signature.Role)} e não pode ser usada como {RoleName(expectedRole)}");
                    return null;
                }

                Advance();

                if (Current == null || Current.Kind != TokenKind.LParen)
                {
                    // 'wait' is the one function that may also be written bare,
                    // without parentheses.
                    if (signature.AcceptsArgumentCount(0))
                        return new FunctionCallNode { FunctionName = signature.Name, Arguments = Array.Empty<ValueNode>() };
                    Fail($"esperava '(' após '{signature.Name}'");
                    return null;
                }

                List<ValueNode> arguments = ParseArgumentList(signature);
                if (arguments == null) return null;
                if (!ValidateValueArguments(signature, arguments)) return null;
                return new FunctionCallNode { FunctionName = signature.Name, Arguments = arguments.ToArray() };
            }

            /// <summary>
            /// Parses one argument: a nested read-function call — recognized
            /// purely by shape, an identifier immediately followed by '(' —
            /// or a single token used as-is, the only shape arguments had
            /// before nested calls existed. Whether the result is actually
            /// allowed in its slot (bare token vs call, and which functions)
            /// is checked afterward by ValidateValueArgument, once the
            /// enclosing call's overload — and so each slot's expected
            /// ArgumentKind — is resolved by argument count.
            /// </summary>
            private ValueNode ParseValueArgument()
            {
                Token token = Current;
                if (token == null) { Fail("fim inesperado dos argumentos"); return null; }

                bool isNestedCall = token.Kind == TokenKind.Identifier && NextIsLParen();
                if (!isNestedCall)
                {
                    Advance();
                    return new LiteralValueNode { Token = token };
                }

                if (!Grammar.TryGetFunction(token.Value, out FunctionSignature signature) || !signature.Role.HasFlag(FunctionRole.Value))
                {
                    Fail($"função desconhecida '{token.Text}' (reconhecidas como valor: {Grammar.DescribeFunctions(FunctionRole.Value)})");
                    return null;
                }

                Advance(); // the function name; ParseArgumentList consumes the '(' NextIsLParen already confirmed.

                List<ValueNode> arguments = ParseArgumentList(signature);
                if (arguments == null) return null;
                if (!ValidateValueArguments(signature, arguments)) return null;
                return new ValueCallNode { FunctionName = signature.Name, Arguments = arguments.ToArray() };
            }

            /// <summary>
            /// Parses "(" arg (',' arg)* ")" for an already-identified
            /// function — shared by ParseFunctionCall and ParseValueArgument
            /// so the comma/closing-paren handling exists once. Assumes the
            /// current token is '(' and consumes it.
            /// </summary>
            private List<ValueNode> ParseArgumentList(FunctionSignature signature)
            {
                Advance(); // '('

                var arguments = new List<ValueNode>();
                if (Current != null && Current.Kind == TokenKind.RParen)
                {
                    Advance();
                    return arguments;
                }

                while (true)
                {
                    if (Current != null && (Current.Kind == TokenKind.Comma || Current.Kind == TokenKind.RParen || Current.Kind == TokenKind.LParen))
                    {
                        Fail($"argumento ausente ou malformado em '{signature.Name}' perto de '{Current.Text}'");
                        return null;
                    }

                    ValueNode argument = ParseValueArgument();
                    if (argument == null) return null;
                    arguments.Add(argument);

                    if (Current == null)
                    {
                        Fail($"parêntese ')' não fechado em '{signature.Name}'");
                        return null;
                    }
                    if (Current.Kind == TokenKind.Comma) { Advance(); continue; }
                    if (Current.Kind == TokenKind.RParen) { Advance(); return arguments; }

                    Fail($"argumento malformado em '{signature.Name}' perto de '{Current.Text}'");
                    return null;
                }
            }

            private bool ValidateValueArguments(FunctionSignature signature, List<ValueNode> arguments)
            {
                if (!signature.AcceptsArgumentCount(arguments.Count))
                {
                    Fail($"'{signature.Name}' espera {signature.DescribeAcceptedCounts()} argumento(s), mas recebeu {arguments.Count}");
                    return false;
                }

                ArgumentKind[] expected = ResolveOverload(signature, arguments);
                for (int i = 0; i < expected.Length; i++)
                    if (!ValidateValueArgument(signature, expected[i], arguments[i], i))
                        return false;

                return ValidateCheckStateOperandRule(signature, arguments);
            }

            /// <summary>
            /// Picks which same-length overload applies. Only sendMessage has
            /// more than one candidate for a given argument count today (2:
            /// directed-no-payload `(dir, tag)` vs broadcast-with-payload
            /// `(tag, payload)`) — resolved by whether argument 0 is
            /// direction-shaped, via Core.LanguageVocabulary.IsDirectionShaped
            /// (shared with SimulationEngine.SimulationEngine.IsDirected,
            /// which re-derives this exact decision at runtime from the same
            /// ValueNode shape, so the two can never disagree about which
            /// overload a call matched — see that method's doc comment for
            /// the sendMessage('X+', 5) edge case this implies).
            /// </summary>
            private static ArgumentKind[] ResolveOverload(FunctionSignature signature, List<ValueNode> arguments)
            {
                ArgumentKind[][] candidates = signature.OverloadsFor(arguments.Count);
                if (candidates.Length <= 1) return candidates.Length == 1 ? candidates[0] : Array.Empty<ArgumentKind>();

                bool looksLikeDirection = LanguageVocabulary.IsDirectionShaped(arguments[0]);
                foreach (ArgumentKind[] candidate in candidates)
                    if ((candidate[0] == ArgumentKind.Direction) == looksLikeDirection)
                        return candidate;

                return candidates[0];
            }

            private bool ValidateValueArgument(FunctionSignature signature, ArgumentKind kind, ValueNode node, int index)
            {
                string where = $"argumento {index + 1} de '{signature.Name}'";

                if (node is ValueCallNode call)
                {
                    if (kind == ArgumentKind.Direction)
                    {
                        if (call.FunctionName == LanguageVocabulary.State || call.FunctionName == LanguageVocabulary.Opposite)
                            return true;
                        Fail($"{where} espera uma direção — '{call.FunctionName}(...)' não produz uma direção");
                        return false;
                    }
                    if (kind == ArgumentKind.Value) return true; // any FunctionRole.Value function produces a comparable value
                    if (kind == ArgumentKind.NumericValue) return true; // accepted syntactically; not statically known to produce a number — see SimulationEngine.ResolvePayload for the runtime fallback

                    Fail($"{where} não aceita uma função aninhada — deve ser um literal simples");
                    return false;
                }

                Token token = ((LiteralValueNode)node).Token;
                switch (kind)
                {
                    case ArgumentKind.Direction:
                        if (token.Kind == TokenKind.Direction) return true;
                        if (token.Kind == TokenKind.QuotedString && LanguageVocabulary.IsDirection(token.Value)) return true;
                        Fail($"{where} deve ser uma direção válida ({LanguageVocabulary.DescribeDirections()}), mas é '{token.Text}'");
                        return false;

                    case ArgumentKind.RelationalOperator:
                        if (token.Kind == TokenKind.RelationalOperator) return true;
                        if (token.Kind == TokenKind.QuotedString && LanguageVocabulary.IsRelationalOperator(OperatorTextOf(token))) return true;
                        Fail($"{where} deve ser um operador relacional ({LanguageVocabulary.DescribeRelationalOperators()}), mas é '{token.Text}'");
                        return false;

                    case ArgumentKind.StateKey:
                        if (token.Kind == TokenKind.QuotedString)
                        {
                            if (string.IsNullOrWhiteSpace(token.Value))
                            {
                                Fail($"{where} não pode ser um nome de estado vazio");
                                return false;
                            }
                            return true;
                        }
                        // An unquoted word also names a state key at runtime.
                        if (token.Kind == TokenKind.Identifier && !LanguageVocabulary.IsBooleanLiteral(token.Value)) return true;
                        Fail($"{where} deve ser o nome de uma variável de estado, mas é '{token.Text}'");
                        return false;

                    case ArgumentKind.HexColor:
                        if (token.Kind == TokenKind.QuotedString && Grammar.IsHexColor(token.Value)) return true;
                        Fail($"{where} deve ser uma cor hexadecimal entre aspas no formato '#RRGGBB', mas é '{token.Text}'");
                        return false;

                    case ArgumentKind.NumericValue:
                        if (IsNumericLiteral(token)) return true;
                        Fail($"{where} deve ser um literal numérico ou uma chamada a uma função de valor, mas é '{token.Text}'");
                        return false;

                    case ArgumentKind.Literal:
                    case ArgumentKind.Value:
                        if (token.Kind == TokenKind.Number
                            || token.Kind == TokenKind.QuotedString
                            || token.Kind == TokenKind.Identifier
                            || token.Kind == TokenKind.Direction)
                            return true;
                        Fail($"{where} deve ser um literal numérico, booleano ou de texto, mas é '{token.Text}'");
                        return false;
                }

                return true;
            }

            /// <summary>
            /// TCC rule: the magnitude operators (&gt;, &lt;, &gt;=, &lt;=) may only compare
            /// against a numeric literal. == and != stay valid for any type.
            /// Only checked when the value is a plain literal — a computed
            /// value (state(...), moveResult(), ...) can't be verified
            /// until runtime, the same already-documented limitation as a
            /// state key's type.
            /// </summary>
            private bool ValidateCheckStateOperandRule(FunctionSignature signature, List<ValueNode> arguments)
            {
                if (!string.Equals(signature.Name, LanguageVocabulary.CheckState, StringComparison.OrdinalIgnoreCase)) return true;
                if (arguments.Count != 3) return true;

                string op = OperatorTextOf(((LiteralValueNode)arguments[1]).Token);
                if (!LanguageVocabulary.IsMagnitudeOperator(op)) return true;

                if (arguments[2] is not LiteralValueNode literalValue) return true;
                if (IsNumericLiteral(literalValue.Token)) return true;

                Fail($"o operador '{op}' em 'checkState' só compara valores numéricos, mas '{literalValue.Token.Text}' é um literal booleano ou de texto");
                return false;
            }

            /// <summary>The operator an argument denotes, whether written bare (&gt;=) or quoted ('&gt;='), with any padding removed.</summary>
            private static string OperatorTextOf(Token token) => token.Value?.Trim();

            private static bool IsNumericLiteral(Token token)
            {
                if (token.Kind == TokenKind.Number) return true;

                // A quoted number ('5') is still compared numerically at runtime.
                if (token.Kind == TokenKind.QuotedString)
                    return double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

                return false;
            }

            /// <summary>Describes a (possibly combined) role for an error message, e.g. "condição/valor" for a dual-role function.</summary>
            private static string RoleName(FunctionRole role)
            {
                var parts = new List<string>();
                if (role.HasFlag(FunctionRole.Condition)) parts.Add("condição");
                if (role.HasFlag(FunctionRole.Action)) parts.Add("ação");
                if (role.HasFlag(FunctionRole.Value)) parts.Add("valor");
                return parts.Count > 0 ? string.Join("/", parts) : role.ToString();
            }
        }
    }
}
