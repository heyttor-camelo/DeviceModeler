using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Infra;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using Modeler.Core.Results;
using CorePlacement = Modeler.Core.Geometry.Placement;
using GiPolyline = Autodesk.AutoCAD.GraphicsInterface.Polyline;

namespace Modeler.Civil3D.Devices;

/// <summary>Acrescenta, aos grips normais do bloco, um grip por parâmetro com <c>grip:</c> na
/// definição, com o desenho dos grips do SOLIDOS (que imitam os do Civil 3D): esticar = seta verde
/// (ACI 3) na direção do parâmetro; rotacionar = círculo ciano (ACI 4) ligado ao centro; perfil =
/// losango ciano. Todos com contorno ACI 7 e tamanho pelo GRIPSIZE (o AutoCAD passa o tamanho já em
/// unidades do desenho). Arrastar mostra a prévia em aramado (avaliação do Core, sem ACIS); ao
/// soltar, a edição vai para a fila do <see cref="DeviceWatcher"/> e o dispositivo se refaz no fim do
/// comando — a geometria nunca é refeita dentro do overrule. Só se aplica a BlockReference com o
/// registro do DeviceModeler (filtro pelo dicionário de extensão).</summary>
internal sealed class DeviceGripOverrule : GripOverrule
{
    private static DeviceGripOverrule? _instance;

    public static void Register()
    {
        if (_instance is not null)
        {
            return;
        }

        _instance = new DeviceGripOverrule();
        _instance.SetExtensionDictionaryEntryFilter(Persistence.DeviceRecord.Key);
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
        base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
        if (entity is not BlockReference reference || reference.Database is null || reference.ObjectId.IsNull)
        {
            return;
        }

        try
        {
            using var tr = reference.Database.TransactionManager.StartOpenCloseTransaction();
            var entry = InstanceCache.Get(tr, reference);
            tr.Commit();
            if (entry is null)
            {
                return;
            }

            var placement = DeviceService.PlacementOf(reference);
            foreach (var grip in entry.Instance.Grips.Where(g => g.Kind != GripKind.Perfil))
            {
                grips.Add(new DeviceGrip(reference.ObjectId, entry, grip, placement, reference.BlockTransform));
            }
        }
        catch (Exception ex)
        {
            // definição ausente ou inválida: ficam só os grips normais do bloco
            HydroLog.Warn("Grips", ex.Message);
        }
    }

    public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
    {
        var standard = new GripDataCollection();
        foreach (GripData grip in grips)
        {
            if (grip is DeviceGrip device)
            {
                device.Drag(offset);
            }
            else
            {
                standard.Add(grip);
            }
        }

        if (standard.Count > 0)
        {
            base.MoveGripPointsAt(entity, standard, offset, bitFlags);
        }
    }

    /// <summary>Grip ligado a um parâmetro.</summary>
    private sealed class DeviceGrip : GripData
    {
        private const short GlyphOutline = 7;

        private readonly ObjectId _entity;
        private readonly InstanceCache.Entry _entry;
        private readonly GripInstance _grip;
        private readonly CorePlacement _placement;
        private readonly Matrix3d _toWorld;
        private readonly Point3d _original;
        private readonly Vector3d _direction;
        private readonly Point3d _center;
        private IReadOnlyList<WireLine>? _preview;
        private double? _value;
        private string _text = "";

        public DeviceGrip(ObjectId entity, InstanceCache.Entry entry, GripInstance grip, CorePlacement placement, Matrix3d toWorld)
        {
            _entity = entity;
            _entry = entry;
            _grip = grip;
            _placement = placement;
            _toWorld = toWorld;
            _original = Solid3dBuilder.ToPoint(grip.Position).TransformBy(toWorld);
            _direction = Solid3dBuilder.ToVector(grip.Direction).TransformBy(toWorld).GetNormal();
            _center = Solid3dBuilder.ToPoint(grip.Center).TransformBy(toWorld);
            GripPoint = _original;
            DrawAtDragImageGripPoint = true;
            RubberBandLineDisabled = grip.Kind == GripKind.Rotacionar;
        }

        /// <summary>Novo valor e prévia para o deslocamento do arrasto (chamado a cada movimento).</summary>
        public void Drag(Vector3d offset)
        {
            var moved = _original + offset;
            if (_grip.Kind == GripKind.Esticar)
            {
                // o grip de esticar anda só na direção do parâmetro
                moved = _original + (_direction * offset.DotProduct(_direction));
            }

            GripPoint = moved;
            var parameter = _entry.Definition.FindParameter(_grip.Parameter);
            if (parameter is null || _entry.Instance.Parameters[_grip.Parameter] is not double current)
            {
                return;
            }

            var localDelta = _placement.DirectionToLocal(Solid3dBuilder.ToVec3(Point3d.Origin + (moved - _original)));
            var value = DeviceEditing.GripValue(parameter, current, localDelta, _grip);
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
                _preview = instance.Solids.SelectMany(s => Wireframe.Of(s.Node, affine)).ToList();
                _text = $"{parameter.Label ?? parameter.Name} = {value.ToString("0.###", CultureInfo.InvariantCulture)}{(parameter.Unit.Length > 0 ? " " + parameter.Unit : "")}";
            }
            catch (ModelerException ex)
            {
                // valor que a definição recusa (validação): mostra o motivo e não deixa prévia
                _preview = null;
                _text = ex.Reason;
            }
        }

