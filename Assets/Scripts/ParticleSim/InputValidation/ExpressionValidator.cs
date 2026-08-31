using System;
using System.Collections.Generic;
using System.Globalization;

using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    /// <summary>
    /// Syntactic and semantic validation phases (RF04/RNF08) for a single
    /// condition or action string, on top of the token stream produced by
    /// <see cref="ExpressionLexer"/>.
    ///
    /// The condition grammar mirrors the interpreter's precedence — Not binds
    /// tighter than And, which binds tighter than Or, and there is no
    /// parenthesized grouping, so '(' only ever opens a function call:
    ///
    ///   Or    := And ( 'Or' And )*
    ///   And   := Not ( 'And' Not )*
    ///   Not   := 'Not'* Term
    ///   Term  := 'true' | 'false' | Call
    ///   Call  := Identifier '(' ( Argument ( ',' Argument )* )? ')'
    ///
    /// An action is a single Call (plus the bare 'wait' form). Every argument
    /// is exactly one token — no function in this language takes a nested
    /// expression — which is enforced here as a syntax rule.
    ///
    /// Only the first problem inside one expression is reported: later errors
    /// in the same string are usually cascades of the first, and the file is
    /// rejected either way. Errors across different expressions are all
    /// collected (see <see cref="ValidationResult"/>).
    /// </summary>
    public static class ExpressionValidator
    {
        /// <summary>
        /// Validates a block's "condition". An absent or empty condition is
        /// valid — it always evaluates true.
        /// </summary>
        public static void ValidateCondition(string expression, string context, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;

            if (!ExpressionLexer.TryTokenize(expression, out List<Token> tokens, out string lexicalError))
            {
                result.AddError($"{context}: condição \"{expression}\" — {lexicalError}.");
                return;
            }

            var parser = new Parser(tokens);
            if (!parser.ParseConditionRoot())
                result.AddError($"{context}: condição \"{expression}\" — {parser.Error}.");
        }

        /// <summary>
        /// Validates one entry of a block's "actions". <paramref name="isOnInit"/>
        /// enables the semantic rule that movement is not allowed at init time.
        /// </summary>
        public static void ValidateAction(string action, string context, bool isOnInit, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                result.AddError($"{context}: ação vazia.");
                return;
            }

            if (!ExpressionLexer.TryTokenize(action, out List<Token> tokens, out string lexicalError))
            {
                result.AddError($"{context}: ação \"{action}\" — {lexicalError}.");
                return;
            }

            var parser = new Parser(tokens);
            if (!parser.ParseActionRoot(isOnInit))
                result.AddError($"{context}: ação \"{action}\" — {parser.Error}.");
        }

        /// <summary>Recursive-descent validator over one expression's tokens.</summary>
        private class Parser
        {
            private readonly List<Token> _tokens;
            private int _index;

            public string Error { get; private set; }

            public Parser(List<Token> tokens) => _tokens = tokens;

            private Token Current => _index < _tokens.Count ? _tokens[_index] : null;
            private bool AtEnd => _index >= _tokens.Count;
            private void Advance() => _index++;

            private bool Fail(string message)
            {
                Error = message;
                return false;
            }

            public bool ParseConditionRoot()
            {
                if (!ParseOr()) return false;
                if (!AtEnd) return Fail($"token inesperado '{Current.Text}' após o fim da condição");
                return true;
            }

            public bool ParseActionRoot(bool isOnInit)
            {
                Token first = Current;
                if (first == null) return Fail("ação vazia");
                if (first.Kind != TokenKind.Identifier)
                    return Fail($"esperava o nome de uma ação, encontrado '{first.Text}'");

                if (!ParseFunctionCall(FunctionRole.Action, out FunctionSignature signature)) return false;
                if (!AtEnd) return Fail($"token inesperado '{Current.Text}' após o fim da ação");

                if (isOnInit && string.Equals(signature.Name, LanguageVocabulary.Move, StringComparison.OrdinalIgnoreCase))
                    return Fail("ações de movimento não são permitidas em onInit");

                return true;
            }

            private bool ParseOr()
            {
                if (!ParseAnd()) return false;
                while (CurrentIsKeyword(LanguageVocabulary.Or))
                {
                    Advance();
                    if (!ParseAnd()) return false;
                }
                return true;
            }

            private bool ParseAnd()
            {
                if (!ParseNot()) return false;
                while (CurrentIsKeyword(LanguageVocabulary.And))
                {
                    Advance();
                    if (!ParseNot()) return false;
                }
                return true;
            }

            private bool ParseNot()
            {
                while (CurrentIsKeyword(LanguageVocabulary.Not)) Advance();
                return ParseConditionTerm();
            }

            private bool CurrentIsKeyword(string keyword)
            {
                Token token = Current;
                return token != null
                    && token.Kind == TokenKind.Identifier
                    && string.Equals(token.Value, keyword, StringComparison.OrdinalIgnoreCase);
            }

            private bool ParseConditionTerm()
            {
                Token token = Current;
                if (token == null) return Fail("fim inesperado da condição");

                if (token.Kind != TokenKind.Identifier)
                    return Fail($"esperava uma condição, encontrado '{token.Text}'");

                if (LanguageVocabulary.IsBooleanLiteral(token.Value))
                {
                    Advance();
                    return true;
                }

                if (LanguageVocabulary.IsLogicalKeyword(token.Value))
                    return Fail($"operador lógico '{token.Text}' fora de lugar");

                return ParseFunctionCall(FunctionRole.Condition, out _);
            }

            private bool ParseFunctionCall(FunctionRole expectedRole, out FunctionSignature signature)
            {
                signature = null;

                Token nameToken = Current;
                if (nameToken == null || nameToken.Kind != TokenKind.Identifier)
                    return Fail("esperava o nome de uma função");

                if (!Grammar.TryGetFunction(nameToken.Value, out signature))
                    return Fail($"função desconhecida '{nameToken.Text}' (reconhecidas em {RoleName(expectedRole)}: {Grammar.DescribeFunctions(expectedRole)})");

                if (signature.Role != expectedRole)
                    return Fail($"'{signature.Name}' é uma função de {RoleName(signature.Role)} e não pode ser usada como {RoleName(expectedRole)}");

                Advance();

                if (Current == null || Current.Kind != TokenKind.LParen)
                {
                    // 'wait' is the one function that may also be written bare,
                    // without parentheses.
                    if (signature.AcceptsArgumentCount(0)) return true;
                    return Fail($"esperava '(' após '{signature.Name}'");
                }

                Advance();

                var arguments = new List<Token>();
                if (Current != null && Current.Kind == TokenKind.RParen)
                {
                    Advance();
                }
                else
                {
                    while (true)
                    {
                        Token argument = Current;
                        if (argument == null)
                            return Fail($"parêntese ')' não fechado em '{signature.Name}'");

                        if (argument.Kind == TokenKind.Comma || argument.Kind == TokenKind.RParen || argument.Kind == TokenKind.LParen)
                            return Fail($"argumento ausente ou malformado em '{signature.Name}' perto de '{argument.Text}'");

                        arguments.Add(argument);
                        Advance();

                        if (Current == null)
                            return Fail($"parêntese ')' não fechado em '{signature.Name}'");

                        if (Current.Kind == TokenKind.Comma) { Advance(); continue; }
                        if (Current.Kind == TokenKind.RParen) { Advance(); break; }

                        // Every argument in this language is a single token.
                        return Fail($"argumento malformado em '{signature.Name}' perto de '{Current.Text}'");
                    }
                }

                return ValidateArguments(signature, arguments);
            }

            private bool ValidateArguments(FunctionSignature signature, List<Token> arguments)
            {
                if (!signature.AcceptsArgumentCount(arguments.Count))
                    return Fail($"'{signature.Name}' espera {signature.DescribeAcceptedCounts()} argumento(s), mas recebeu {arguments.Count}");

                ArgumentKind[] expected = signature.OverloadFor(arguments.Count);
                for (int i = 0; i < expected.Length; i++)
                    if (!ValidateArgument(signature, expected[i], arguments[i], i))
                        return false;

                return ValidateCheckStateOperandRule(signature, arguments);
            }

            private bool ValidateArgument(FunctionSignature signature, ArgumentKind kind, Token token, int index)
            {
                string where = $"argumento {index + 1} de '{signature.Name}'";

                switch (kind)
                {
                    case ArgumentKind.Direction:
                        if (token.Kind == TokenKind.Direction) return true;
                        if (token.Kind == TokenKind.QuotedString && LanguageVocabulary.IsDirection(token.Value)) return true;
                        return Fail($"{where} deve ser uma direção válida ({LanguageVocabulary.DescribeDirections()}), mas é '{token.Text}'");

                    case ArgumentKind.RelationalOperator:
                        if (token.Kind == TokenKind.RelationalOperator) return true;
                        if (token.Kind == TokenKind.QuotedString && LanguageVocabulary.IsRelationalOperator(OperatorTextOf(token))) return true;
                        return Fail($"{where} deve ser um operador relacional ({LanguageVocabulary.DescribeRelationalOperators()}), mas é '{token.Text}'");

                    case ArgumentKind.StateKey:
                        if (token.Kind == TokenKind.QuotedString)
                        {
                            if (string.IsNullOrWhiteSpace(token.Value))
                                return Fail($"{where} não pode ser um nome de estado vazio");
                            return true;
                        }
                        // An unquoted word also names a state key at runtime.
                        if (token.Kind == TokenKind.Identifier && !LanguageVocabulary.IsBooleanLiteral(token.Value)) return true;
                        return Fail($"{where} deve ser o nome de uma variável de estado, mas é '{token.Text}'");

                    case ArgumentKind.HexColor:
                        if (token.Kind == TokenKind.QuotedString && Grammar.IsHexColor(token.Value)) return true;
                        return Fail($"{where} deve ser uma cor hexadecimal entre aspas no formato '#RRGGBB', mas é '{token.Text}'");

                    case ArgumentKind.Literal:
                        if (token.Kind == TokenKind.Number
                            || token.Kind == TokenKind.QuotedString
                            || token.Kind == TokenKind.Identifier
                            || token.Kind == TokenKind.Direction)
                            return true;
                        return Fail($"{where} deve ser um literal numérico, booleano ou de texto, mas é '{token.Text}'");
                }

                return true;
            }

            /// <summary>
            /// TCC rule: the magnitude operators (&gt;, &lt;, &gt;=, &lt;=) may only compare
            /// against a numeric literal. == and != stay valid for any type.
            /// </summary>
            private bool ValidateCheckStateOperandRule(FunctionSignature signature, List<Token> arguments)
            {
                if (!string.Equals(signature.Name, LanguageVocabulary.CheckState, StringComparison.OrdinalIgnoreCase)) return true;
                if (arguments.Count != 3) return true;

                string op = OperatorTextOf(arguments[1]);
                if (!LanguageVocabulary.IsMagnitudeOperator(op)) return true;

                Token value = arguments[2];
                if (IsNumericLiteral(value)) return true;

                return Fail($"o operador '{op}' em 'checkState' só compara valores numéricos, mas '{value.Text}' é um literal booleano ou de texto");
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

            private static string RoleName(FunctionRole role) =>
                role == FunctionRole.Condition ? "condição" : "ação";
        }
    }
}
