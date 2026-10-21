using Hydro.Core.Definition;
using Hydro.Core.Expressions;
using Hydro.Core.Text;

namespace Hydro.Core.Results;

/// <summary>Resultado de <see cref="DeviceDefinition.Evaluate"/>. Tudo o que a Fase 2 precisa:
/// os parâmetros resolvidos (para gravar no Xrecord junto com id/versão/hash da definição),
/// a árvore CSG de cada sólido, conectores, nós hidráulicos, quantitativos e grips.</summary>
public sealed class DeviceInstance
{
    internal DeviceInstance(
        DeviceDefinition definition,
        IReadOnlyDictionary<string, object> parameters,
        IReadOnlyDictionary<string, object> variables,
        NamedList<NamedSolid> solids,
        NamedList<Connector> connectors,
        IReadOnlyList<HydraulicNode> hydraulics,
        NamedList<Quantity> quantities,
        IReadOnlyList<GripInstance> grips,
        IReadOnlyList<string> warnings,
        string? code = null,
        IReadOnlyList<string>? log = null)
    {
        Definition = definition;
        Parameters = parameters;
        Variables = variables;
        Solids = solids;
        Connectors = connectors;
        Hydraulics = hydraulics;
        Quantities = quantities;
        Grips = grips;
        Warnings = warnings;
        Code = code;
        Log = log ?? Array.Empty<string>();
    }

    public DeviceDefinition Definition { get; }

    /// <summary>Valores finais de todos os parâmetros (informados ou padrão), já validados.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>Valores calculados das variáveis.</summary>
    public IReadOnlyDictionary<string, object> Variables { get; }

    public NamedList<NamedSolid> Solids { get; }

    public NamedList<Connector> Connectors { get; }

    public IReadOnlyList<HydraulicNode> Hydraulics { get; }

    public NamedList<Quantity> Quantities { get; }

    public IReadOnlyList<GripInstance> Grips { get; }

    /// <summary>Mensagens de validações com <c>nivel: aviso</c> que falharam.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Código da instância (campo <c>codigo</c> da definição); nulo se a definição não tem.</summary>
    public string? Code { get; }

    /// <summary>Linhas do <c>mensagem:</c> do fluxo (informação, não aviso).</summary>
    public IReadOnlyList<string> Log { get; }

    public double Variable(string name) => (double)Variables[name];

    /// <summary>Nomes que os modelos de texto enxergam além de parâmetros, variáveis e quantitativos.</summary>
    public static readonly IReadOnlyList<string> TemplateNames = new[] { "codigo", "nome", "numero", "familia", "subtipo", "dispositivo" };

    /// <summary>Avalia um modelo de texto com parâmetros, variáveis, quantitativos e os nomes de
    /// <see cref="TemplateNames"/> (<paramref name="extra"/> traz <c>nome</c> e <c>numero</c>, que
    /// só o desenho conhece).</summary>
    public string Render(Template template, IReadOnlyDictionary<string, object>? extra = null)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["codigo"] = Code ?? "",
            ["nome"] = "",
            ["numero"] = 0.0,
            ["familia"] = Definition.Family ?? "",
            ["subtipo"] = Definition.Subtype ?? "",
            ["dispositivo"] = Definition.Name ?? Definition.Id,
        };
        if (extra is not null)
        {
            foreach (var (name, value) in extra)
            {
                values[name] = Values.Normalize(value)!;
            }
        }

        foreach (var q in Quantities)
        {
            values[q.Name] = q.Value;
        }

        foreach (var (name, value) in Variables)
        {
            values[name] = value;
        }

        foreach (var (name, value) in Parameters)
        {
            values[name] = value;
        }

        return template.Render(new DictionaryContext(values));
    }
}
