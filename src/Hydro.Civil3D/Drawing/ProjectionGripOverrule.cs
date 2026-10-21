using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.Civil.DatabaseServices;
using Hydro.Civil3D.Civil;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Geometry;
using Hydro.Civil3D.Infra;
using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Drawing;
using Hydro.Core.Editing;
using Hydro.Core.Errors;
using Hydro.Core.Results;
using ProfileProjection = Hydro.Core.Drawing.ProfileProjection;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Hydro.Civil3D.Drawing;

/// <summary>Grips do dispositivo na vista de perfil (o "grip em ProfileView" do SOLIDOS): nas projeções,
/// os parâmetros com <c>grip: { tipo: perfil }</c> (e os de esticar em Z) aparecem como losango ciano
/// (ACI 4, contorno ACI 7) que só anda na vertical. O deslocamento em Y vira Δelevação pela escala
/// vertical da vista (exagero incluído); durante o arrasto aparece a projeção do dispositivo com o
/// valor novo. Ao soltar, a edição vai para a fila e o dispositivo se refaz — a projeção acompanha.</summary>
internal sealed class ProjectionGripOverrule : GripOverrule
{
    private static ProjectionGripOverrule? _instance;

    public static void Register()
    {
        if (_instance is not null)
        {
            return;
        }

        _instance = new ProjectionGripOverrule();
        _instance.SetExtensionDictionaryEntryFilter(OutputRecord.Key);
        AddOverrule(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _instance, false);
        Overruling = true;
    }

    public static void Unregister()
    {
        if (_instance is null)
        {
            return;
        }

        RemoveOverrule(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _instance);
        _instance.Dispose();
        _instance = null;
    }

