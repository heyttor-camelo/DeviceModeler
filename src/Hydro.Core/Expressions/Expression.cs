using Hydro.Core.Errors;

namespace Hydro.Core.Expressions;

/// <summary>Expressão já analisada, ligada à sua posição no arquivo de definição para que
/// erros de sintaxe e de avaliação apontem linha e coluna.</summary>
public sealed class Expression
{
    private Expression(string text, Node root, SourceLocation location, string? field, int columnShift)
    {
        Text = text;
        Root = root;
        Location = location;
        Field = field;
        ColumnShift = columnShift;
        FreeNames = CollectNames(root);
    }

    public string Text { get; }

    public Node Root { get; }

    /// <summary>Posição do início do texto no arquivo.</summary>
    public SourceLocation Location { get; }

    /// <summary>Campo de origem (ex.: <c>variaveis.H</c>), usado nas mensagens.</summary>
    public string? Field { get; }

    /// <summary>Colunas entre <see cref="Location"/> e o primeiro caractere do texto (ex.: aspas).</summary>
    internal int ColumnShift { get; }

    /// <summary>Nomes livres referenciados (parâmetros/variáveis/locais), com a posição da 1ª ocorrência.
    /// Não inclui nomes de funções nem campos após '.'.</summary>
    public IReadOnlyDictionary<string, int> FreeNames { get; }

    /// <summary>Analisa <paramref name="text"/>; erros viram <see cref="DefinitionException"/> com linha/coluna.</summary>
    public static Expression Parse(string text, SourceLocation? location = null, string? field = null, int columnShift = 0)
    {
        var loc = location ?? SourceLocation.None;
        try
        {
            return new Expression(text, ExpressionParser.Parse(text), loc, field, columnShift);
        }
        catch (ExpressionException ex)
        {
            throw new DefinitionException($"expressão inválida \"{text.Trim()}\": {ex.Message}", LocationOf(loc, text, columnShift, ex.Position), field, ex);
        }
    }

    /// <summary>Atalho para avaliar um texto avulso.</summary>
    public static object Evaluate(string text, IReadOnlyDictionary<string, object>? values = null) =>
        Parse(text).Evaluate(new DictionaryContext(values));

    public object Evaluate(IEvaluationContext context)
    {
        try
        {
            return Evaluator.Evaluate(Root, context);
        }
        catch (ExpressionException ex)
        {
            throw new EvaluationException(ex.Message + $" (em \"{Text.Trim()}\")", LocationAt(ex.Position), Field, ex);
        }
    }

    public double EvaluateNumber(IEvaluationContext context, string what = "a expressão")
    {
        var value = Evaluate(context);
        return value is double d
            ? d
            : throw new EvaluationException($"{what} deve resultar em número, mas resultou em {Values.TypeName(value)} (em \"{Text.Trim()}\")", Location, Field);
    }

    public bool EvaluateBool(IEvaluationContext context, string what = "a condição")
    {
        var value = Evaluate(context);
        return value is bool b
            ? b
            : throw new EvaluationException($"{what} deve resultar em verdadeiro/falso, mas resultou em {Values.TypeName(value)} (em \"{Text.Trim()}\")", Location, Field);
    }

    /// <summary>Posição no arquivo de um deslocamento dentro do texto.</summary>
    public SourceLocation LocationAt(int position) => LocationOf(Location, Text, ColumnShift, position);

    public override string ToString() => Text;

    private static SourceLocation LocationOf(SourceLocation start, string text, int columnShift, int position)
    {
        if (!start.IsKnown)
        {
            return start;
        }

        position = Math.Clamp(position, 0, text.Length);
        var lastNewLine = position == 0 ? -1 : text.LastIndexOf('\n', position - 1, position);
        if (lastNewLine < 0)
        {
            return start with { Column = start.Column + columnShift + position };
        }

        var lines = text.AsSpan(0, position).Count('\n');
        return start with { Line = start.Line + lines, Column = position - lastNewLine };
    }

    private static Dictionary<string, int> CollectNames(Node root)
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        void Walk(Node node)
        {
            switch (node)
            {
                case IdentifierNode id:
                    names.TryAdd(id.Name, id.Position);
                    break;
                case ListNode l:
                    l.Items.ToList().ForEach(Walk);
                    break;
                case MemberNode m:
                    Walk(m.Target);
                    break;
                case IndexNode ix:
                    Walk(ix.Target);
                    Walk(ix.Index);
                    break;
                case UnaryNode u:
                    Walk(u.Operand);
                    break;
                case BinaryNode b:
                    Walk(b.Left);
                    Walk(b.Right);
                    break;
                case CallNode c:
                    c.Arguments.ToList().ForEach(a => Walk(a.Value));
                    break;
            }
        }

        Walk(root);
        return names;
    }
}
