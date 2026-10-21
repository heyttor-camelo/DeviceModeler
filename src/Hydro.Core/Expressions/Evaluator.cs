using Hydro.Core.Csg;

namespace Hydro.Core.Expressions;

/// <summary>Avalia a árvore sintática. Erros saem como <see cref="ExpressionException"/> com a
/// posição do nó que falhou.</summary>
public static class Evaluator
{
    /// <summary>Constantes sempre disponíveis.</summary>
    public static readonly IReadOnlyDictionary<string, object> Constants = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["pi"] = Math.PI,
    };

    public static object Evaluate(Node node, IEvaluationContext context) => node switch
    {
        NumberNode n => n.Value,
        StringNode s => s.Value,
        BoolNode b => b.Value,
        NullNode => NullValue.Instance,
        ListNode l => l.Items.Select(i => Evaluate(i, context)).ToList(),
        IdentifierNode id => Lookup(id, context),
        MemberNode m => Member(m, context),
        IndexNode ix => Index(ix, context),
        UnaryNode u => Unary(u, context),
        BinaryNode b => Binary(b, context),
        CallNode c => Call(c, context),
        _ => throw new ExpressionException($"nó desconhecido {node.GetType().Name}", node.Position),
    };

    public static bool EvaluateBool(Node node, IEvaluationContext context, string what)
    {
        var value = Evaluate(node, context);
        return value is bool b
            ? b
            : throw new ExpressionException($"{what} deve ser lógica (verdadeiro/falso), mas é {Values.TypeName(value)}", node.Position);
    }

    private static object Lookup(IdentifierNode id, IEvaluationContext context)
    {
        if (context.TryGetValue(id.Name, out var value))
        {
            return value;
        }

        if (Constants.TryGetValue(id.Name, out var constant))
        {
            return constant;
        }

        throw new ExpressionException($"nome desconhecido '{id.Name}'", id.Position);
    }

    private static object Member(MemberNode node, IEvaluationContext context)
    {
        var target = Evaluate(node.Target, context);
        switch (target)
        {
            case IReadOnlyDictionary<string, object> record:
                return record.TryGetValue(node.Member, out var value)
                    ? value
                    : throw new ExpressionException(
                        $"campo '{node.Member}' não existe; campos disponíveis: {string.Join(", ", record.Keys)}", node.Position, missing: true);
            case NullValue:
                throw new ExpressionException($"'.{node.Member}' de um valor nulo (use existe() ou ou())", node.Position, missing: true);
            case IReadOnlyList<object> { Count: 3 } point when node.Member is "x" or "y" or "z":
                return point[node.Member[0] - 'x'];
            default:
                throw new ExpressionException($"'.{node.Member}' não se aplica a {Values.TypeName(target)}", node.Position);
        }
    }

    private static object Index(IndexNode node, IEvaluationContext context)
    {
        var target = Evaluate(node.Target, context);
        if (target is not IReadOnlyList<object> list)
        {
            throw new ExpressionException($"índice só se aplica a listas, não a {Values.TypeName(target)}", node.Position);
        }

        var index = ToInteger(Evaluate(node.Index, context), node.Index.Position, "índice");
        if (index < 0 || index >= list.Count)
        {
            throw new ExpressionException($"índice {index} fora da lista (tamanho {list.Count})", node.Index.Position, missing: true);
        }

        return list[index];
    }

    private static object Unary(UnaryNode node, IEvaluationContext context)
    {
        if (node.Operator == "not")
        {
            return !EvaluateBool(node.Operand, context, "a operação 'nao'");
        }

        var value = Number(Evaluate(node.Operand, context), node.Operand.Position, $"operando de '{node.Operator}'");
        return node.Operator == "-" ? -value : value;
    }

    private static object Binary(BinaryNode node, IEvaluationContext context)
    {
        switch (node.Operator)
        {
            case "and":
                return EvaluateBool(node.Left, context, "o operando de 'e'") && EvaluateBool(node.Right, context, "o operando de 'e'");
            case "or":
                return EvaluateBool(node.Left, context, "o operando de 'ou'") || EvaluateBool(node.Right, context, "o operando de 'ou'");
        }

        var left = Evaluate(node.Left, context);
        var right = Evaluate(node.Right, context);

        switch (node.Operator)
        {
            case "==":
                return AreEqual(left, right, node);
            case "!=":
                return !AreEqual(left, right, node);
            case "+" when left is string || right is string:
                return Values.Format(left) + Values.Format(right);
        }

        // pontos e vetores [x, y, z]: soma, diferença e escala
        if (Values.TryToVec3(left, out var va) && Values.TryToVec3(right, out var vb) && node.Operator is "+" or "-")
        {
            return Values.FromVec3(node.Operator == "+" ? va + vb : va - vb);
        }

        if (node.Operator is "*" or "/" && (Values.TryToVec3(left, out var vector) ? right is double : Values.TryToVec3(right, out vector) && left is double && node.Operator == "*"))
        {
            var k = left as double? ?? (double)right;
            if (node.Operator == "/" && k == 0)
            {
                throw new ExpressionException("divisão por zero", node.Position);
            }

            return Values.FromVec3(vector * (node.Operator == "*" ? k : 1 / k));
        }

        var a = Number(left, node.Left.Position, $"operando de '{node.Operator}'");
        var b = Number(right, node.Right.Position, $"operando de '{node.Operator}'");
        return node.Operator switch
        {
            "<" => a < b,
            "<=" => a < b || Values.NearlyEqual(a, b),
            ">" => a > b,
            ">=" => a > b || Values.NearlyEqual(a, b),
            "+" => Finite(a + b, node),
            "-" => Finite(a - b, node),
            "*" => Finite(a * b, node),
            "/" => b == 0 ? throw new ExpressionException("divisão por zero", node.Position) : Finite(a / b, node),
            "%" => b == 0 ? throw new ExpressionException("resto de divisão por zero", node.Position) : Finite(a % b, node),
            "^" => Finite(Math.Pow(a, b), node),
            _ => throw new ExpressionException($"operador desconhecido '{node.Operator}'", node.Position),
        };
    }

    private static bool AreEqual(object left, object right, BinaryNode node) => (left, right) switch
    {
        (NullValue, NullValue) => true,
        (NullValue, _) or (_, NullValue) => false,
        (double a, double b) => Values.NearlyEqual(a, b),
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (bool a, bool b) => a == b,
        _ => throw new ExpressionException(
            $"não é possível comparar {Values.TypeName(left)} com {Values.TypeName(right)}", node.Position),
    };

    private static object Call(CallNode node, IEvaluationContext context)
    {
        // se(cond, a, b) é preguiçosa: só avalia o ramo escolhido (permite se(x > 0, 1/x, 0)).
        if (node.Name is "se" or "if")
        {
            if (node.Arguments.Count != 3 || node.Arguments.Any(a => a.Name is not null))
            {
                throw new ExpressionException("se(condição, valor_se_verdadeiro, valor_se_falso) exige 3 argumentos posicionais", node.Position);
            }

            var condition = EvaluateBool(node.Arguments[0].Value, context, "a condição de se()");
            return Evaluate(node.Arguments[condition ? 1 : 2].Value, context);
        }

        // existe(x) e ou(x, padrao) também são preguiçosas: só absorvem a falta de valor
        // (nulo, campo ausente, índice fora da lista); qualquer outro erro continua sendo erro.
        if (node.Name is "existe" or "exists")
        {
            if (node.Arguments.Count != 1 || node.Arguments[0].Name is not null)
            {
                throw new ExpressionException("existe(valor) exige 1 argumento posicional", node.Position);
            }

            return TryValue(node.Arguments[0].Value, context, out _);
        }

        if (node.Name is "ou" or "coalesce")
        {
            if (node.Arguments.Count < 2 || node.Arguments.Any(a => a.Name is not null))
            {
                throw new ExpressionException("ou(valor, alternativa, ...) exige ao menos 2 argumentos posicionais", node.Position);
            }

            for (var i = 0; i < node.Arguments.Count - 1; i++)
            {
                if (TryValue(node.Arguments[i].Value, context, out var value))
                {
                    return value;
                }
            }

            return Evaluate(node.Arguments[^1].Value, context);
        }

        if (!context.Functions.TryGet(node.Name, out var function))
        {
            throw new ExpressionException($"função desconhecida '{node.Name}'", node.Position);
        }

        var args = function.Bind(node);
        var values = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            values[i] = args[i] is null ? null : Evaluate(args[i]!, context);
        }

        return function.Implementation(new FunctionCall(function, values, args, context, node.Position));
    }

    private static bool TryValue(Node node, IEvaluationContext context, out object value)
    {
        try
        {
            value = Evaluate(node, context);
            return value is not NullValue;
        }
        catch (ExpressionException ex) when (ex.Missing)
        {
            value = NullValue.Instance;
            return false;
        }
    }

    internal static double Number(object value, int position, string what) =>
        value is double d ? d : throw new ExpressionException($"{what} deve ser número, mas é {Values.TypeName(value)}", position);

    internal static int ToInteger(object value, int position, string what)
    {
        var d = Number(value, position, what);
        var rounded = Math.Round(d);
        if (!Values.NearlyEqual(d, rounded) || Math.Abs(rounded) > int.MaxValue)
        {
            throw new ExpressionException($"{what} deve ser inteiro, mas é {Values.FormatNumber(d)}", position);
        }

        return (int)rounded;
    }

    private static double Finite(double value, Node node) =>
        double.IsFinite(value) ? value : throw new ExpressionException("resultado não é um número finito", node.Position);
}
