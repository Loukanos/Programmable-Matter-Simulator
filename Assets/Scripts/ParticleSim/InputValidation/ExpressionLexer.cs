using System.Collections.Generic;
using System.Globalization;

using ParticleSim.Core;

namespace ParticleSim.InputValidation
{
    public static class ExpressionLexer
    {
        public static bool TryTokenize(string input, out List<Token> tokens, out string error)
        {
            tokens = new List<Token>();
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "expressão vazia";
                return false;
            }

            int i = 0;
            while (i < input.Length)
            {
                char c = input[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c == '(') { tokens.Add(Make(TokenKind.LParen, "(", i)); i++; continue; }
                if (c == ')') { tokens.Add(Make(TokenKind.RParen, ")", i)); i++; continue; }
                if (c == ',') { tokens.Add(Make(TokenKind.Comma, ",", i)); i++; continue; }

                if (c == '\'' || c == '"')
                {
                    int close = input.IndexOf(c, i + 1);
                    if (close < 0)
                    {
                        error = $"literal de texto não terminado a partir da posição {i}";
                        return false;
                    }

                    tokens.Add(new Token
                    {
                        Kind = TokenKind.QuotedString,
                        Text = input.Substring(i, close - i + 1),
                        Value = input.Substring(i + 1, close - i - 1),
                        Position = i,
                    });
                    i = close + 1;
                    continue;
                }

                if (TryReadDirection(input, i, out Token direction))
                {
                    tokens.Add(direction);
                    i += direction.Text.Length;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                    tokens.Add(Make(TokenKind.Identifier, input.Substring(start, i - start), start));
                    continue;
                }

                if (StartsNumber(input, i))
                {
                    if (!TryReadNumber(input, ref i, out Token number, out error)) return false;
                    tokens.Add(number);
                    continue;
                }

                if (c == '=' || c == '!' || c == '>' || c == '<')
                {
                    if (i + 1 < input.Length)
                    {
                        string two = input.Substring(i, 2);
                        if (LanguageVocabulary.IsRelationalOperator(two))
                        {
                            tokens.Add(Make(TokenKind.RelationalOperator, two, i));
                            i += 2;
                            continue;
                        }
                    }

                    string one = c.ToString();
                    if (LanguageVocabulary.IsRelationalOperator(one))
                    {
                        tokens.Add(Make(TokenKind.RelationalOperator, one, i));
                        i++;
                        continue;
                    }

                    error = $"operador incompleto '{c}' na posição {i} (esperado um de: {LanguageVocabulary.DescribeRelationalOperators()})";
                    return false;
                }

                error = $"caractere inesperado '{c}' na posição {i}";
                return false;
            }

            return true;
        }

        private static Token Make(TokenKind kind, string text, int position) =>
            new Token { Kind = kind, Text = text, Value = text, Position = position };

        private static bool TryReadDirection(string input, int index, out Token token)
        {
            token = null;
            if (index + 1 >= input.Length) return false;

            char axis = char.ToUpperInvariant(input[index]);
            if (axis != 'X' && axis != 'Y' && axis != 'Z') return false;

            char sign = input[index + 1];
            if (sign != '+' && sign != '-') return false;

            int after = index + 2;
            if (after < input.Length && (char.IsLetterOrDigit(input[after]) || input[after] == '_' || input[after] == '.'))
                return false;

            string text = input.Substring(index, 2);
            token = new Token
            {
                Kind = TokenKind.Direction,
                Text = text,
                Value = text.ToUpperInvariant(),
                Position = index,
            };
            return true;
        }

        private static bool StartsNumber(string input, int index)
        {
            char c = input[index];
            if (char.IsDigit(c) || c == '.') return true;

            if (c != '+' && c != '-') return false;
            if (index + 1 >= input.Length) return false;
            return char.IsDigit(input[index + 1]) || input[index + 1] == '.';
        }

        private static bool TryReadNumber(string input, ref int index, out Token token, out string error)
        {
            token = null;
            error = null;

            int start = index;
            if (input[index] == '+' || input[index] == '-') index++;

            bool anyDigit = false;
            while (index < input.Length && char.IsDigit(input[index])) { index++; anyDigit = true; }

            if (index < input.Length && input[index] == '.')
            {
                index++;
                while (index < input.Length && char.IsDigit(input[index])) { index++; anyDigit = true; }
            }

            string text = input.Substring(start, index - start);

            if (!anyDigit)
            {
                error = $"número malformado '{text}' na posição {start}";
                return false;
            }

            if (index < input.Length && (char.IsLetter(input[index]) || input[index] == '_' || input[index] == '.'))
            {
                error = $"número malformado '{text}{input[index]}' na posição {start}";
                return false;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                error = $"número inválido '{text}' na posição {start}";
                return false;
            }

            token = new Token { Kind = TokenKind.Number, Text = text, Value = text, Position = start };
            return true;
        }
    }
}
