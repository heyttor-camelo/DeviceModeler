using Modeler.Core.Csg;

namespace Modeler.Core.Expressions;

public delegate object FunctionImplementation(FunctionCall call);

/// <summary>Assinatura de uma função: nomes dos parâmetros (para argumentos nomeados),
/// quantos são obrigatórios e se aceita número variável de argumentos.</summary>
public sealed class FunctionDef
{
    public FunctionDef(string name, string[] parameters, int required, FunctionImplementation implementation, bool variadic = false, string? description = null)
    {
        Name = name;
        Parameters = parameters;
        Required = required;
        Implementation = implementation;
        Variadic = variadic;
        Description = description ?? "";
    }

    public string Name { get; }

    public IReadOnlyList<string> Parameters { get; }

    public int Required { get; }

    public bool Variadic { get; }

    public FunctionImplementation Implementation { get; }

    public string Description { get; }

    public string Signature => Variadic
        ? $"{Name}({string.Join(", ", Parameters)}, ...)"
        : $"{Name}({string.Join(", ", Parameters.Select((p, i) => i < Required ? p : p + "?"))})";

    /// <summary>Associa os argumentos da chamada aos parâmetros. Entradas nulas = opcional omitido.</summary>
    internal Node?[] Bind(CallNode call)
    {
        if (Variadic)
        {
            if (call.Arguments.Any(a => a.Name is not null))
            {
                throw new ExpressionException($"{Name}() não aceita argumentos nomeados", call.Position);
            }

            if (call.Arguments.Count < Required)
            {
                throw new ExpressionException($"{Name}() exige ao menos {Required} argumento(s): {Signature}", call.Position);
            }

            return call.Arguments.Select(a => (Node?)a.Value).ToArray();
        }

        var bound = new Node?[Parameters.Count];
        var positional = 0;
        foreach (var arg in call.Arguments)
        {
            int slot;
            if (arg.Name is null)
            {
                slot = positional++;
                if (slot >= Parameters.Count)
                {
                    throw new ExpressionException($"{Name}() recebeu argumentos demais: {Signature}", arg.Value.Position);
                }
            }
            else
            {
                slot = IndexOf(arg.Name);
                if (slot < 0)
                {
                    throw new ExpressionException($"{Name}() não tem parâmetro '{arg.Name}': {Signature}", arg.Value.Position);
                }

                if (bound[slot] is not null)
                {
                    throw new ExpressionException($"{Name}(): parâmetro '{arg.Name}' informado duas vezes", arg.Value.Position);
                }
            }

            bound[slot] = arg.Value;
        }

        for (var i = 0; i < Required; i++)
        {
            if (bound[i] is null)
            {
                throw new ExpressionException($"{Name}(): falta o argumento '{Parameters[i]}': {Signature}", call.Position);
            }
        }

        return bound;
    }

    private int IndexOf(string parameter)
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i] == parameter)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Argumentos já avaliados de uma chamada, com conversões que geram mensagens claras.</summary>
public sealed class FunctionCall
{
    private readonly Node?[] _nodes;

    internal FunctionCall(FunctionDef function, object?[] args, Node?[] nodes, IEvaluationContext context, int position)
    {
        Function = function;
        Arguments = args;
        _nodes = nodes;
        Context = context;
        Position = position;
    }

    public FunctionDef Function { get; }

    /// <summary>Valores na ordem dos parâmetros; nulo = opcional omitido.</summary>
    public IReadOnlyList<object?> Arguments { get; }

    public IEvaluationContext Context { get; }

    public int Position { get; }

    public int Count => Arguments.Count;

    public bool Has(int index) => index < Arguments.Count && Arguments[index] is not null;

    public object Value(int index) => Arguments[index] ?? throw Error($"falta o argumento '{ParameterName(index)}'", index);

    public double Number(int index) => Value(index) is double d ? d : throw TypeError(index, "número");

    public double Number(int index, double fallback) => Has(index) ? Number(index) : fallback;

