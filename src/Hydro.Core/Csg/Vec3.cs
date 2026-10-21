using System.Globalization;

namespace Hydro.Core.Csg;

/// <summary>Vetor/ponto 3D no sistema local do dispositivo (X, Y relativos ao ponto de
/// inserção; Z = cota absoluta, em metros).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public Vec3 Normalized()
    {
        var length = Length;
        if (length < 1e-12)
        {
            throw new InvalidOperationException("vetor nulo não pode ser normalizado");
        }

        return this * (1.0 / length);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{X:G10}, {Y:G10}, {Z:G10}]");
}
