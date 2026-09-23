using System;
using System.Collections.Generic;
using System.Globalization;

using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public static class ExpressionValidator
    {
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

                Advance();

                List<ValueNode> arguments = ParseArgumentList(signature);
                if (arguments == null) return null;
                if (!ValidateValueArguments(signature, arguments)) return null;
                return new ValueCallNode { FunctionName = signature.Name, Arguments = arguments.ToArray() };
            }

            private List<ValueNode> ParseArgumentList(FunctionSignature signature)
            {
                Advance();

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
                    if (kind == ArgumentKind.Value) return true;
                    if (kind == ArgumentKind.NumericValue) return true;

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

            private static string OperatorTextOf(Token token) => token.Value?.Trim();

            private static bool IsNumericLiteral(Token token)
            {
                if (token.Kind == TokenKind.Number) return true;

                if (token.Kind == TokenKind.QuotedString)
                    return double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

                return false;
            }

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
