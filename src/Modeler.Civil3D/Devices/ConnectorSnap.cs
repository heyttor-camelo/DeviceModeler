using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Infra;

namespace Modeler.Civil3D.Devices;

/// <summary>Osnap próprio dos conectores dos dispositivos (técnica 10 do estudo E): ao desenhar um
/// tubo ou uma linha, o cursor prende no ponto do conector (centro do furo, no eixo do tubo). Liga e
/// desliga com DM_OSNAP; o glifo é um quadrado magenta com um círculo, a cor dos conectores.</summary>
internal static class ConnectorSnap
{
    public const string Name = "HYDROCONECTOR";

    private static CustomObjectSnapMode? _mode;
    private static ConnectorGlyph? _glyph;

    public static bool Active { get; private set; }

    public static void Register()
    {
        if (_mode is not null)
        {
            return;
        }

        try
        {
            _glyph = new ConnectorGlyph();
            _mode = new CustomObjectSnapMode(Name, Name, "Conector HYDRO", _glyph);
            _mode.ApplyToEntityType(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), new AddObjectSnapInfo(Snap));
            Activate(true);
        }
        catch (Exception ex)
        {
            HydroLog.Error("Osnap", "não foi possível registrar o osnap de conector", ex);
        }
    }

    public static void Activate(bool on)
    {
        if (_mode is null)
        {
            return;
        }

        if (on)
        {
            CustomObjectSnapMode.Activate(Name);
        }
        else
        {
            CustomObjectSnapMode.Deactivate(Name);
        }

        Active = on;
    }

    private static void Snap(ObjectSnapContext context, ObjectSnapInfo result)
    {
        try
        {
            if (context.PickedObject is not BlockReference reference || reference.Database is null)
            {
                return;
            }

            using var tr = reference.Database.TransactionManager.StartOpenCloseTransaction();
            var entry = InstanceCache.Get(tr, reference);
            tr.Commit();
            if (entry is null)
            {
                return;
            }

            var placement = DeviceService.PlacementOf(reference);
            foreach (var connector in entry.Instance.Connectors)
            {
                result.SnapPoints.Add(Solid3dBuilder.ToPoint(placement.ToWorld(connector.Position)));
            }
        }
        catch (Exception ex)
        {
            HydroLog.Warn("Osnap", ex.Message);
        }
    }

    /// <summary>Glifo do osnap: quadrado magenta com um círculo, do tamanho do GLYPHSIZE do AutoCAD.</summary>
    private sealed class ConnectorGlyph : Glyph
    {
        private Point3d _point;

        public override void SetLocation(Point3d point) => _point = point;

        protected override void SubViewportDraw(ViewportDraw vd)
        {
            try
            {
                var pixels = vd.Viewport.GetNumPixelsInUnitSquare(_point);
                var size = CustomObjectSnapMode.GlyphSize / pixels.Y;
                var toWorld = vd.Viewport.EyeToWorldTransform;
                var center = _point.TransformBy(toWorld);
                var normal = Vector3d.ZAxis.TransformBy(toWorld);
                var corners = new List<Point3d>();
                foreach (var (x, y) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0) })
                {
                    corners.Add(new Point3d(_point.X + (x * size), _point.Y + (y * size), _point.Z).TransformBy(toWorld));
                }

                vd.SubEntityTraits.Color = 6;
                vd.Geometry.Polyline(new Point3dCollection(corners.ToArray()), normal, IntPtr.Zero);
                vd.Geometry.Circle(center, size * 0.6, normal);
            }
            catch (Exception)
            {
                // glifo nunca derruba o cursor
            }
        }
    }
}
