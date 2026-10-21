using Hydro.Core.Expressions;
using Hydro.Core.Results;

namespace Hydro.Core.Flow;

/// <summary>Recebe as saídas publicadas durante o fluxo.</summary>
internal interface IOutputSink
{
    void AddSolid(NamedSolid solid, FunctionCall call);

    void AddConnector(Connector connector, FunctionCall call);
}

/// <summary>Escopo do fluxo: nomes globais (parâmetros + variáveis, somente leitura) e locais.</summary>
internal sealed class FlowScope : IEvaluationContext, IOutputSink
{
    private readonly IReadOnlyDictionary<string, object> _globals;
    private readonly Dictionary<string, object> _locals = new(StringComparer.Ordinal);

    public FlowScope(IReadOnlyDictionary<string, object> globals, FunctionRegistry functions, string? edited = null)
    {
        _globals = globals;
        Functions = functions;
        Edited = edited;
    }

    public FunctionRegistry Functions { get; }

    public object? Host => this;

    public string? Edited { get; }

    public List<NamedSolid> Solids { get; } = new();

    public List<Connector> Connectors { get; } = new();

    /// <summary>Linhas de <c>mensagem:</c>.</summary>
    public List<string> Log { get; } = new();

    public IReadOnlyDictionary<string, object> Locals => _locals;

    public bool TryGetValue(string name, out object value) =>
        _locals.TryGetValue(name, out value!) || _globals.TryGetValue(name, out value!);

    public void Set(string name, object value) => _locals[name] = value;

    public bool TryGetLocal(string name, out object value) => _locals.TryGetValue(name, out value!);

    public void Remove(string name) => _locals.Remove(name);

    public void AddSolid(NamedSolid solid, FunctionCall call)
    {
        if (Solids.Any(s => s.Name == solid.Name))
        {
            throw call.Error($"sólido '{solid.Name}' já foi exposto");
        }

        Solids.Add(solid);
    }

    public void AddConnector(Connector connector, FunctionCall call)
    {
        if (Connectors.Any(c => c.Name == connector.Name))
        {
            throw call.Error($"conector '{connector.Name}' já existe");
        }

        Connectors.Add(connector);
    }
}

/// <summary>Contexto de avaliação das variáveis e validações (sem saídas).</summary>
internal sealed class GlobalScope : IEvaluationContext
{
    private readonly Dictionary<string, object> _values;

    public GlobalScope(Dictionary<string, object> values, FunctionRegistry functions, string? edited = null)
    {
        _values = values;
        Functions = functions;
        Edited = edited;
    }

    public FunctionRegistry Functions { get; }

    public string? Edited { get; }

    public bool TryGetValue(string name, out object value) => _values.TryGetValue(name, out value!);
}
