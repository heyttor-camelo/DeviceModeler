namespace Hydro.Core.Csg;

/// <summary>Linha 3D do aramado; <see cref="Removed"/> marca as ferramentas de <c>subtrair</c>.</summary>
public sealed record WireLine(IReadOnlyList<Vec3> Points, bool Closed, bool Removed);

/// <summary>Aramado da árvore CSG, sem geometria real (sem booleanas): as arestas de cada primitiva
/// já transformada. É a prévia barata que o grip desenha a cada movimento do mouse; o sólido
/// verdadeiro só é refeito ao soltar.</summary>
public sealed class Wireframe : ICsgVisitor<bool>
{
    public const int MaxLines = 4000;

    private const int CircleSegments = 24;

    private readonly List<WireLine> _lines = new();
    private Affine _transform = Affine.Identity;
    private bool _removed;

    private Wireframe()
    {
    }

    public static IReadOnlyList<WireLine> Of(CsgNode node, Affine? placement = null)
    {
        var visitor = new Wireframe { _transform = placement ?? Affine.Identity };
        node.Accept(visitor);
        return visitor._lines;
    }

    public bool VisitEmpty(EmptyNode node) => true;

    public bool VisitPrism(PrismNode node)
    {
        var (x, y) = (node.Length / 2, node.Width / 2);
        var b = node.Base;
        Vec3[] Ring(double z) => new[] { new Vec3(b.X - x, b.Y - y, z), new Vec3(b.X + x, b.Y - y, z), new Vec3(b.X + x, b.Y + y, z), new Vec3(b.X - x, b.Y + y, z) };
        var bottom = Ring(b.Z);
        var top = Ring(b.Z + node.Height);
        Add(bottom, true);
        Add(top, true);
        for (var i = 0; i < 4; i++)
        {
            Add(new[] { bottom[i], top[i] }, false);
        }

        return true;
    }

    public bool VisitCylinder(CylinderNode node)
    {
        var axis = node.Axis.Normalized();
        var u = Affine.Cross(axis, Math.Abs(axis.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX).Normalized();
        var v = Affine.Cross(axis, u);
        var r = node.Diameter / 2;
        var start = new List<Vec3>();
        var end = new List<Vec3>();
        for (var i = 0; i < CircleSegments; i++)
        {
            var a = 2 * Math.PI * i / CircleSegments;
            var rim = (u * (r * Math.Cos(a))) + (v * (r * Math.Sin(a)));
            start.Add(node.Base + rim);
            end.Add(node.Base + rim + (axis * node.Height));
        }

        Add(start, true);
        Add(end, true);
        for (var i = 0; i < CircleSegments; i += CircleSegments / 4)
        {
            Add(new[] { start[i], end[i] }, false);
        }

        return true;
    }

    public bool VisitExtrude(ExtrudeNode node)
    {
        var bottom = node.Profile.Sample();
        var top = bottom.Select(p => p + node.Vector).ToList();
        Add(bottom, true);
        Add(top, true);
        foreach (var vertex in node.Profile.Vertices)
        {
            Add(new[] { vertex, vertex + node.Vector }, false);
        }

        return true;
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
        for (var k = 0; k < node.Count && _lines.Count < MaxLines; k++)
        {
            With(Affine.Translation(node.Step * k), node.Child);
        }

        return true;
    }

    /// <summary>Corte: mostra a peça inteira (prévia).</summary>
    public bool VisitCut(CutNode node) => node.Child.Accept(this);

    private bool With(Affine local, CsgNode child)
    {
        var previous = _transform;
        _transform = previous.Then(local);
        child.Accept(this);
        _transform = previous;
        return true;
    }

    private void Add(IEnumerable<Vec3> points, bool closed)
    {
        if (_lines.Count < MaxLines)
        {
            _lines.Add(new WireLine(points.Select(_transform.Apply).ToList(), closed, _removed));
        }
    }
}
