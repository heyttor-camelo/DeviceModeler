namespace Hydro.Core.Csg;

/// <summary>Volume "ingênuo" da árvore CSG, sem geometria real: supõe que as partes de
/// <c>unir</c>/<c>array</c> não se sobrepõem e que as ferramentas de <c>subtrair</c> estão contidas
/// na peça e não se sobrepõem entre si. Serve para conferir quantitativos em testes; o volume
/// exato vem do Solid3d na Fase 2.</summary>
public sealed class NaiveVolume : ICsgVisitor<double>
{
    public static readonly NaiveVolume Instance = new();

    public static double Of(CsgNode node) => node.Accept(Instance);

    public double VisitEmpty(EmptyNode node) => 0;

    public double VisitPrism(PrismNode node) => node.Length * node.Width * node.Height;

    public double VisitCylinder(CylinderNode node) => Math.PI * node.Diameter * node.Diameter / 4 * node.Height;

    public double VisitUnion(UnionNode node) => node.Children.Sum(c => c.Accept(this));

    public double VisitSubtract(SubtractNode node) =>
        node.Source.Accept(this) - node.Tools.Sum(t => t.Accept(this));

    public double VisitIntersect(IntersectNode node) =>
        throw new NotSupportedException("NaiveVolume não calcula interseções; use o volume do sólido real.");

    public double VisitTranslate(TranslateNode node) => node.Child.Accept(this);

    public double VisitRotate(RotateNode node) => node.Child.Accept(this);

    public double VisitArray(ArrayNode node) => node.Count * node.Child.Accept(this);

    /// <summary>Área do perfil × componente do vetor na normal (prisma oblíquo tem o mesmo volume).</summary>
    public double VisitExtrude(ExtrudeNode node) => node.Profile.Area() * Math.Abs(Affine.Dot(node.Vector, node.Profile.Normal));

    public double VisitCut(CutNode node) =>
        throw new NotSupportedException("NaiveVolume não calcula cortes por plano; use o volume do sólido real ou um quantitativo analítico.");
}
