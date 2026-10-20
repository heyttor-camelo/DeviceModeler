using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Csg;
using Modeler.Core.Results;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Modeler.Civil3D.Devices;

/// <summary>Acrescenta, aos grips normais do bloco, um grip por parâmetro com <c>grip:</c> na
/// definição. Só se aplica a BlockReference com o registro do DeviceModeler (filtro pelo
/// dicionário de extensão). Clicar num desses grips inicia o comando DM_GRIP, que pede o novo
/// ponto e regenera o dispositivo — a geometria nunca é refeita dentro do overrule.</summary>
internal sealed class DeviceGripOverrule : GripOverrule
{
    private static DeviceGripOverrule? _instance;

    /// <summary>Grip clicado que o DM_GRIP deve tratar.</summary>
    internal static (ObjectId Entity, string Parameter, Point3d Point)? Pending { get; set; }

    public static void Register()
    {
        if (_instance is not null)
        {
            return;
        }

        _instance = new DeviceGripOverrule();
        _instance.SetExtensionDictionaryEntryFilter(DeviceRecord.Key);
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
            foreach (var grip in GripsOf(reference))
            {
                grips.Add(new DeviceGrip(reference.ObjectId, grip.Parameter,
                    Solid3dBuilder.ToPoint(grip.Position).TransformBy(reference.BlockTransform)));
            }
        }
        catch (Exception)
        {
            // definição ausente ou inválida: ficam só os grips normais do bloco
        }
    }

    public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
    {
        var standard = new GripDataCollection();
        foreach (GripData grip in grips)
        {
            if (grip is not DeviceGrip)
            {
                standard.Add(grip);
            }
        }

        if (standard.Count > 0)
        {
            base.MoveGripPointsAt(entity, standard, offset, bitFlags);
        }
    }

    private static IReadOnlyList<GripInstance> GripsOf(BlockReference reference)
    {
        using var tr = reference.Database.TransactionManager.StartOpenCloseTransaction();
        var (_, _, instance) = DeviceService.Load(tr, reference);
        tr.Commit();
        return instance.Grips;
    }

    /// <summary>Grip ligado a um parâmetro.</summary>
    private sealed class DeviceGrip : GripData
    {
        private readonly ObjectId _entity;
        private readonly string _parameter;

        public DeviceGrip(ObjectId entity, string parameter, Point3d point)
        {
            _entity = entity;
            _parameter = parameter;
            GripPoint = point;
        }

        public override ReturnValue OnHotGrip(ObjectId entityId, Context contextFlags)
        {
            Pending = (_entity, _parameter, GripPoint);
            var document = AcApp.DocumentManager.MdiActiveDocument;
            document?.SendStringToExecute("_.DM_GRIP ", true, false, false);
            return ReturnValue.Ok;
        }

        public override string GetTooltip() => $"Alterar {_parameter}";
    }
}