    public double Positive(int index)
    {
        var value = Number(index);
        return value > 0 ? value : throw Error($"'{ParameterName(index)}' deve ser maior que zero (recebido {Values.FormatNumber(value)})", index);
    }

    public int Integer(int index) => Evaluator.ToInteger(Value(index), PositionOf(index), $"{Function.Name}: '{ParameterName(index)}'");

    public string Text(int index) => Value(index) is string s ? s : throw TypeError(index, "texto");

    public string Text(int index, string fallback) => Has(index) ? Text(index) : fallback;

    public CsgNode Solid(int index) => Value(index) is CsgNode n ? n : throw TypeError(index, "sólido");

    public IReadOnlyList<object> List(int index) => Value(index) is IReadOnlyList<object> l ? l : throw TypeError(index, "lista");

    public Vec3 Vector(int index) =>
        Values.TryToVec3(Value(index), out var v) ? v : throw TypeError(index, "ponto/vetor [x, y, z]");

    public Vec3 Vector(int index, Vec3 fallback) => Has(index) ? Vector(index) : fallback;

    /// <summary>Eixo: "x", "y", "z" (aceita "-x" etc.) ou vetor [dx, dy, dz]; devolve unitário.</summary>
    public Vec3 Axis(int index, Vec3 fallback)
    {
        if (!Has(index))
        {
            return fallback;
        }

        if (Value(index) is string s)
        {
            return s.Trim().ToLowerInvariant() switch
            {
                "x" or "+x" => Vec3.UnitX,
                "y" or "+y" => Vec3.UnitY,
                "z" or "+z" => Vec3.UnitZ,
                "-x" => Vec3.UnitX * -1,
                "-y" => Vec3.UnitY * -1,
                "-z" => Vec3.UnitZ * -1,
                _ => throw Error($"eixo '{s}' inválido; use \"x\", \"y\", \"z\" ou [dx, dy, dz]", index),
            };
        }

        var vector = Vector(index);
        return vector.Length < 1e-12 ? throw Error("eixo não pode ser o vetor nulo", index) : vector.Normalized();
    }

    public ExpressionException Error(string message, int? argumentIndex = null) =>
        new($"{Function.Name}: {message}", argumentIndex is { } i ? PositionOf(i) : Position);

    private ExpressionException TypeError(int index, string expected) =>
        Error($"'{ParameterName(index)}' deve ser {expected}, mas é {Values.TypeName(Arguments[index])}", index);

    private string ParameterName(int index) =>
        Function.Variadic ? $"argumento {index + 1}" : Function.Parameters[index];

    private int PositionOf(int index) => index < _nodes.Length && _nodes[index] is { } node ? node.Position : Position;
}

/// <summary>Tabela de funções disponíveis nas expressões.</summary>
public sealed class FunctionRegistry
{
    private readonly Dictionary<string, FunctionDef> _functions = new(StringComparer.Ordinal);

    /// <summary>Matemática, listas, geometria CSG e saídas do fluxo.</summary>
    public static FunctionRegistry Default { get; } = CreateDefault();

    public IEnumerable<FunctionDef> All => _functions.Values.Distinct();

    /// <summary>Nomes e apelidos registrados.</summary>
    public IEnumerable<string> Names => _functions.Keys;

    public static FunctionRegistry CreateDefault()
    {
        var registry = new FunctionRegistry();
        BuiltinFunctions.RegisterMath(registry);
        BuiltinFunctions.RegisterGeometry(registry);
        return registry;
    }

    public FunctionRegistry Register(FunctionDef function, params string[] aliases)
    {
        _functions[function.Name] = function;
        foreach (var alias in aliases)
        {
            _functions[alias] = function;
        }

        return this;
    }

    public bool TryGet(string name, out FunctionDef function) => _functions.TryGetValue(name, out function!);

    public bool Contains(string name) => _functions.ContainsKey(name) || name is "se" or "if";
}
