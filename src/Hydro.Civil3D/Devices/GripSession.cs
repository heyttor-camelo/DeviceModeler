using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Hydro.Civil3D.Geometry;
using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Editing;
using Hydro.Core.Errors;
using Hydro.Core.Results;
using CorePlacement = Hydro.Core.Geometry.Placement;

namespace Hydro.Civil3D.Devices;

/// <summary>Uma edição pelo grip em andamento, comum ao arrasto nativo (<see cref="DeviceGripOverrule"/>)
/// e ao <c>HY_GRIP</c> (<see cref="GripEditJig"/>): o valor novo do parâmetro, a posição do grip e a
/// prévia em aramado (Core, sem ACIS). Não mexe no desenho.</summary>
internal sealed class GripSession
{
    private readonly InstanceCache.Entry _entry;
    private readonly CorePlacement _placement;

    public GripSession(InstanceCache.Entry entry, GripInstance grip, CorePlacement placement, Matrix3d toWorld)
    {
        _entry = entry;
        _placement = placement;
        Grip = grip;
        Parameter = entry.Definition.FindParameter(grip.Parameter);
        Original = Solid3dBuilder.ToPoint(grip.Position).TransformBy(toWorld);
        Direction = Solid3dBuilder.ToVector(grip.Direction).TransformBy(toWorld).GetNormal();
        Center = Solid3dBuilder.ToPoint(grip.Center).TransformBy(toWorld);
        Current = Original;
    }

    public GripInstance Grip { get; }

    public ParameterDef? Parameter { get; }

    public Point3d Original { get; }

    public Vector3d Direction { get; }

    public Point3d Center { get; }

    /// <summary>Posição do grip no arrasto (o grip de esticar anda só na direção do parâmetro).</summary>
    public Point3d Current { get; private set; }

    /// <summary>Valor do parâmetro antes da edição; nulo se não for numérico.</summary>
    public double? CurrentValue => _entry.Instance.Parameters.GetValueOrDefault(Grip.Parameter) as double?;

    /// <summary>Valor novo aceito pela definição; nulo enquanto não houver um.</summary>
    public double? Value { get; private set; }

    /// <summary>"L = 1,50 m", ou o motivo da recusa da definição.</summary>
    public string Text { get; private set; } = "";

    public IReadOnlyList<WireLine>? Preview { get; private set; }

    /// <summary>Valor e prévia para o grip arrastado até <paramref name="moved"/>.</summary>
    public void DragTo(Point3d moved)
    {
        if (Grip.Kind == GripKind.Esticar)
        {
            moved = Original + (Direction * (moved - Original).DotProduct(Direction));
        }

        Current = moved;
        if (Parameter is null || CurrentValue is not { } current)
        {
            return;
        }

        var localDelta = _placement.DirectionToLocal(Solid3dBuilder.ToVec3(Point3d.Origin + (moved - Original)));
        SetValue(DeviceEditing.GripValue(Parameter, current, localDelta, Grip));
    }

    /// <summary>Valor dado diretamente (digitado); falso se a definição o recusa (o motivo fica em <see cref="Text"/>).</summary>
    public bool SetValue(double value)
    {
        if (Parameter is null)
        {
            return false;
        }

        if (Value is { } previous && Math.Abs(previous - value) < 1e-9)
        {
            return true;
        }

        try
        {
            var values = DeviceEditing.Edit(_entry.Definition, _entry.Instance.Parameters, Parameter.Name, value, out _);
            var instance = _entry.Definition.Evaluate(values, Parameter.Name);
            var affine = _placement.ToAffine();
            Preview = instance.Solids.SelectMany(s => Wireframe.Of(s.Node, affine)).ToList();
            Value = value;
            Text = $"{Parameter.Label ?? Parameter.Name} = {DeviceEditing.FormatGripValue(Parameter, value)}";
            return true;
        }
        catch (HydroException ex)
        {
            // valor que a definição recusa (validação): mostra o motivo e não deixa prévia
            Value = null;
            Preview = null;
            Text = ex.Reason;
            return false;
        }
    }

    public void Reset()
    {
        Current = Original;
        Value = null;
        Preview = null;
        Text = "";
    }

    /// <summary>Aramado do dispositivo com o valor novo (peças em amarelo, furos em cinza) e o texto
    /// do valor ao lado de <paramref name="at"/>.</summary>
    public void Draw(WorldDraw wd, Point3d at, double textHeight)
    {
        wd.SubEntityTraits.FillType = FillType.FillNever;
        foreach (var line in Preview ?? Array.Empty<WireLine>())
        {
            wd.SubEntityTraits.Color = line.Removed ? (short)8 : (short)2;
            var points = new Point3dCollection(line.Points.Select(Solid3dBuilder.ToPoint).ToArray());
            if (line.Closed && points.Count > 2)
            {
                points.Add(points[0]);
            }

            wd.Geometry.Polyline(points, Vector3d.ZAxis, IntPtr.Zero);
        }

        if (Text.Length > 0)
        {
            wd.SubEntityTraits.Color = 2;
            wd.Geometry.Text(at + new Vector3d(textHeight * 0.8, textHeight * 0.8, 0), Vector3d.ZAxis, Vector3d.XAxis, textHeight, 1, 0, Text);
        }
    }
}
