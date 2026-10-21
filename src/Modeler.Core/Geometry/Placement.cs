using Modeler.Core.Csg;

namespace Modeler.Core.Geometry;

/// <summary>Posição de uma instância no desenho: ponto de inserção (X, Y) e rotação em planta.
/// O sistema local do dispositivo tem Z = cota absoluta, então só X/Y são transformados;
/// <see cref="Origin"/>.Z é um deslocamento vertical (normalmente 0).</summary>
public readonly record struct Placement(Vec3 Origin, double RotationDegrees)
{
    public static readonly Placement Identity = new(Vec3.Zero, 0);

    private double Radians => RotationDegrees * Math.PI / 180;

    public Vec3 ToWorld(Vec3 local)
    {
        var (sin, cos) = Math.SinCos(Radians);
        return new Vec3(
            Origin.X + (local.X * cos) - (local.Y * sin),
            Origin.Y + (local.X * sin) + (local.Y * cos),
            Origin.Z + local.Z);
    }

    public Vec3 ToLocal(Vec3 world)
    {
        var (sin, cos) = Math.SinCos(Radians);
        var dx = world.X - Origin.X;
        var dy = world.Y - Origin.Y;
        return new Vec3((dx * cos) + (dy * sin), (-dx * sin) + (dy * cos), world.Z - Origin.Z);
    }

    public Vec3 DirectionToWorld(Vec3 local)
    {
        var (sin, cos) = Math.SinCos(Radians);
        return new Vec3((local.X * cos) - (local.Y * sin), (local.X * sin) + (local.Y * cos), local.Z);
    }

    public Vec3 DirectionToLocal(Vec3 world)
    {
        var (sin, cos) = Math.SinCos(Radians);
        return new Vec3((world.X * cos) + (world.Y * sin), (-world.X * sin) + (world.Y * cos), world.Z);
    }

    /// <summary>A mesma transformação como <see cref="Affine"/> (para percorrer a árvore CSG já no desenho).</summary>
    public Affine ToAffine() => Affine.Translation(Origin).Then(Affine.Rotation(RotationDegrees, Vec3.UnitZ, Vec3.Zero));

    /// <summary>Ângulo em graus no intervalo [0, 360).</summary>
    public static double NormalizeAngle(double degrees)
    {
        var a = degrees % 360;
        if (a < 0)
        {
            a += 360;
        }

        return Math.Abs(a - 360) < 1e-9 ? 0 : a;
    }
}