        public override void OnGripStatusChanged(ObjectId entityId, Status newStatus)
        {
            switch (newStatus)
            {
                case Status.GripEnd when _value is { } value:
                    DeviceWatcher.QueueGrip(_entity, _grip.Parameter, value);
                    break;
                case Status.GripAbort:
                    GripPoint = _original;
                    break;
            }

            if (newStatus is Status.GripEnd or Status.GripAbort)
            {
                _value = null;
                _preview = null;
            }

            base.OnGripStatusChanged(entityId, newStatus);
        }

        public override bool WorldDraw(WorldDraw worldDraw, ObjectId entityId, DrawType type, Point3d? imageGripPoint, double dGripSize)
        {
            try
            {
                var at = imageGripPoint ?? GripPoint;
                if (worldDraw.IsDragging && _preview is not null)
                {
                    DrawPreview(worldDraw);
                }

                if (worldDraw.IsDragging && _text.Length > 0)
                {
                    worldDraw.SubEntityTraits.Color = 2;
                    worldDraw.Geometry.Text(at + new Vector3d(dGripSize * 2, dGripSize * 2, 0), Vector3d.ZAxis, Vector3d.XAxis, dGripSize * 2.5, 1, 0, _text);
                }

                worldDraw.SubEntityTraits.FillType = FillType.FillAlways;
                switch (_grip.Kind)
                {
                    case GripKind.Rotacionar:
                        DrawRotate(worldDraw, at, dGripSize);
                        break;
                    case GripKind.Perfil:
                        DrawDiamond(worldDraw, at, dGripSize);
                        break;
                    default:
                        DrawArrow(worldDraw, at, dGripSize);
                        break;
                }
            }
            catch (Exception ex)
            {
                HydroLog.Warn("Grips", "desenho do grip: " + ex.Message);
            }

            return true;
        }

        public override string GetTooltip()
        {
            var parameter = _entry.Definition.FindParameter(_grip.Parameter);
            var verb = _grip.Kind == GripKind.Rotacionar ? "Girar" : "Esticar";
            return $"{verb}: {parameter?.Label ?? _grip.Parameter}";
        }

        /// <summary>Seta (cabeça triangular + haste) ao longo da direção, com 2× o tamanho do grip;
        /// vista de cima, um grip vertical aparece também como círculo.</summary>
        private void DrawArrow(WorldDraw wd, Point3d at, double size)
        {
            size *= 2;
            var x = _direction;
            var viewDir = ViewDirection();
            if (Math.Abs(x.DotProduct(viewDir)) > 0.999)
            {
                // seta vista de ponta (grip vertical visto de cima): círculo, como no SOLIDOS
                wd.SubEntityTraits.Color = 3;
                wd.Geometry.Circle(at, size * 0.75, viewDir);
                Outline(wd, () => wd.Geometry.Circle(at, size * 0.75, viewDir));
                return;
            }

            // a seta fica no plano da tela que contém a direção
            var y = viewDir.CrossProduct(x).GetNormal();

            var shape = new[] { (0.0, 0.7), (0.8, 0.0), (0.0, -0.7), (0.0, -0.3), (-0.5, -0.3), (-0.5, 0.3), (0.0, 0.3) };
            var points = new Point3dCollection(shape.Select(p => at + (x * (p.Item1 * size)) + (y * (p.Item2 * size))).ToArray());
            wd.SubEntityTraits.Color = 3;
            wd.Geometry.Polygon(points);
            Outline(wd, () => wd.Geometry.Polygon(points));
        }

        /// <summary>Linha do centro ao grip e círculo cheio de raio 1,2× com contorno de raio 1×.</summary>
        private void DrawRotate(WorldDraw wd, Point3d at, double size)
        {
            wd.SubEntityTraits.Color = 4;
            wd.Geometry.WorldLine(_center, at);
            wd.Geometry.Circle(at, size * 1.2, Vector3d.ZAxis);
            Outline(wd, () => wd.Geometry.Circle(at, size, Vector3d.ZAxis));
        }

        private static void DrawDiamond(WorldDraw wd, Point3d at, double size)
        {
            var points = new Point3dCollection(new[]
            {
                at + new Vector3d(0, size, 0), at + new Vector3d(-size / 2, 0, 0), at + new Vector3d(0, -size, 0), at + new Vector3d(size / 2, 0, 0),
            });
            wd.SubEntityTraits.Color = 4;
            wd.Geometry.Polygon(points);
            Outline(wd, () => wd.Geometry.Polygon(points));
        }

        private static void Outline(WorldDraw wd, Action draw)
        {
            wd.SubEntityTraits.FillType = FillType.FillNever;
            wd.SubEntityTraits.Color = GlyphOutline;
            draw();
            wd.SubEntityTraits.FillType = FillType.FillAlways;
        }

        /// <summary>Aramado do dispositivo com o valor novo: peças em amarelo, furos em cinza.</summary>
        private void DrawPreview(WorldDraw wd)
        {
            wd.SubEntityTraits.FillType = FillType.FillNever;
            foreach (var line in _preview!)
            {
                wd.SubEntityTraits.Color = line.Removed ? (short)8 : (short)2;
                var points = new Point3dCollection(line.Points.Select(Solid3dBuilder.ToPoint).ToArray());
                if (line.Closed && points.Count > 2)
                {
                    points.Add(points[0]);
                }

                wd.Geometry.Polyline(points, Vector3d.ZAxis, IntPtr.Zero);
            }
        }

        private static Vector3d ViewDirection()
        {
            try
            {
                var value = Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("VIEWDIR");
                return value is Point3d p && p.GetAsVector().Length > 1e-12 ? p.GetAsVector().GetNormal() : Vector3d.ZAxis;
            }
            catch (Exception)
            {
                return Vector3d.ZAxis;
            }
        }
    }
}
