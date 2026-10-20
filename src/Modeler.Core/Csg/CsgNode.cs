namespace Modeler.Core.Csg;

/// <summary>Nó da árvore CSG abstrata. Não contém malha: a Fase 2 (Modeler.Civil3D)
/// converte a árvore em Solid3d implementando <see cref="ICsgVisitor{T}"/>.</summary>
public abstract record CsgNode
{
    public abstract T Accept<T>(ICsgVisitor<T> visitor);
}

/// <summary>Sólido vazio (elemento neutro de <c>unir</c>). Saídas vazias são omitidas.</summary>
public sealed record EmptyNode : CsgNode
{
    public static readonly EmptyNode Instance = new();

    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitEmpty(this);
}

/// <summary>Caixa alinhada aos eixos locais: <paramref name="Length"/> em X, <paramref name="Width"/>
/// em Y, <paramref name="Height"/> em Z. <paramref name="Base"/> é o centro da face inferior.</summary>
public sealed record PrismNode(double Length, double Width, double Height, Vec3 Base) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitPrism(this);
}

/// <summary>Cilindro de diâmetro <paramref name="Diameter"/> e comprimento <paramref name="Height"/>.
/// <paramref name="Base"/> é o centro da face inicial e <paramref name="Axis"/> (unitário) aponta
/// para a face final.</summary>
public sealed record CylinderNode(double Diameter, double Height, Vec3 Base, Vec3 Axis) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitCylinder(this);
}

public sealed record UnionNode(IReadOnlyList<CsgNode> Children) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitUnion(this);
}

/// <summary><paramref name="Source"/> menos todos os <paramref name="Tools"/>.</summary>
public sealed record SubtractNode(CsgNode Source, IReadOnlyList<CsgNode> Tools) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitSubtract(this);
}

public sealed record IntersectNode(IReadOnlyList<CsgNode> Children) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitIntersect(this);
}

public sealed record TranslateNode(CsgNode Child, Vec3 Offset) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitTranslate(this);
}

/// <summary>Rotação de <paramref name="AngleDegrees"/> (regra da mão direita) em torno do eixo
/// <paramref name="Axis"/> que passa por <paramref name="Center"/>.</summary>
public sealed record RotateNode(CsgNode Child, double AngleDegrees, Vec3 Axis, Vec3 Center) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitRotate(this);
}

/// <summary><paramref name="Count"/> cópias de <paramref name="Child"/>; a cópia k é deslocada
/// de k·<paramref name="Step"/> (k = 0..Count-1).</summary>
public sealed record ArrayNode(CsgNode Child, int Count, Vec3 Step) : CsgNode
{
    public override T Accept<T>(ICsgVisitor<T> visitor) => visitor.VisitArray(this);
}
