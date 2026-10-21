using System.Globalization;
using System.Text;

namespace Hydro.Core.Expressions;

internal enum TokenKind
{
    Number,
    String,
    Identifier,
    Symbol,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Position, double Number = 0);

internal static class Lexer
{
    // Ordem importa: símbolos de dois caracteres antes dos de um.
    private static readonly string[] Symbols =
    {
        "==", "!=", "<>", "<=", ">=", "&&", "||",
        "+", "-", "*", "/", "%", "^", "(", ")", "[", "]", ",", ".", "<", ">", "=", "!",
    };

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
            }
            else if (char.IsDigit(ch))
            {
                tokens.Add(ReadNumber(text, ref i));
            }
            else if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
            }
            else if (ch is '"' or '\'')
            {
                tokens.Add(ReadString(text, ref i));
            }
            else
            {
                var symbol = Array.Find(Symbols, s => string.CompareOrdinal(text, i, s, 0, s.Length) == 0);
                if (symbol is null)
                {
                    throw new ExpressionException($"caractere inesperado '{ch}'", i);
                }

                tokens.Add(new Token(TokenKind.Symbol, symbol, i));
                i += symbol.Length;
            }
        }

        tokens.Add(new Token(TokenKind.End, "", text.Length));
        return tokens;
    }

    private static Token ReadNumber(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }
        }

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            var j = i + 1;
            if (j < text.Length && text[j] is '+' or '-')
            {
                j++;
            }

            if (j < text.Length && char.IsDigit(text[j]))
            {
                i = j;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }
            }
        }

        if (i < text.Length && (char.IsLetter(text[i]) || text[i] == '_'))
        {
            throw new ExpressionException($"número malformado '{text[start..(i + 1)]}'", start);
        }

        var literal = text[start..i];
        return new Token(TokenKind.Number, literal, start, double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private static Token ReadString(string text, ref int i)
    {
        var quote = text[i];
        var start = i;
        var sb = new StringBuilder();
        i++;
        while (true)
        {
            if (i >= text.Length)
            {
                throw new ExpressionException("texto sem aspas de fechamento", start);
            }

            if (text[i] == quote)
            {
                // Aspas duplicadas ("" ou '') representam a própria aspa.
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    sb.Append(quote);
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            sb.Append(text[i]);
            i++;
        }

        return new Token(TokenKind.String, sb.ToString(), start);
    }
}
