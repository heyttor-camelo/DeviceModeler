namespace Modeler.Core.Csg;

/// <summary>Contorno em planta (XY) de uma peça primitiva da árvore. <see cref="Removed"/> marca as
/// peças que entram como ferramenta de <c>subtrair</c> (furos, vazios), desenhadas tracejadas.</summary>
public sealed record OutlinePart(IReadOnlyList<(double X, double Y)> Points, bool Removed, double ZMin, double ZMax);

/// <summary>Planta esquemática da árvore CSG, sem geometria real: cada primitiva vira o polígono
/// convexo da sua projeção em XY, já com <c>mover</c>/<c>rotacionar</c>/<c>array</c> aplicados.
/// Booleanas não são resolvidas (é uma prévia para o monitor do painel, não um corte).</summary>
public sealed class PlanOutline : ICsgVisitor<bool>
{
    /// <summary>Limite de peças geradas (arrays grandes não travam o painel).</summary>
    public const int MaxParts = 2000;

    private const int CircleSegments = 24;

    private readonly List<OutlinePart> _parts = new();
    private Affine _transform = Affine.Identity;
    private bool _removed;

    private PlanOutline()
    {
    }

    public static IReadOnlyList<OutlinePart> Of(CsgNode node, Affine? placement = null)
    {
        var visitor = new PlanOutline { _transform = placement ?? Affine.Identity };
        node.Accept(visitor);
        return visitor._parts;
    }

    public bool VisitEmpty(EmptyNode node) => true;

    public bool VisitPrism(PrismNode node)
    {
        var (x, y, z) = (node.Length / 2, node.Width / 2, node.Height);
        var b = node.Base;
        var corners = new List<Vec3>(8);
        foreach (var dz in new[] { 0.0, z })
        {
            corners.Add(new Vec3(b.X - x, b.Y - y, b.Z + dz));
            corners.Add(new Vec3(b.X + x, b.Y - y, b.Z + dz));
            corners.Add(new Vec3(b.X + x, b.Y + y, b.Z + dz));
            corners.Add(new Vec3(b.X - x, b.Y + y, b.Z + dz));
        }

        return Add(corners);
    }

    public bool VisitCylinder(CylinderNode node)
    {
        var axis = node.Axis.Normalized();
        var u = Affine.Cross(axis, Math.Abs(axis.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX).Normalized();
        var v = Affine.Cross(axis, u);
        var r = node.Diameter / 2;
        var points = new List<Vec3>(CircleSegments * 2);
        for (var i = 0; i < CircleSegments; i++)
        {
            var a = 2 * Math.PI * i / CircleSegments;
            var rim = (u * (r * Math.Cos(a))) + (v * (r * Math.Sin(a)));
            points.Add(node.Base + rim);
            points.Add(node.Base + rim + (axis * node.Height));
        }

        return Add(points);
    }

    public bool VisitUnion(UnionNode node)
    {
        foreach (var child in node.Children)
        {
            child.Accept(this);
        }

        return true;
    }

    public bool VisitSubtract(SubtractNode node)
    {
        node.Source.Accept(this);
        var previous = _removed;
        _removed = true;
        foreach (var tool in node.Tools)
        {
            tool.Accept(this);
        }

        _removed = previous;
        return true;
    }

    public bool VisitIntersect(IntersectNode node)
    {
        foreach (var child in node.Children)
        {
            child.Accept(this);
        }

        return true;
    }

    public bool VisitTranslate(TranslateNode node) => With(Affine.Translation(node.Offset), node.Child);

    public bool VisitRotate(RotateNode node) => With(Affine.Rotation(node.AngleDegrees, node.Axis, node.Center), node.Child);

    public bool VisitArray(ArrayNode node)
    {
        for (var k = 0; k < node.Count && _parts.Count < MaxParts; k++)
        {
            With(Affine.Translation(node.Step * k), node.Child);
        }

        return true;
    }

    private bool With(Affine local, CsgNode child)
    {
        var previous = _transform;
        _transform = previous.Then(local);
        child.Accept(this);
        _transform = previous;
        return true;
    }

    private bool Add(IEnumerable<Vec3> points)
    {
        if (_parts.Count >= MaxParts)
        {
            return false;
        }

        var world = points.Select(_transform.Apply).ToList();
        var hull = ConvexHull(world.Select(p => (p.X, p.Y)).ToList());
        if (hull.Count >= 2)
        {
            _parts.Add(new OutlinePart(hull, _removed, world.Min(p => p.Z), world.Max(p => p.Z)));
        }

        return true;
    }

    /// <summary>Envoltória convexa (cadeia monótona de Andrew), em sentido anti-horário.</summary>
    internal static List<(double X, double Y)> ConvexHull(List<(double X, double Y)> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        static double Turn((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

        var hull = new List<(double X, double Y)>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], p) <= 1e-12)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }
}
