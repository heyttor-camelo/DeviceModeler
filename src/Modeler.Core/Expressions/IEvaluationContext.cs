namespace Modeler.Core.Expressions;

/// <summary>Resolve nomes e funções durante a avaliação.</summary>
public interface IEvaluationContext
{
    FunctionRegistry Functions { get; }

    /// <summary>Objeto do interpretador do fluxo (recebe <c>expor_solido</c>/<c>conector</c>);
    /// nulo fora do fluxo.</summary>
    object? Host => null;

    /// <summary>Nome do último parâmetro editado (paleta ou grip), para <c>editado()</c>; nulo se nenhum.</summary>
    string? Edited => null;

    bool TryGetValue(string name, out object value);
}

/// <summary>Contexto simples baseado em dicionário.</summary>
public sealed class DictionaryContext : IEvaluationContext
{
    private readonly IReadOnlyDictionary<string, object> _values;

    public DictionaryContext(IReadOnlyDictionary<string, object>? values = null, FunctionRegistry? functions = null)
    {
        _values = values ?? new Dictionary<string, object>();
        Functions = functions ?? FunctionRegistry.Default;
    }

    public FunctionRegistry Functions { get; }

    public bool TryGetValue(string name, out object value) => _values.TryGetValue(name, out value!);
}
