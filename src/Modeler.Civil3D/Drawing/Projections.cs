using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Infra;
using Modeler.Core.Csg;
using Modeler.Core.Drawing;
using Modeler.Core.Geometry;
using AcPlane = Autodesk.AutoCAD.Geometry.Plane;
using ProfileProjection = Modeler.Core.Drawing.ProfileProjection;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Modeler.Civil3D.Drawing;

/// <summary>Projeção de dispositivo numa vista de perfil (pedido 2; estudo C §4 e E técnica 9).
/// Não é a projeção por estaca da Pipe Network: o sólido do dispositivo é <b>cortado</b> pelo plano
/// vertical do alinhamento na estaca dele (a tangente local, porque o alinhamento curvo não é plano),
/// e o resultado é levado para a vista por <c>FindXYAtStationAndElevation</c> (exagero vertical e
/// datum, inclusive em vista dividida). Componentes do estilo: corte (arestas da face cortada),
/// hachura da face cortada, contorno (silhueta), linhas ocultas e eixo. A projeção é um bloco anônimo
/// próprio, com o vínculo (dispositivo, vista, estilo, lado, distância) no <see cref="OutputRecord"/>;
/// ela se refaz quando o dispositivo ou a vista mudam.</summary>
internal static class Projections
{
    private const int CurveSamples = 24;

