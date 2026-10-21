using System.Text;
using Hydro.Core.Errors;
using Hydro.Core.Expressions;

namespace Hydro.Core.Text;

/// <summary>Texto com campos <c>{expressão}</c> ou <c>{expressão:formato}</c> (ver
/// <see cref="FormatSpec"/>): é o motor único de macros de mensagens, código, nome, dica, rótulos e
/// tabelas. <c>{{</c> e <c>}}</c> escrevem chaves. Sem formato, o valor sai como no restante da
/// linguagem (<c>1.5</c>); com formato, em pt-BR (<c>1,50 m</c>).</summary>
public sealed class Template
{
    private readonly IReadOnlyList<Part> _parts;

    private Template(string text, IReadOnlyList<Part> parts)
    {
        Text = text;
        _parts = parts;
    }

    public string Text { get; }

    /// <summary>Expressões dos campos (para a verificação de nomes na carga).</summary>
    public IEnumerable<Expression> Expressions => _parts.Where(p => p.Expression is not null).Select(p => p.Expression!);

    public bool IsConstant => _parts.All(p => p.Expression is null);

    /// <summary>Analisa o texto. Erros (chave sem par, expressão ou formato inválidos) saem como
    /// <see cref="DefinitionException"/> com a posição no arquivo.</summary>
    public static Template Parse(string text, SourceLocation? location = null, string? field = null, int columnShift = 0)
    {
        var loc = location ?? SourceLocation.None;
        var parts = new List<Part>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '{' && i + 1 < text.Length && text[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }

            if (ch == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            if (ch == '}')
            {
                throw new DefinitionException("'}' sem '{' correspondente (use '}}' para escrever uma chave)", Shift(loc, columnShift + i), field);
            }

            if (ch != '{')
            {
                literal.Append(ch);
                i++;
                continue;
            }

            var end = FindClose(text, i + 1);
            if (end < 0)
            {
                throw new DefinitionException("'{' sem '}' correspondente (use '{{' para escrever uma chave)", Shift(loc, columnShift + i), field);
            }

            if (literal.Length > 0)
            {
                parts.Add(new Part(literal.ToString(), null, null));
                literal.Clear();
            }

            var inner = text[(i + 1)..end];
            var colon = TopLevelColon(inner);
            var expressionText = colon < 0 ? inner : inner[..colon];
            FormatSpec? spec = null;
            if (colon >= 0)
            {
                try
                {
                    spec = FormatSpec.Parse(inner[(colon + 1)..]);
                }
                catch (FormatException ex)
                {
                    throw new DefinitionException(ex.Message, Shift(loc, columnShift + i + 1 + colon), field);
                }
            }

            parts.Add(new Part(null, Expression.Parse(expressionText, loc, field, columnShift + i + 1), spec));
            i = end + 1;
        }

        if (literal.Length > 0)
        {
            parts.Add(new Part(literal.ToString(), null, null));
        }

        return new Template(text, parts);
    }

    public string Render(IEvaluationContext context)
    {
        var sb = new StringBuilder();
        foreach (var part in _parts)
        {
            if (part.Expression is null)
            {
                sb.Append(part.Literal);
                continue;
            }

            var value = part.Expression.Evaluate(context);
            sb.Append(part.Spec is null ? Values.Format(value) : part.Spec.Format(value));
        }

        return sb.ToString();
    }

    public override string ToString() => Text;

    private static SourceLocation Shift(SourceLocation location, int columns) =>
        location.IsKnown ? location with { Column = location.Column + columns } : location;

    /// <summary>Fecha o campo no primeiro '}' fora de aspas.</summary>
    private static int FindClose(string text, int from)
    {
        char? quote = null;
        for (var i = from; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '}')
            {
                return i;
            }
            else if (ch == '{')
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>Primeiro ':' fora de aspas, parênteses e colchetes (as expressões não usam ':').</summary>
    private static int TopLevelColon(string text)
    {
        char? quote = null;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (ch)
            {
                case '"' or '\'':
                    quote = ch;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ':' when depth == 0:
                    return i;
            }
        }

        return -1;
    }

    private sealed record Part(string? Literal, Expression? Expression, FormatSpec? Spec);
}
