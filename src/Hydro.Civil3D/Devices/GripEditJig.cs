using System;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Hydro.Civil3D.Infra;
using Hydro.Core.Definition;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Hydro.Civil3D.Devices;

/// <summary>Arrasto do grip dentro do <c>HY_GRIP</c>: o mesmo cálculo e a mesma prévia em aramado do
/// estiramento nativo (<see cref="GripSession"/>), numa pergunta de ponto que também aceita texto
/// (<see cref="UserInputControls.AcceptOtherInputString"/>), para o valor digitado. Sem ponto base
/// de propósito: com ele, um número digitado viraria distância direta a partir do grip.</summary>
internal sealed class GripEditJig : DrawJig
{
    private readonly GripSession _session;
    private readonly string _prompt;
    private Point3d? _last;

    public GripEditJig(GripSession session, string prompt)
    {
        _session = session;
        _prompt = prompt;
    }

    // Sampler e desenho rodam dentro do Editor.Drag nativo: exceção ali derrubaria o AutoCAD
    protected override SamplerStatus Sampler(JigPrompts prompts) =>
        Infra.Guard.Run("Jig", () => Sample(prompts), SamplerStatus.Cancel);

    private SamplerStatus Sample(JigPrompts prompts)
    {
        var options = new JigPromptPointOptions($"\n{_prompt}: ")
        {
            UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted | UserInputControls.AcceptOtherInputString,
            UseBasePoint = false,
        };
        var result = prompts.AcquirePoint(options);
        if (result.Status == PromptStatus.Cancel)
        {
            return SamplerStatus.Cancel;
        }

        if (result.Status != PromptStatus.OK)
        {
            // texto digitado ou Enter: quem decide é o HY_GRIP, pelo resultado do Drag
            return SamplerStatus.OK;
        }

        if (_last is { } last && last.DistanceTo(result.Value) < 1e-9)
        {
            return SamplerStatus.NoChange;
        }

        _last = result.Value;
        _session.DragTo(result.Value);
        return SamplerStatus.OK;
    }

    protected override bool WorldDraw(WorldDraw draw) => Infra.Guard.Run("Jig", () => Draw(draw), true);

    private bool Draw(WorldDraw draw)
    {
        try
        {
            var size = GripSize();
            var at = _session.Current;
            _session.Draw(draw, at, size * 2.5);
            if (_session.Grip.Kind != GripKind.Rotacionar)
            {
                // linha elástica do grip original até a posição nova, como no estiramento nativo
                draw.SubEntityTraits.Color = 7;
                draw.Geometry.WorldLine(_session.Original, at);
            }

            GripGlyphs.Draw(draw, _session, at, size);
        }
        catch (Exception ex)
        {
            HydroLog.Warn("Grips", "desenho do HY_GRIP: " + ex.Message);
        }

        return true;
    }

    /// <summary>Tamanho do grip em unidades do desenho: GRIPSIZE (pixels) × VIEWSIZE ÷ altura da tela.</summary>
    private static double GripSize()
    {
        try
        {
            var pixels = Convert.ToDouble(AcApp.GetSystemVariable("GRIPSIZE"));
            var viewSize = Convert.ToDouble(AcApp.GetSystemVariable("VIEWSIZE"));
            var screen = AcApp.GetSystemVariable("SCREENSIZE") is Point2d s && s.Y > 0 ? s.Y : 800;
            return Math.Max(pixels, 1) * viewSize / screen;
        }
        catch (Exception)
        {
            return 0.1;
        }
    }
}
