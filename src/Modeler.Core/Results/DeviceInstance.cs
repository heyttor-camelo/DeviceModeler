using Modeler.Core.Definition;

namespace Modeler.Core.Results;

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
        IReadOnlyList<string> warnings)
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

    public double Variable(string name) => (double)Variables[name];
}
