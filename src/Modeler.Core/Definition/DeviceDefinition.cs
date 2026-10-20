using Modeler.Core.Flow;
using Modeler.Core.Results;
using Modeler.Core.Yaml;

namespace Modeler.Core.Definition;

/// <summary>Definição paramétrica de um dispositivo, lida de um arquivo YAML.</summary>
public sealed class DeviceDefinition
{
    internal DeviceDefinition()
    {
    }

    /// <summary>Identificador estável (campo <c>dispositivo</c>), gravado na entidade na Fase 2.</summary>
    public string Id { get; internal set; } = "";

    public string? Name { get; internal set; }

    public string Version { get; internal set; } = "";

    public DeviceCategory Category { get; internal set; }

    public string? Description { get; internal set; }

    public IReadOnlyList<ParameterDef> Parameters { get; internal set; } = Array.Empty<ParameterDef>();

    /// <summary>Variáveis já em ordem de dependência (cada uma depois das que usa).</summary>
    public IReadOnlyList<VariableDef> Variables { get; internal set; } = Array.Empty<VariableDef>();

    public IReadOnlyList<ValidationDef> Validations { get; internal set; } = Array.Empty<ValidationDef>();

    public IReadOnlyList<Statement> Flow { get; internal set; } = Array.Empty<Statement>();

    public OutputsDef Outputs { get; internal set; } = new();

    public string? SourceFile { get; internal set; }

    /// <summary>SHA-256 do texto da definição: permite detectar, na Fase 2, que a definição mudou
    /// desde que a entidade foi gerada.</summary>
    public string Hash { get; internal set; } = "";

    public static DeviceDefinition Load(string path) =>
        DefinitionParser.Parse(File.ReadAllText(path), path);

    /// <param name="sourceName">Nome mostrado nas mensagens de erro.</param>
    public static DeviceDefinition Parse(string yaml, string? sourceName = null) =>
        DefinitionParser.Parse(yaml, sourceName);

    public ParameterDef? FindParameter(string name) => Parameters.FirstOrDefault(p => p.Name == name);

    /// <summary>Avalia uma instância. Parâmetros omitidos usam o padrão. Lança
    /// <see cref="Errors.ParameterValidationException"/> ou <see cref="Errors.EvaluationException"/>.</summary>
    public DeviceInstance Evaluate(IReadOnlyDictionary<string, object>? values = null) =>
        DeviceEvaluator.Evaluate(this, values);
}
