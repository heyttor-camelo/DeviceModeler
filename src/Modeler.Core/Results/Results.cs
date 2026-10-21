using Modeler.Core.Csg;

namespace Modeler.Core.Results;

/// <summary>Sólido de saída. Na Fase 2 cada nome vira um componente de estilo (camada/material).</summary>
public sealed record NamedSolid(string Name, CsgNode Node, string? Material) : INamed;

/// <summary>Ponto de ligação de tubulação: posição no sistema local, direção unitária apontando
/// para fora do dispositivo e diâmetro (m). <paramref name="Filter"/> restringe que tubos podem ligar.</summary>
public sealed record Connector(string Name, Vec3 Position, Vec3 Direction, double Diameter, string? Filter) : INamed;

/// <summary>Nó hidráulico exportável para SWMM/EPANET.</summary>
public abstract record HydraulicNode;

/// <summary>[JUNCTIONS] do SWMM: cota de fundo (Invert Elev.), profundidade máxima, inicial,
/// de sobrecarga e área de alagamento (m²).</summary>
public sealed record SwmmJunction(
    double InvertElevation,
    double MaxDepth,
    double InitialDepth = 0,
    double SurchargeDepth = 0,
    double PondedArea = 0) : HydraulicNode;

/// <summary>Quantitativo nomeado (m³, m², m, un...).</summary>
public sealed record Quantity(string Name, double Value, string Unit, string? Description) : INamed;

/// <summary>Grip de edição: arrastar a partir de <paramref name="Position"/> ao longo de
/// <paramref name="Direction"/> altera <paramref name="Parameter"/> em deslocamento × <paramref name="Factor"/>.</summary>
public sealed record GripInstance(string Parameter, Vec3 Position, Vec3 Direction, double Factor)
{
    public Definition.GripKind Kind { get; init; } = Definition.GripKind.Esticar;

    /// <summary>Centro da rotação (grip de rotação); igual à posição nos outros.</summary>
    public Vec3 Center { get; init; }

    /// <summary>Grip de perfil: para cima aumenta o valor.</summary>
    public bool Up { get; init; } = true;
}

/// <summary>Conexão de tubo informada ao avaliar (na Fase 2 vem dos tubos ligados).
/// Ângulo em graus (planta, a partir de +X local, anti-horário) apontando do dispositivo para o tubo;
/// cota = geratriz inferior interna (invert); diâmetro interno; diâmetro externo opcional (padrão = interno);
/// <paramref name="Reference"/> identifica o objeto de origem (handle do tubo no DWG).</summary>
public sealed record ConnectionInput(string Name, double Angle, double InvertElevation, double Diameter, double? OuterDiameter = null, string? Reference = null);

public interface INamed
{
    string Name { get; }
}

/// <summary>Lista somente leitura com acesso por nome (<c>inst.Quantities["concreto_m3"]</c>).</summary>
public sealed class NamedList<T> : IReadOnlyList<T>
    where T : INamed
{
    private readonly List<T> _items;

    public NamedList(IEnumerable<T> items)
    {
        _items = items.ToList();
    }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public T this[string name] =>
        Find(name) ?? throw new KeyNotFoundException($"'{name}' não existe; disponíveis: {string.Join(", ", _items.Select(i => i.Name))}");

    public T? Find(string name) => _items.FirstOrDefault(i => i.Name == name);

    public bool Contains(string name) => Find(name) is not null;

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
