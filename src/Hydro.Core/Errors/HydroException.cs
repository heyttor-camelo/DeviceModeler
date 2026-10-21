namespace Hydro.Core.Errors;

/// <summary>Posição em um arquivo de definição (linha e coluna começam em 1).</summary>
public sealed record SourceLocation(string? File, int Line, int Column)
{
    public static readonly SourceLocation None = new(null, 0, 0);

    public bool IsKnown => Line > 0;

    public override string ToString() =>
        IsKnown ? $"{File ?? "<texto>"}({Line},{Column})" : File ?? "";
}

/// <summary>Base de todos os erros do modelador. A mensagem já vem formatada como
/// <c>arquivo(linha,coluna): campo: motivo</c>.</summary>
public class HydroException : Exception
{
    public HydroException(string reason, SourceLocation? location, string? field, Exception? inner = null)
        : base(Format(reason, location, field), inner)
    {
        Reason = reason;
        Location = location ?? SourceLocation.None;
        Field = field;
    }

    /// <summary>Motivo sem o prefixo de localização.</summary>
    public string Reason { get; }

    public SourceLocation Location { get; }

    /// <summary>Caminho do campo no YAML, por exemplo <c>parametros.L.max</c>.</summary>
    public string? Field { get; }

    public int Line => Location.Line;

    public int Column => Location.Column;

    private static string Format(string reason, SourceLocation? location, string? field)
    {
        var prefix = location is { IsKnown: true } || !string.IsNullOrEmpty(location?.File) ? location + ": " : "";
        var fieldPart = string.IsNullOrEmpty(field) ? "" : field + ": ";
        return prefix + fieldPart + reason;
    }
}

/// <summary>Erro ao ler/validar a definição (YAML, expressões, dependências).</summary>
public class DefinitionException : HydroException
{
    public DefinitionException(string reason, SourceLocation? location, string? field, Exception? inner = null)
        : base(reason, location, field, inner)
    {
    }
}

/// <summary>Variáveis que dependem umas das outras em ciclo.</summary>
public sealed class CircularDependencyException : DefinitionException
{
    public CircularDependencyException(IReadOnlyList<string> cycle, SourceLocation? location, string? field)
        : base("dependência circular: " + string.Join(" -> ", cycle), location, field)
    {
        Cycle = cycle;
    }

    public IReadOnlyList<string> Cycle { get; }
}

/// <summary>Erro durante a avaliação de uma instância (fluxo, saídas, validações).</summary>
public class EvaluationException : HydroException
{
    public EvaluationException(string reason, SourceLocation? location, string? field, Exception? inner = null)
        : base(reason, location, field, inner)
    {
    }
}

/// <summary>Valor de parâmetro inválido (tipo, fora de min/max ou fora da lista).</summary>
public sealed class ParameterValidationException : HydroException
{
    public ParameterValidationException(string parameter, string reason)
        : base(reason, null, "parametro " + parameter)
    {
        Parameter = parameter;
    }

    public string Parameter { get; }
}
