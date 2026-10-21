using Modeler.Core.Csg;

namespace Modeler.Core.Drawing;

/// <summary>Referencial local do alinhamento numa estaca (estudo C §4.1): o ponto do eixo, a tangente
/// horizontal unitária e a estaca. Como o alinhamento curvo não é plano, o plano de corte de cada
/// dispositivo é o vertical que contém a tangente na estaca mais próxima dele.</summary>
public readonly record struct AlignmentFrame(Vec3 Origin, Vec3 Tangent, double Station)
{
    /// <summary>Normal horizontal para a direita do alinhamento (o lado dos offsets positivos, como no Civil 3D).</summary>
    public Vec3 Right => new(Tangent.Y, -Tangent.X, 0);

    /// <summary>Estaca, offset (positivo à direita) e elevação de um ponto.</summary>
    public (double Station, double Offset, double Elevation) Map(Vec3 p)
    {
        var d = p - Origin;
        return (Station + Affine.Dot(d, Tangent), Affine.Dot(d, Right), p.Z);
    }

    /// <summary>Plano vertical do alinhamento nesta estaca (normal para a direita).</summary>
    public Plane CutPlane => new(Origin, Right);
}

/// <summary>Linha projetada na vista: pontos (estaca, elevação).</summary>
public sealed record ProjectedLine(IReadOnlyList<(double Station, double Elevation)> Points, bool Closed, bool Removed);

/// <summary>Projeção de um dispositivo numa vista de perfil, sem CAD: as linhas do aramado mapeadas
/// para (estaca, elevação), filtradas pelo lado, e a silhueta (envoltória) de cada peça. O corte
/// verdadeiro (face cortada e hachura) vem do sólido real no plugin.</summary>
public static class ProfileProjection
{
    /// <summary>O dispositivo aparece na vista? Fora da distância máxima, não; do lado contrário ao
    /// pedido, só se cruzar o plano do alinhamento (aparece em corte).</summary>
    public static bool Visible(double offset, double halfWidth, ProjectionSide side, double maxDistance)
    {
        if (Math.Abs(offset) - halfWidth > maxDistance + 1e-9)
        {
            return false;
        }

        var crosses = Math.Abs(offset) <= halfWidth + 1e-9;
        return side switch
        {
            ProjectionSide.Esquerdo => offset <= 0 || crosses,
            ProjectionSide.Direito => offset >= 0 || crosses,
            _ => true,
        };
    }

    /// <summary>Linhas do aramado na vista. Com lado, só ficam as linhas que têm algum ponto daquele lado.</summary>
    public static List<ProjectedLine> Lines(IEnumerable<WireLine> lines, AlignmentFrame frame, ProjectionSide side = ProjectionSide.Ambos)
    {
        var result = new List<ProjectedLine>();
        foreach (var line in lines)
        {
            var mapped = line.Points.Select(frame.Map).ToList();
            var keep = side switch
            {
                ProjectionSide.Esquerdo => mapped.Any(m => m.Offset <= 1e-9),
                ProjectionSide.Direito => mapped.Any(m => m.Offset >= -1e-9),
                _ => true,
            };
            if (keep)
            {
                result.Add(new ProjectedLine(mapped.Select(m => (m.Station, m.Elevation)).ToList(), line.Closed, line.Removed));
            }
        }

        return result;
    }

    /// <summary>Silhueta da peça na vista: envoltória convexa dos pontos projetados (estaca, elevação).</summary>
    public static IReadOnlyList<(double Station, double Elevation)> Outline(IEnumerable<WireLine> lines, AlignmentFrame frame) =>
        PlanOutline.ConvexHull(lines.SelectMany(l => l.Points).Select(p =>
        {
            var m = frame.Map(p);
            return (m.Station, m.Elevation);
        }).ToList());

    /// <summary>Meia largura do dispositivo na direção transversal ao alinhamento (para o filtro de distância).</summary>
    public static double HalfWidth(IEnumerable<WireLine> lines, AlignmentFrame frame, double deviceOffset)
    {
        var offsets = lines.SelectMany(l => l.Points).Select(p => frame.Map(p).Offset - deviceOffset).ToList();
        return offsets.Count == 0 ? 0 : offsets.Max(Math.Abs);
    }
}
