using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Hydro.Civil3D.Infra;
using Hydro.Core.Definition;

namespace Hydro.Civil3D.Devices;

/// <summary>Acrescenta, aos grips normais do bloco, um grip por parâmetro com <c>grip:</c> na
/// definição, com o desenho dos grips do SOLIDOS (que imitam os do Civil 3D): esticar = seta verde
/// (ACI 3) na direção do parâmetro; rotacionar = círculo ciano (ACI 4) ligado ao centro; perfil =
/// losango ciano. Todos com contorno ACI 7 e tamanho pelo GRIPSIZE (o AutoCAD passa o tamanho já em
/// unidades do desenho). Arrastar mostra a prévia em aramado (avaliação do Core, sem ACIS); ao
/// soltar, a edição vai para a fila do <see cref="DeviceWatcher"/> e o dispositivo se refaz no fim do
/// comando — a geometria nunca é refeita dentro do overrule. Só se aplica a BlockReference com o
/// registro do HYDRO (filtro pelo dicionário de extensão).
/// <para>Com <see cref="GripCommands.TypedInput"/> ligado (o padrão), clicar o grip abre o
/// <c>HY_GRIP</c> em vez do estiramento nativo: o mesmo arrasto com prévia, mais o valor digitado na
/// linha de comando. Desligado, fica só o estiramento nativo.</para></summary>
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
            var typed = GripCommands.TypedInput;
            foreach (var grip in entry.Instance.Grips.Where(g => g.Kind != GripKind.Perfil))
            {
                var session = new GripSession(entry, grip, placement, reference.BlockTransform);
                grips.Add(typed ? new TypedDeviceGrip(reference.ObjectId, session) : new DeviceGrip(reference.ObjectId, session));
            }
        }
        catch (Exception ex)
        {
            // definição ausente ou inválida: ficam só os grips normais do bloco
            Guard.Report("Grips", ex);
        }
    }

    public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
    {
        var standard = new GripDataCollection();
        foreach (GripData grip in grips)
        {
            if (grip is DeviceGrip device)
            {
                Guard.Run("Grips", () => device.Drag(offset));
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

    /// <summary>Grip ligado a um parâmetro, com o estiramento nativo do AutoCAD.</summary>
    private class DeviceGrip : GripData
    {
        public DeviceGrip(ObjectId entity, GripSession session)
        {
            Entity = entity;
            Session = session;
            GripPoint = session.Original;
            DrawAtDragImageGripPoint = true;
            RubberBandLineDisabled = session.Grip.Kind == GripKind.Rotacionar;
        }

        protected ObjectId Entity { get; }

        protected GripSession Session { get; }

        /// <summary>Novo valor e prévia para o deslocamento do arrasto (chamado a cada movimento).</summary>
        public void Drag(Vector3d offset)
        {
            Session.DragTo(Session.Original + offset);
            GripPoint = Session.Current;
        }

        public override void OnGripStatusChanged(ObjectId entityId, Status newStatus)
        {
            Guard.Run("Grips", () =>
            {
                if (newStatus == Status.GripEnd && Session.Value is { } value)
                {
                    DeviceWatcher.QueueGrip(Entity, Session.Grip.Parameter, value);
                }

                if (newStatus is Status.GripEnd or Status.GripAbort)
                {
                    Session.Reset();
                    GripPoint = Session.Original;
                }
            });

            base.OnGripStatusChanged(entityId, newStatus);
        }

        public override bool WorldDraw(WorldDraw worldDraw, ObjectId entityId, DrawType type, Point3d? imageGripPoint, double dGripSize)
        {
            try
            {
                var at = imageGripPoint ?? GripPoint;
                if (worldDraw.IsDragging)
                {
                    Session.Draw(worldDraw, at, dGripSize * 2.5);
                }

                GripGlyphs.Draw(worldDraw, Session, at, dGripSize);
            }
            catch (Exception ex)
            {
                Guard.Report("Grips: desenho", ex);
            }

            return true;
        }

        public override string GetTooltip() => Guard.Run("Grips", () =>
            $"{(Session.Grip.Kind == GripKind.Rotacionar ? "Girar" : "Esticar")}: {Session.Parameter?.Label ?? Session.Grip.Parameter}", "");
    }

    /// <summary>Grip que, ao ser clicado, abre o <c>HY_GRIP</c> (arrastar ou digitar o valor) em vez
    /// do estiramento nativo. É um grip de disparo (<see cref="GripData.TriggerGrip"/>): o AutoCAD só
    /// chama <see cref="OnHotGrip"/>, e o grip volta a morno.</summary>
    private sealed class TypedDeviceGrip : DeviceGrip
    {
        public TypedDeviceGrip(ObjectId entity, GripSession session)
            : base(entity, session)
        {
            TriggerGrip = true;
        }

        public override ReturnValue OnHotGrip(ObjectId entityId, Context contextFlags)
        {
            Guard.Run("Grips", () => GripCommands.Start(Entity, Session.Grip.Parameter));
            return ReturnValue.GripHotToWarm;
        }

        public override string GetTooltip() => base.GetTooltip() + " (clique: arraste ou digite o valor)";
    }
}

/// <summary>Desenho dos grips do SOLIDOS (que imitam os do Civil 3D), usado pelos grips e pelo
/// <see cref="GripEditJig"/>: esticar = seta verde (ACI 3), rotacionar = círculo ciano (ACI 4) ligado
/// ao centro, perfil = losango ciano; contorno ACI 7.</summary>
internal static class GripGlyphs
{
    private const short Outline = 7;

    public static void Draw(WorldDraw wd, GripSession session, Point3d at, double size)
    {
        wd.SubEntityTraits.FillType = FillType.FillAlways;
        switch (session.Grip.Kind)
        {
            case GripKind.Rotacionar:
                DrawRotate(wd, session.Center, at, size);
                break;
            case GripKind.Perfil:
                DrawDiamond(wd, at, size);
                break;
            default:
                DrawArrow(wd, session.Direction, at, size);
                break;
        }
    }

    /// <summary>Seta (cabeça triangular + haste) ao longo da direção, com 2× o tamanho do grip;
    /// vista de cima, um grip vertical aparece também como círculo.</summary>
    private static void DrawArrow(WorldDraw wd, Vector3d x, Point3d at, double size)
    {
        size *= 2;
        var viewDir = ViewDirection();
        if (Math.Abs(x.DotProduct(viewDir)) > 0.999)
        {
            // seta vista de ponta (grip vertical visto de cima): círculo, como no SOLIDOS
            wd.SubEntityTraits.Color = 3;
            wd.Geometry.Circle(at, size * 0.75, viewDir);
            Outlined(wd, () => wd.Geometry.Circle(at, size * 0.75, viewDir));
            return;
        }

        // a seta fica no plano da tela que contém a direção
        var y = viewDir.CrossProduct(x).GetNormal();

        var shape = new[] { (0.0, 0.7), (0.8, 0.0), (0.0, -0.7), (0.0, -0.3), (-0.5, -0.3), (-0.5, 0.3), (0.0, 0.3) };
        var points = new Point3dCollection(shape.Select(p => at + (x * (p.Item1 * size)) + (y * (p.Item2 * size))).ToArray());
        wd.SubEntityTraits.Color = 3;
        wd.Geometry.Polygon(points);
        Outlined(wd, () => wd.Geometry.Polygon(points));
    }

    /// <summary>Linha do centro ao grip e círculo cheio de raio 1,2× com contorno de raio 1×.</summary>
    private static void DrawRotate(WorldDraw wd, Point3d center, Point3d at, double size)
    {
        wd.SubEntityTraits.Color = 4;
        wd.Geometry.WorldLine(center, at);
        wd.Geometry.Circle(at, size * 1.2, Vector3d.ZAxis);
        Outlined(wd, () => wd.Geometry.Circle(at, size, Vector3d.ZAxis));
    }

    private static void DrawDiamond(WorldDraw wd, Point3d at, double size)
    {
        var points = new Point3dCollection(new[]
        {
            at + new Vector3d(0, size, 0), at + new Vector3d(-size / 2, 0, 0), at + new Vector3d(0, -size, 0), at + new Vector3d(size / 2, 0, 0),
        });
        wd.SubEntityTraits.Color = 4;
        wd.Geometry.Polygon(points);
        Outlined(wd, () => wd.Geometry.Polygon(points));
    }

    private static void Outlined(WorldDraw wd, Action draw)
    {
        wd.SubEntityTraits.FillType = FillType.FillNever;
        wd.SubEntityTraits.Color = Outline;
        draw();
        wd.SubEntityTraits.FillType = FillType.FillAlways;
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
