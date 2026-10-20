namespace Modeler.Core.Csg;

/// <summary>Visitante da árvore CSG. Na Fase 2, um <c>Solid3dBuilder : ICsgVisitor&lt;Solid3d&gt;</c>
/// cria os sólidos ACIS; no Core, <see cref="NaiveVolume"/> calcula volumes para conferência.</summary>
public interface ICsgVisitor<out T>
{
    T VisitEmpty(EmptyNode node);

    T VisitPrism(PrismNode node);

    T VisitCylinder(CylinderNode node);

    T VisitUnion(UnionNode node);

    T VisitSubtract(SubtractNode node);

    T VisitIntersect(IntersectNode node);

    T VisitTranslate(TranslateNode node);

    T VisitRotate(RotateNode node);

    T VisitArray(ArrayNode node);
}
