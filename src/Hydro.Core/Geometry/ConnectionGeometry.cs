using Hydro.Core.Csg;
using Hydro.Core.Results;

namespace Hydro.Core.Geometry;

/// <summary>Converte um tubo (eixo em coordenadas do desenho) numa <see cref="ConnectionInput"/>
/// relativa ao dispositivo. Usado pela Fase 2 ao ligar tubos do Civil 3D a um PV/caixa.</summary>
public static class ConnectionGeometry
{
    /// <param name="placement">posição do dispositivo.</param>
    /// <param name="start">ponto inicial do eixo do tubo (centro da seção).</param>
    /// <param name="end">ponto final do eixo do tubo.</param>
    /// <param name="innerDiameter">diâmetro interno (m).</param>
    /// <param name="outerDiameter">diâmetro externo (m); nulo = igual ao interno.</param>
    /// <returns>A extremidade mais próxima do dispositivo (em planta) é a que se liga a ele; o ângulo
    /// aponta do dispositivo para o tubo e a cota é a geratriz inferior interna nessa extremidade.</returns>
    public static ConnectionInput FromPipe(
        Placement placement,
        Vec3 start,
        Vec3 end,
        double innerDiameter,
        double? outerDiameter,
        string name,
        string? reference = null)
    {
        if (innerDiameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(innerDiameter), "diâmetro interno deve ser maior que zero");
        }

        var startLocal = placement.ToLocal(start);
        var endLocal = placement.ToLocal(end);
        var startIsNear = PlanDistance(startLocal) <= PlanDistance(endLocal);
        var near = startIsNear ? startLocal : endLocal;
        var far = startIsNear ? endLocal : startLocal;

        var dx = far.X - near.X;
        var dy = far.Y - near.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            throw new ArgumentException("tubo vertical (sem direção em planta) não pode ser ligado");
        }

        var angle = Placement.NormalizeAngle(Math.Atan2(dy, dx) * 180 / Math.PI);
        var invert = near.Z - (innerDiameter / 2);
        return new ConnectionInput(name, angle, invert, innerDiameter, outerDiameter ?? innerDiameter, reference);
    }

    /// <summary>Distância em planta do ponto (em coordenadas locais) ao ponto de inserção.</summary>
    public static double PlanDistance(Vec3 local) => Math.Sqrt((local.X * local.X) + (local.Y * local.Y));
}
