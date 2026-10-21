using Modeler.Core.Csg;
using Modeler.Core.Results;

namespace Modeler.Core.Network;

/// <summary>Como escolher o conector da ponta de um tubo (os seis modos do SOLIDOS = três escolhas ×
/// duas regras de elevação).</summary>
public enum ConnectorChoice
{
    /// <summary>O conector já ligado (pelo nome); se ele não existir mais, o mais próximo.</summary>
    Fixo,

    /// <summary>O conector mais próximo da ponta atual.</summary>
    AjustarPosicao,

    /// <summary>O que deixa o tubo mais curto em 3D (medido a partir da outra ponta).</summary>
    Melhor3D,

    /// <summary>O que deixa o tubo mais curto em planta.</summary>
    Melhor2D,
}

/// <summary>Regra de elevação da ponta.</summary>
public enum EndElevation
{
    /// <summary>A ponta vai para a elevação do conector.</summary>
    Corrigir,

    /// <summary>A ponta só anda em planta: a elevação do tubo fica.</summary>
    Manter,
}

/// <summary>Resultado: o conector escolhido e a nova posição da ponta.</summary>
public sealed record ConnectorMatchResult(Connector Connector, Vec3 End, double Distance);

/// <summary>Ligação de pontas de tubo a conectores (técnica 11 do estudo E e estudo C §1.3). O filtro
/// do conector é uma lista de tipos separados por vírgula (<c>tubo</c>, <c>valeta</c>,
/// <c>descida,escada</c>), sem diferenciar maiúsculas; sem filtro, aceita qualquer tipo. A direção do
/// conector aponta para fora do dispositivo: o tubo precisa sair por ela (ângulo até
/// <see cref="MaxAngle"/>).</summary>
public static class ConnectorMatch
{
    /// <summary>Maior ângulo entre a direção do conector e a do tubo, em graus (90 = só não pode
    /// entrar pelo lado de dentro).</summary>
    public const double MaxAngle = 90;

    public static bool Accepts(Connector connector, string? type)
    {
        if (string.IsNullOrWhiteSpace(connector.Filter) || string.IsNullOrWhiteSpace(type))
        {
            return true;
        }

        return connector.Filter.Split(',').Select(t => t.Trim()).Any(t => string.Equals(t, type.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Escolhe o conector (em coordenadas do desenho) para a ponta <paramref name="end"/> de um
    /// tubo cuja outra ponta é <paramref name="otherEnd"/>. Nulo se nenhum conector aceitar o tipo e a
    /// direção.</summary>
    /// <param name="current">nome do conector já ligado (modo <see cref="ConnectorChoice.Fixo"/>).</param>
    public static ConnectorMatchResult? Choose(IReadOnlyList<Connector> connectors, Vec3 end, Vec3 otherEnd, string? type,
        ConnectorChoice choice, EndElevation elevation, string? current = null)
    {
        var candidates = connectors.Where(c => Accepts(c, type) && Faces(c, otherEnd)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        Connector chosen;
        if (choice == ConnectorChoice.Fixo && candidates.FirstOrDefault(c => c.Name == current) is { } same)
        {
            chosen = same;
        }
        else
        {
            chosen = choice switch
            {
                ConnectorChoice.Melhor3D => candidates.MinBy(c => (c.Position - otherEnd).Length)!,
                ConnectorChoice.Melhor2D => candidates.MinBy(c => Plan(c.Position - otherEnd))!,
                _ => candidates.MinBy(c => (c.Position - end).Length)!,
            };
        }

        var target = elevation == EndElevation.Corrigir ? chosen.Position : chosen.Position with { Z = end.Z };
        return new ConnectorMatchResult(chosen, target, (target - end).Length);
    }

    /// <summary>O tubo que vai do conector até <paramref name="otherEnd"/> sai pela face do conector
    /// (direção dentro de <see cref="MaxAngle"/>). Conector com direção nula aceita qualquer lado.</summary>
    public static bool Faces(Connector connector, Vec3 otherEnd)
    {
        var toPipe = otherEnd - connector.Position;
        if (toPipe.Length < 1e-9 || connector.Direction.Length < 1e-9)
        {
            return true;
        }

        var cos = Affine.Dot(toPipe.Normalized(), connector.Direction.Normalized());
        return cos >= Math.Cos(MaxAngle * Math.PI / 180) - 1e-9;
    }

    private static double Plan(Vec3 v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y));

    /// <summary>Lê o modo gravado como texto (<c>ajustar_corrigir</c>, <c>melhor3d_manter</c>...).</summary>
    public static (ConnectorChoice Choice, EndElevation Elevation) ParseMode(string? text)
    {
        var parts = (text ?? "").ToLowerInvariant().Split('_', ' ', '/');
        var choice = parts.FirstOrDefault() switch
        {
            "fixo" => ConnectorChoice.Fixo,
            "melhor3d" or "melhor" => ConnectorChoice.Melhor3D,
            "melhor2d" => ConnectorChoice.Melhor2D,
            _ => ConnectorChoice.AjustarPosicao,
        };
        var elevation = parts.Skip(1).FirstOrDefault() == "manter" ? EndElevation.Manter : EndElevation.Corrigir;
        return (choice, elevation);
    }

    public static string FormatMode(ConnectorChoice choice, EndElevation elevation) =>
        $"{choice switch { ConnectorChoice.Fixo => "fixo", ConnectorChoice.Melhor3D => "melhor3d", ConnectorChoice.Melhor2D => "melhor2d", _ => "ajustar" }}_{(elevation == EndElevation.Manter ? "manter" : "corrigir")}";
}