    public override void GetGripPoints(Entity entity, GripDataCollection grips, double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
    {
        if (entity is not BlockReference projection || projection.Database is null || projection.ObjectId.IsNull)
        {
            base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
            return;
        }

        try
        {
            var db = projection.Database;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            if (OutputRecord.TryRead(projection, tr) is not { Kind: OutputKind.Projecao } record || record.Devices.Count == 0 ||
                !CivilLinks.TryGet<BlockReference>(tr, db, record.Devices[0], out var device) ||
                !CivilLinks.TryGet<ProfileView>(tr, db, record.ProfileView, out var view) ||
                InstanceCache.Get(tr, device) is not { } entry ||
                Projections.FrameAt((Alignment)tr.GetObject(view.AlignmentId, OpenMode.ForRead), device.Position) is not { } at)
            {
                tr.Commit();
                return; // projeção sem grips próprios (não deixa mover o bloco da projeção)
            }

            var placement = DeviceService.PlacementOf(device);
            foreach (var grip in entry.Instance.Grips.Where(g => g.Kind == GripKind.Perfil || (g.Kind == GripKind.Esticar && Math.Abs(g.Direction.Z) > 0.999)))
            {
                var (station, _, elevation) = at.Frame.Map(placement.ToWorld(grip.Position));
                if (ViewMap.Create(view, station, elevation) is { } map)
                {
                    grips.Add(new ProfileGrip(device.ObjectId, entry, grip, placement, at.Frame, map));
                }
            }

            tr.Commit();
        }
        catch (Exception ex)
        {
            Guard.Report("Grips: vista de perfil", ex);
        }
    }

    public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
    {
        foreach (GripData grip in grips)
        {
            if (grip is ProfileGrip profile)
            {
                Guard.Run("Grips", () => profile.Drag(offset));
            }
        }
    }

    /// <summary>Mapeamento afim (estaca, elevação) → XY da vista em torno de um ponto (a prévia não abre
    /// a vista a cada movimento; o resultado final usa o mapeamento exato).</summary>
    private sealed record ViewMap(double Station, double Elevation, Point3d Origin, Vector3d PerStation, Vector3d PerMeter)
    {
        public static ViewMap? Create(ProfileView view, double station, double elevation)
        {
            double x0 = 0, y0 = 0, x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            if (!view.FindXYAtStationAndElevation(station, elevation, ref x0, ref y0) ||
                !view.FindXYAtStationAndElevation(station + 1, elevation, ref x1, ref y1) ||
                !view.FindXYAtStationAndElevation(station, elevation + 1, ref x2, ref y2))
            {
                return null;
            }

            return new ViewMap(station, elevation, new Point3d(x0, y0, 0), new Vector3d(x1 - x0, y1 - y0, 0), new Vector3d(x2 - x0, y2 - y0, 0));
        }

        public Point3d At(double station, double elevation) => Origin + (PerStation * (station - Station)) + (PerMeter * (elevation - Elevation));
    }

    private sealed class ProfileGrip : GripData
    {
        private readonly ObjectId _device;
        private readonly InstanceCache.Entry _entry;
        private readonly GripInstance _grip;
        private readonly Hydro.Core.Geometry.Placement _placement;
        private readonly AlignmentFrame _frame;
        private readonly ViewMap _map;
        private readonly Point3d _original;
        private List<ProjectedLine>? _preview;
        private double? _value;
        private string _text = "";

        public ProfileGrip(ObjectId device, InstanceCache.Entry entry, GripInstance grip, Hydro.Core.Geometry.Placement placement, AlignmentFrame frame, ViewMap map)
        {
            _device = device;
            _entry = entry;
            _grip = grip;
            _placement = placement;
            _frame = frame;
            _map = map;
            _original = map.Origin;
            GripPoint = _original;
            DrawAtDragImageGripPoint = true;
        }

        public void Drag(Vector3d offset)
        {
            // só na vertical da vista
            var along = _map.PerMeter.GetNormal();
            var moved = _original + (along * offset.DotProduct(along));
            GripPoint = moved;
            var parameter = _entry.Definition.FindParameter(_grip.Parameter);
            if (parameter is null || _entry.Instance.Parameters[_grip.Parameter] is not double current || _map.PerMeter.Length < 1e-12)
            {
                return;
            }

            var dz = (moved - _original).DotProduct(along) / _map.PerMeter.Length;
            var value = DeviceEditing.GripValue(parameter, current, new Vec3(0, 0, dz), _grip);
            if (_value is { } previous && Math.Abs(previous - value) < 1e-9)
            {
                return;
            }

            _value = value;
            try
            {
                var values = DeviceEditing.Edit(_entry.Definition, _entry.Instance.Parameters, parameter.Name, value, out _);
                var instance = _entry.Definition.Evaluate(values, parameter.Name);
                var affine = _placement.ToAffine();
                _preview = ProfileProjection.Lines(instance.Solids.SelectMany(s => Wireframe.Of(s.Node, affine)), _frame);
                _text = $"{parameter.Label ?? parameter.Name} = {value.ToString("0.###", CultureInfo.InvariantCulture)}{(parameter.Unit.Length > 0 ? " " + parameter.Unit : "")}";
            }
            catch (HydroException ex)
            {
                _preview = null;
                _text = ex.Reason;
            }
        }

        public override void OnGripStatusChanged(ObjectId entityId, Status newStatus)
        {
            Guard.Run("Grips", () =>
            {
                if (newStatus == Status.GripEnd && _value is { } value)
                {
                    DeviceWatcher.QueueGrip(_device, _grip.Parameter, value);
                }

                if (newStatus is Status.GripEnd or Status.GripAbort)
                {
                    GripPoint = _original;
                    _value = null;
                    _preview = null;
                }
            });

            base.OnGripStatusChanged(entityId, newStatus);
        }

        public override bool WorldDraw(WorldDraw worldDraw, ObjectId entityId, DrawType type, Point3d? imageGripPoint, double dGripSize)
        {
            try
            {
                var at = imageGripPoint ?? GripPoint;
                if (worldDraw.IsDragging && _preview is not null)
                {
                    worldDraw.SubEntityTraits.FillType = FillType.FillNever;
                    foreach (var line in _preview)
                    {
                        worldDraw.SubEntityTraits.Color = line.Removed ? (short)8 : (short)2;
                        var points = new Point3dCollection(line.Points.Select(p => _map.At(p.Station, p.Elevation)).ToArray());
                        if (line.Closed && points.Count > 2)
                        {
                            points.Add(points[0]);
                        }

                        worldDraw.Geometry.Polyline(points, Vector3d.ZAxis, IntPtr.Zero);
                    }
                }

                if (worldDraw.IsDragging && _text.Length > 0)
                {
                    worldDraw.SubEntityTraits.Color = 2;
                    worldDraw.Geometry.Text(at + new Vector3d(dGripSize * 2, dGripSize * 2, 0), Vector3d.ZAxis, Vector3d.XAxis, dGripSize * 2.5, 1, 0, _text);
                }

                // losango do grip de perfil (como o GripPview do SOLIDOS)
                var diamond = new Point3dCollection(new[]
                {
                    at + new Vector3d(0, dGripSize, 0), at + new Vector3d(-dGripSize / 2, 0, 0), at + new Vector3d(0, -dGripSize, 0), at + new Vector3d(dGripSize / 2, 0, 0),
                });
                worldDraw.SubEntityTraits.FillType = FillType.FillAlways;
                worldDraw.SubEntityTraits.Color = 4;
                worldDraw.Geometry.Polygon(diamond);
                worldDraw.SubEntityTraits.FillType = FillType.FillNever;
                worldDraw.SubEntityTraits.Color = 7;
                worldDraw.Geometry.Polygon(diamond);
            }
            catch (Exception ex)
            {
                Guard.Report("Grips: desenho de perfil", ex);
            }

            return true;
        }

        public override string GetTooltip() => Guard.Run("Grips", () => $"Perfil: {_entry.Definition.FindParameter(_grip.Parameter)?.Label ?? _grip.Parameter}", "");
    }
}
