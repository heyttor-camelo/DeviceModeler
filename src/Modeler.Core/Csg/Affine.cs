namespace Modeler.Core.Csg;

/// <summary>Transformação afim 3D (rotação/escala + translação) usada para acompanhar
/// <c>mover</c>/<c>rotacionar</c>/<c>array</c> ao percorrer a árvore CSG sem geometria real.</summary>
public readonly record struct Affine(Vec3 Ex, Vec3 Ey, Vec3 Ez, Vec3 Origin)
{
    public static readonly Affine Identity = new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);

    public static Affine Translation(Vec3 offset) => Identity with { Origin = offset };

    /// <summary>Rotação de <paramref name="degrees"/> (regra da mão direita) em torno do eixo
    /// <paramref name="axis"/> que passa por <paramref name="center"/> (fórmula de Rodrigues).</summary>
    public static Affine Rotation(double degrees, Vec3 axis, Vec3 center)
    {
        var k = axis.Normalized();
        var a = degrees * Math.PI / 180;
        var c = Math.Cos(a);
        var s = Math.Sin(a);
        Vec3 Rotate(Vec3 v) => (v * c) + (Cross(k, v) * s) + (k * (Dot(k, v) * (1 - c)));
        var ex = Rotate(Vec3.UnitX);
        var ey = Rotate(Vec3.UnitY);
        var ez = Rotate(Vec3.UnitZ);
        var moved = Rotate(center);
        return new Affine(ex, ey, ez, center - moved);
    }

    public Vec3 Apply(Vec3 p) => Origin + (Ex * p.X) + (Ey * p.Y) + (Ez * p.Z);

    public Vec3 ApplyVector(Vec3 v) => (Ex * v.X) + (Ey * v.Y) + (Ez * v.Z);

    /// <summary>Composição: primeiro <paramref name="inner"/>, depois esta.</summary>
    public Affine Then(Affine inner) =>
        new(ApplyVector(inner.Ex), ApplyVector(inner.Ey), ApplyVector(inner.Ez), Apply(inner.Origin));

    public static double Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    public static Vec3 Cross(Vec3 a, Vec3 b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}
