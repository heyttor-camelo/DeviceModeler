using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace Hydro.Civil3D.Devices;

/// <summary>Arrasta o dispositivo (referência do bloco já gerado) para escolher o ponto de
/// inserção e, em seguida, a rotação em planta.</summary>
internal sealed class PlacementJig : EntityJig
{
    private Point3d _position;
    private double _rotation;
    private bool _rotating;

    public PlacementJig(BlockReference reference)
        : base(reference)
    {
        _position = reference.Position;
    }

    public Point3d Position => _position;

    public double Rotation => _rotation;

    /// <summary>Pede ponto e rotação; falso se o usuário cancelar.</summary>
    public bool Run(Editor editor)
    {
        if (editor.Drag(this).Status != PromptStatus.OK)
        {
            return false;
        }

        _rotating = true;
        var result = editor.Drag(this);
        return result.Status is PromptStatus.OK or PromptStatus.None;
    }

    protected override SamplerStatus Sampler(JigPrompts prompts)
    {
        if (!_rotating)
        {
            var options = new JigPromptPointOptions("\nPonto de inserção: ") { UserInputControls = UserInputControls.Accept3dCoordinates };
            var point = prompts.AcquirePoint(options);
            if (point.Status != PromptStatus.OK)
            {
                return SamplerStatus.Cancel;
            }

            var flat = new Point3d(point.Value.X, point.Value.Y, 0);
            if (flat.DistanceTo(_position) < 1e-9)
            {
                return SamplerStatus.NoChange;
            }

            _position = flat;
            return SamplerStatus.OK;
        }

        var angleOptions = new JigPromptAngleOptions("\nRotação <0>: ")
        {
            BasePoint = _position,
            UseBasePoint = true,
            UserInputControls = UserInputControls.NullResponseAccepted,
        };
        var angle = prompts.AcquireAngle(angleOptions);
        if (angle.Status == PromptStatus.None)
        {
            return SamplerStatus.NoChange;
        }

        if (angle.Status != PromptStatus.OK)
        {
            return SamplerStatus.Cancel;
        }

        if (System.Math.Abs(angle.Value - _rotation) < 1e-9)
        {
            return SamplerStatus.NoChange;
        }

        _rotation = angle.Value;
        return SamplerStatus.OK;
    }

    protected override bool Update()
    {
        var reference = (BlockReference)Entity;
        reference.Position = _position;
        reference.Rotation = _rotation;
        return true;
    }
}