    /// <summary>Cria a projeção (bloco + referência na origem) e devolve a referência.</summary>
    public static BlockReference Create(Transaction tr, Database db, BlockReference device, ProfileView view, OutputRecord record)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var block = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
        var blockId = table.Add(block);
        tr.AddNewlyCreatedDBObject(block, true);
        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        var reference = new BlockReference(Point3d.Origin, blockId);
        reference.SetDatabaseDefaults(db);
        reference.LayerId = Layers.Ensure(tr, db, "DM-PERFIL", 7);
        space.AppendEntity(reference);
        tr.AddNewlyCreatedDBObject(reference, true);
        Fill(tr, db, block, device, view, record);
        record.Write(reference, tr);
        return reference;
    }

    /// <summary>Refaz o conteúdo. Falso se o dispositivo ou a vista não existem mais (o chamador apaga).</summary>
    public static bool Update(Transaction tr, Database db, BlockReference projection, OutputRecord record)
    {
        if (record.Devices.Count == 0 || !CivilLinks.TryGet<BlockReference>(tr, db, record.Devices[0], out var device) ||
            !CivilLinks.TryGet<ProfileView>(tr, db, record.ProfileView, out var view))
        {
            return false;
        }

        var block = (BlockTableRecord)tr.GetObject(projection.BlockTableRecord, OpenMode.ForWrite);
        foreach (ObjectId id in block)
        {
            tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        Fill(tr, db, block, device, view, record);
        projection.Writable();
        // a projeção é desenhada em coordenadas da vista: um MOVE/ROTATE/SCALE nela é desfeito
        projection.Position = Point3d.Origin;
        projection.Rotation = 0;
        projection.ScaleFactors = new Scale3d(1);
        record.Write(projection, tr);
        projection.RecordGraphicsModified(true);
        return true;
    }

    /// <summary>Referencial do alinhamento da vista na estaca do ponto (tangente por diferença).</summary>
    public static (AlignmentFrame Frame, double Offset)? FrameAt(Alignment alignment, Point3d point)
    {
        double station = 0, offset = 0, e = 0, n = 0, e1 = 0, n1 = 0, e2 = 0, n2 = 0;
        try
        {
            alignment.StationOffset(point.X, point.Y, ref station, ref offset);
        }
        catch (System.Exception)
        {
            return null;
        }

        const double step = 0.05;
        alignment.PointLocation(station, 0, ref e, ref n);
        alignment.PointLocation(Math.Max(alignment.StartingStation, station - step), 0, ref e1, ref n1);
        alignment.PointLocation(Math.Min(alignment.EndingStation, station + step), 0, ref e2, ref n2);
        var tangent = new Vec3(e2 - e1, n2 - n1, 0);
        if (tangent.Length < 1e-12)
        {
            return null;
        }

        return (new AlignmentFrame(new Vec3(e, n, 0), tangent.Normalized(), station), offset);
    }

    private static void Fill(Transaction tr, Database db, BlockTableRecord block, BlockReference device, ProfileView view, OutputRecord record)
    {
        var entry = InstanceCache.Get(tr, device);
        var alignment = (Alignment)tr.GetObject(view.AlignmentId, OpenMode.ForRead);
        var style = StyleStore.Projection(tr, db, record.Style);
        var side = Enum.TryParse<ProjectionSide>(record.Side, true, out var s) ? s : style.Side;
        var maxDistance = record.MaxDistance ?? style.MaxDistance;
        record.Status = "";
        if (entry is null)
        {
            record.Status = "dispositivo sem definição válida";
            return;
        }

        if (FrameAt(alignment, device.Position) is not { } at)
        {
            record.Status = "fora do alinhamento da vista";
            return;
        }

        var (frame, offset) = at;
        if (frame.Station < view.StationStart - 1e-6 || frame.Station > view.StationEnd + 1e-6)
        {
            record.Status = $"estaca {frame.Station:0.00} fora da vista ({view.StationStart:0.00} a {view.StationEnd:0.00})";
            return;
        }

        var placement = DeviceService.PlacementOf(device);
        var affine = placement.ToAffine();
        var wire = entry.Instance.Solids.ToDictionary(o => o.Name, o => Wireframe.Of(o.Node, affine).ToList());
        var halfWidth = ProfileProjection.HalfWidth(wire.Values.SelectMany(w => w), frame, offset);
        if (!ProfileProjection.Visible(offset, halfWidth, side, maxDistance))
        {
            record.Status = Math.Abs(offset) - halfWidth > maxDistance
                ? $"a {Math.Abs(offset):0.00} m do alinhamento (máximo {maxDistance:0.00} m)"
                : $"do lado {(offset > 0 ? "direito" : "esquerdo")}, e a projeção é do lado {side.ToString().ToLowerInvariant()}";
            return;
        }

        Point3d? Map(double station, double elevation)
        {
            double x = 0, y = 0;
            try
            {
                return view.FindXYAtStationAndElevation(station, elevation, ref x, ref y) ? new Point3d(x, y, 0) : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        // eixo
        var all = wire.Values.SelectMany(w => w).SelectMany(l => l.Points).ToList();
        if (all.Count > 0 && style.Component("eixo") is { Visible: true } axis &&
            Map(frame.Station, all.Min(p => p.Z) - 0.2) is { } a && Map(frame.Station, all.Max(p => p.Z) + 0.2) is { } b)
        {
            Add(tr, db, block, new Line(a, b), axis);
        }

        // linhas ocultas (arestas fora do plano) e contorno (silhueta) de cada peça
        foreach (var (name, lines) in wire)
        {
            if (style.Component("ocultas") is { Visible: true } hidden)
            {
                foreach (var line in ProfileProjection.Lines(lines, frame, side))
                {
                    AddPolyline(tr, db, block, line.Points.Select(p => Map(p.Station, p.Elevation)), line.Closed, hidden);
                }
            }

            if (style.Component("contorno") is { Visible: true } contour)
            {
                AddPolyline(tr, db, block, ProfileProjection.Outline(lines, frame).Select(p => Map(p.Station, p.Elevation)), true, contour);
            }
        }

        // corte: a face do sólido real no plano do alinhamento, com a hachura
        var cut = style.Component("corte");
        var hatch = style.Component("hachura");
        if (!cut.Visible && !hatch.Visible)
        {
            return;
        }

        var plane = new AcPlane(Solid3dBuilder.ToPoint(frame.Origin), Solid3dBuilder.ToVector(frame.Right));
        var deviceBlock = (BlockTableRecord)tr.GetObject(device.BlockTableRecord, OpenMode.ForRead);
        foreach (ObjectId id in deviceBlock)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Solid3d solid)
            {
                continue;
            }

            foreach (var loop in Section(solid, device.BlockTransform, plane))
            {
                var mapped = loop.Select(p => frame.Map(Solid3dBuilder.ToVec3(p))).Select(m => Map(m.Station, m.Elevation)).ToList();
                if (mapped.Any(p => p is null))
                {
                    continue;
                }

                var points = mapped.Select(p => p!.Value).ToList();
                if (cut.Visible)
                {
                    AddPolyline(tr, db, block, points.Select(p => (Point3d?)p), true, cut);
                }

                if (hatch.Visible && points.Count >= 3)
                {
                    AddHatch(tr, db, block, points, hatch, style.Hatch);
                }
            }
        }
    }

    /// <summary>Laços fechados (pontos no desenho) da seção do sólido pelo plano.</summary>
    private static List<List<Point3d>> Section(Solid3d solid, Matrix3d toWorld, AcPlane plane)
    {
        var loops = new List<List<Point3d>>();
        using var world = (Solid3d)solid.Clone();
        world.TransformBy(toWorld);
        Region? region;
        try
        {
            region = world.GetSection(plane);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return loops; // o plano não corta esta peça
        }

        if (region is null)
        {
            return loops;
        }

        using (region)
        {
            var curves = new DBObjectCollection();
            try
            {
                region.Explode(curves);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return loops;
            }

            var pieces = new List<List<Point3d>>();
            Collect(curves, pieces);

            loops.AddRange(Chain(pieces));
        }

        return loops;
    }

    /// <summary>Curvas da explosão; uma seção com várias faces separadas explode em regiões, que são
    /// explodidas de novo.</summary>
    private static void Collect(DBObjectCollection items, List<List<Point3d>> pieces)
    {
        foreach (DBObject item in items)
        {
            if (item is Curve curve)
            {
                pieces.Add(Sample(curve));
            }
            else if (item is Region region)
            {
                var inner = new DBObjectCollection();
                try
                {
                    region.Explode(inner);
                    Collect(inner, pieces);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // região que não explode: fica sem corte
                }
            }

            item.Dispose();
        }
    }

    private static List<Point3d> Sample(Curve curve)
    {
        if (curve is Line line)
        {
            return new List<Point3d> { line.StartPoint, line.EndPoint };
        }

        var points = new List<Point3d>();
        var (start, end) = (curve.StartParam, curve.EndParam);
        for (var i = 0; i <= CurveSamples; i++)
        {
            points.Add(curve.GetPointAtParameter(start + ((end - start) * i / CurveSamples)));
        }

        return points;
    }

    /// <summary>Junta os pedaços pelas pontas (tolerância de 1 mm) em laços fechados.</summary>
    internal static List<List<Point3d>> Chain(List<List<Point3d>> pieces)
    {
        const double tolerance = 1e-3;
        var loops = new List<List<Point3d>>();
        var open = pieces.Where(p => p.Count >= 2).ToList();
        while (open.Count > 0)
        {
            var loop = new List<Point3d>(open[0]);
            open.RemoveAt(0);
            var closed = loop[0].DistanceTo(loop[^1]) < tolerance;
            while (!closed)
            {
                var index = open.FindIndex(p => p[0].DistanceTo(loop[^1]) < tolerance || p[^1].DistanceTo(loop[^1]) < tolerance);
                if (index < 0)
                {
                    break;
                }

                var next = open[index];
                open.RemoveAt(index);
                if (next[0].DistanceTo(loop[^1]) >= tolerance)
                {
                    next = Enumerable.Reverse(next).ToList();
                }

                loop.AddRange(next.Skip(1));
                closed = loop[0].DistanceTo(loop[^1]) < tolerance;
            }

            if (closed && loop.Count >= 4)
            {
                loop.RemoveAt(loop.Count - 1);
                loops.Add(loop);
            }
        }

        return loops;
    }

    private static void AddPolyline(Transaction tr, Database db, BlockTableRecord block, IEnumerable<Point3d?> points, bool closed, StyleComponent component)
    {
        var list = points.Where(p => p is not null).Select(p => p!.Value).ToList();
        if (list.Count < 2)
        {
            return;
        }

        var polyline = new Polyline();
        for (var i = 0; i < list.Count; i++)
        {
            polyline.AddVertexAt(i, new Point2d(list[i].X, list[i].Y), 0, 0, 0);
        }

        polyline.Closed = closed && list.Count > 2;
        Add(tr, db, block, polyline, component);
    }

    private static void AddHatch(Transaction tr, Database db, BlockTableRecord block, List<Point3d> points, StyleComponent component, HatchSettings settings)
    {
        var hatch = new Hatch();
        Add(tr, db, block, hatch, component);
        try
        {
            hatch.PatternScale = settings.Scale;
            hatch.PatternAngle = settings.Angle * Math.PI / 180;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, settings.Pattern);
            var vertices = new Point2dCollection(points.Select(p => new Point2d(p.X, p.Y)).Append(new Point2d(points[0].X, points[0].Y)).ToArray());
            var bulges = new DoubleCollection(Enumerable.Repeat(0.0, vertices.Count).ToArray());
            hatch.AppendLoop(HatchLoopTypes.Polyline, vertices, bulges);
            hatch.EvaluateHatch(true);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            HydroLog.Warn("Projeção", $"hachura '{settings.Pattern}' não aplicada: {ex.ErrorStatus}");
            hatch.Erase();
        }
    }

    private static void Add(Transaction tr, Database db, BlockTableRecord block, Entity entity, StyleComponent component)
    {
        entity.SetDatabaseDefaults(db);
        entity.LayerId = Layers.Ensure(tr, db, component.Layer ?? "DM-PERFIL", component.Color ?? 7, component.Linetype);
        entity.ColorIndex = component.Color ?? 256;
        if (component.Linetype is { } linetype && Layers.Linetype(tr, db, linetype) is { IsNull: false } id)
        {
            entity.LinetypeId = id;
        }

        block.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
    }
}
