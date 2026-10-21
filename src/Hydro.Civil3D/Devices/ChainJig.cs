using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Hydro.Civil3D.Geometry;
using Hydro.Core.Csg;

namespace Hydro.Civil3D.Devices;

/// <summary>Jig do lançamento em cadeia (técnica 11 do estudo E): mostra o dispositivo em aramado no
/// cursor (geometria real avaliada pelo Core), girado na direção do trecho, e o trecho de tubo desde o
/// dispositivo anterior. <c>DrawJig</c>, e não <c>EntityJig</c>, porque a prévia é um conjunto de
/// linhas, não uma entidade.</summary>
internal sealed class ChainJig : DrawJig
{
    private readonly IReadOnlyList<WireLine> _lines;
    private readonly Point3d? _from;
    private readonly string _prompt;
    private Point3d _position;

    /// <param name="lines">aramado do dispositivo no sistema local (origem no ponto de inserção).</param>
    /// <param name="from">ponto do dispositivo anterior (nulo no primeiro).</param>
    public ChainJig(IReadOnlyList<WireLine> lines, Point3d? from, string prompt)
    {
        _lines = lines;
        _from = from;
        _prompt = prompt;
        _position = from ?? Point3d.Origin;
    }

    public Point3d Position => _position;

    /// <summary>Rotação em planta (rad): a direção do trecho desde o anterior; 0 no primeiro.</summary>
    public double Rotation => _from is { } from && from.DistanceTo(new Point3d(_position.X, _position.Y, from.Z)) > 1e-9
        ? Math.Atan2(_position.Y - from.Y, _position.X - from.X)
        : 0;

    public string? Keyword { get; private set; }

    public PromptStatus Run(Editor editor)
    {
        Keyword = null;
        var result = editor.Drag(this);
        if (result.Status == PromptStatus.Keyword)
        {
            Keyword = result.StringResult;
        }

        return result.Status;
    }

    protected override SamplerStatus Sampler(JigPrompts prompts)
    {
        var options = new JigPromptPointOptions(_prompt)
        {
            UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted,
        };
        options.Keywords.Add("Desfazer");
        options.Keywords.Add("Sair");
        if (_from is { } from)
        {
            options.BasePoint = from;
            options.UseBasePoint = true;
            options.Cursor = CursorType.RubberBand;
        }

        var result = prompts.AcquirePoint(options);
        if (result.Status != PromptStatus.OK)
        {
            return result.Status == PromptStatus.Keyword || result.Status == PromptStatus.None ? SamplerStatus.OK : SamplerStatus.Cancel;
        }

        var flat = new Point3d(result.Value.X, result.Value.Y, 0);
        if (flat.DistanceTo(_position) < 1e-9)
        {
            return SamplerStatus.NoChange;
        }

        _position = flat;
        return SamplerStatus.OK;
    }

    protected override bool WorldDraw(WorldDraw draw)
    {
        var rotation = Matrix3d.Rotation(Rotation, Vector3d.ZAxis, Point3d.Origin);
        var move = Matrix3d.Displacement(new Vector3d(_position.X, _position.Y, 0));
        var transform = move * rotation;
        draw.SubEntityTraits.FillType = FillType.FillNever;
        foreach (var line in _lines)
        {
            draw.SubEntityTraits.Color = line.Removed ? (short)8 : (short)2;
            var points = new Point3dCollection(line.Points.Select(p => Solid3dBuilder.ToPoint(p).TransformBy(transform)).ToArray());
            if (line.Closed && points.Count > 2)
            {
                points.Add(points[0]);
            }

            draw.Geometry.Polyline(points, Vector3d.ZAxis, IntPtr.Zero);
        }

        if (_from is { } from)
        {
            draw.SubEntityTraits.Color = 4;
            draw.Geometry.WorldLine(new Point3d(from.X, from.Y, 0), _position);
        }

        return true;
    }
}
