using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Modeler.Core.Csg;

namespace Modeler.Civil3D.Geometry;

/// <summary>Converte a árvore CSG do Core em um <see cref="Solid3d"/> (ACIS), no sistema local do
/// dispositivo (coordenadas do bloco). Devolve nulo para sólidos vazios. O chamador é dono do
/// sólido retornado (precisa adicioná-lo ao banco ou descartá-lo).</summary>
internal sealed class Solid3dBuilder : ICsgVisitor<Solid3d?>
{
    public static Solid3d? Build(CsgNode node) => node.Accept(new Solid3dBuilder());

    public Solid3d? VisitEmpty(EmptyNode node) => null;

    public Solid3d? VisitPrism(PrismNode node)
    {
        var solid = new Solid3d();
        solid.CreateBox(node.Length, node.Width, node.Height);
        // CreateBox é centrado na origem; a base do prisma é o centro da face inferior.
        solid.TransformBy(Matrix3d.Displacement(ToVector(node.Base) + new Vector3d(0, 0, node.Height / 2)));
        return solid;
    }

    public Solid3d? VisitCylinder(CylinderNode node)
    {
        var radius = node.Diameter / 2;
        var solid = new Solid3d();
        solid.CreateFrustum(node.Height, radius, radius, radius);
        // CreateFrustum é centrado na origem ao longo de Z: leva a face inicial para a origem
        // e depois alinha Z ao eixo pedido, com a base no ponto informado.
        solid.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, node.Height / 2)));
        var axis = ToVector(node.Axis).GetNormal();
        var xAxis = axis.GetPerpendicularVector().GetNormal();
        var yAxis = axis.CrossProduct(xAxis).GetNormal();
        solid.TransformBy(Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
            ToPoint(node.Base), xAxis, yAxis, axis));
        return solid;
    }

    public Solid3d? VisitUnion(UnionNode node) => Combine(node.Children, BooleanOperationType.BoolUnite, "unir");

    public Solid3d? VisitIntersect(IntersectNode node)
    {
        var result = Combine(node.Children, BooleanOperationType.BoolIntersect, "intersectar");
        return result is { IsNull: true } ? Discard(result) : result;
    }

    public Solid3d? VisitSubtract(SubtractNode node)
    {
        var source = node.Source.Accept(this);
        if (source is null)
        {
            return null;
        }

        foreach (var toolNode in node.Tools)
        {
            using var tool = toolNode.Accept(this);
            if (tool is null)
            {
                continue;
            }

            Boolean(source, tool, BooleanOperationType.BoolSubtract, "subtrair");
        }

        return source.IsNull ? Discard(source) : source;
    }

    public Solid3d? VisitTranslate(TranslateNode node)
    {
        var solid = node.Child.Accept(this);
        solid?.TransformBy(Matrix3d.Displacement(ToVector(node.Offset)));
        return solid;
    }

    public Solid3d? VisitRotate(RotateNode node)
    {
        var solid = node.Child.Accept(this);
        solid?.TransformBy(Matrix3d.Rotation(node.AngleDegrees * Math.PI / 180, ToVector(node.Axis), ToPoint(node.Center)));
        return solid;
    }

    public Solid3d? VisitArray(ArrayNode node)
    {
        var first = node.Child.Accept(this);
        if (first is null || node.Count <= 0)
        {
            return first is null ? null : Discard(first);
        }

        var step = ToVector(node.Step);
        for (var k = 1; k < node.Count; k++)
        {
            using var copy = (Solid3d)first.Clone();
            copy.TransformBy(Matrix3d.Displacement(step * k));
            Boolean(first, copy, BooleanOperationType.BoolUnite, "array");
        }

        return first;
    }

    public Solid3d? VisitExtrude(ExtrudeNode node)
    {
        var curves = new DBObjectCollection();
        try
        {
            var profile = node.Profile;
            for (var s = 0; s < profile.SegmentCount; s++)
            {
                var a = ToPoint(profile.Vertices[s]);
                var b = ToPoint(profile.Vertices[(s + 1) % profile.Vertices.Count]);
                curves.Add(Math.Abs(profile.Bulge(s)) < 1e-12
                    ? new Line(a, b)
                    : Curve.CreateFromGeCurve(new CircularArc3d(a, ToPoint(profile.ArcMidpoint(s)), b)));
            }

            var regions = Region.CreateFromCurves(curves);
            if (regions.Count == 0)
            {
                throw new InvalidOperationException("extrudar: o perfil não forma uma região fechada (verifique se os vértices são coplanares e se o perfil não se cruza)");
            }

            using var region = (Region)regions[0];
            for (var i = 1; i < regions.Count; i++)
            {
                regions[i].Dispose();
            }

            var solid = new Solid3d();
            solid.CreateExtrudedSolid(region, ToVector(node.Vector), new SweepOptions());
            return solid;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new InvalidOperationException($"falha em 'extrudar' no modelador de sólidos ({ex.ErrorStatus}): verifique o perfil (fechado, plano, sem autointerseção).", ex);
        }
        finally
        {
            foreach (DBObject curve in curves)
            {
                curve.Dispose();
            }
        }
    }

    /// <summary>Corta pelo plano e fica com a parte do lado da normal. O lado é conferido pelo
    /// centroide (a API não documenta com clareza qual metade fica no sólido original).</summary>
    public Solid3d? VisitCut(CutNode node)
    {
        var solid = node.Child.Accept(this);
        if (solid is null)
        {
            return null;
        }

        var plane = new Autodesk.AutoCAD.Geometry.Plane(ToPoint(node.Plane.Origin), ToVector(node.Plane.Normal));
        var extents = solid.GeometricExtents;
        var distances = Corners(extents).Select(p => node.Plane.SignedDistance(ToVec3(p))).ToList();
        if (distances.All(d => d >= -1e-9))
        {
            return solid;
        }

        if (distances.All(d => d <= 1e-9))
        {
            return Discard(solid);
        }

        try
        {
            var other = solid.Slice(plane, true);
            if (node.Plane.SignedDistance(ToVec3(solid.MassProperties.Centroid)) < 0 && other is not null)
            {
                solid.Dispose();
                return other;
            }

            other?.Dispose();
            return solid;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // o plano corta a caixa envolvente mas não o sólido: o sólido inteiro fica de um lado
            return node.Plane.SignedDistance(ToVec3(solid.MassProperties.Centroid)) >= 0 ? solid : Discard(solid);
        }
    }

    private static IEnumerable<Point3d> Corners(Extents3d e)
    {
        foreach (var x in new[] { e.MinPoint.X, e.MaxPoint.X })
        {
            foreach (var y in new[] { e.MinPoint.Y, e.MaxPoint.Y })
            {
                foreach (var z in new[] { e.MinPoint.Z, e.MaxPoint.Z })
                {
                    yield return new Point3d(x, y, z);
                }
            }
        }
    }

    private Solid3d? Combine(IReadOnlyList<CsgNode> children, BooleanOperationType operation, string name)
    {
        Solid3d? result = null;
        foreach (var child in children)
        {
            var solid = child.Accept(this);
            if (solid is null)
            {
                if (operation == BooleanOperationType.BoolIntersect)
                {
                    result?.Dispose();
                    return null;
                }

                continue;
            }

            if (result is null)
            {
                result = solid;
                continue;
            }

            using (solid)
            {
                Boolean(result, solid, operation, name);
            }
        }

        return result;
    }

    /// <summary>Operação booleana com mensagem clara em caso de falha do ACIS.</summary>
    private static void Boolean(Solid3d target, Solid3d tool, BooleanOperationType operation, string name)
    {
        try
        {
            target.BooleanOperation(operation, tool);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new InvalidOperationException($"falha na operação '{name}' do modelador de sólidos ({ex.ErrorStatus}). " +
                                                "Verifique se as peças se tocam corretamente (faces coincidentes costumam falhar).", ex);
        }
    }

    private static Solid3d? Discard(Solid3d solid)
    {
        solid.Dispose();
        return null;
    }

    internal static Vector3d ToVector(Vec3 v) => new(v.X, v.Y, v.Z);

    internal static Point3d ToPoint(Vec3 v) => new(v.X, v.Y, v.Z);

    internal static Vec3 ToVec3(Point3d p) => new(p.X, p.Y, p.Z);
}
