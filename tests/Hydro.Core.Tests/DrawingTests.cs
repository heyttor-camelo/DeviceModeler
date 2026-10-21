using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Drawing;
using Hydro.Core.Geometry;
using Hydro.Core.Text;

namespace Hydro.Core.Tests;

/// <summary>Desenho (F4): estilos casados pelo nome do sólido, estilo de projeção e projeção na vista
/// de perfil (estaca, offset, elevação, lado, distância máxima, silhueta).</summary>
public class DrawingTests
{
    [Fact]
    public void Estilo_casa_pelo_nome_e_tem_generico()
    {
        var style = new DeviceStyle
        {
            Name = "PV prancha",
            Components =
            {
                new StyleComponent { Name = "Concreto", Layer = "C-PV", Color = 8 },
                new StyleComponent { Name = "degraus", Visible = false },
                new StyleComponent { Name = "*", Layer = "C-OUTROS" },
            },
        };
        Assert.Equal("C-PV", style.Resolve("concreto")!.Layer);
        Assert.False(style.Resolve("degraus")!.Visible);
        Assert.Equal("C-OUTROS", style.Resolve("tampa")!.Layer);

        var back = DeviceStyle.FromJson(style.ToJson());
        Assert.Equal(3, back.Components.Count);
        Assert.Equal((short)8, back.Resolve("concreto")!.Color);
        Assert.Null(new DeviceStyle().Resolve("x"));
    }

    [Fact]
    public void Estilo_de_projecao_completa_componentes_que_faltam()
    {
        var style = new ProjectionStyle { Name = "Só corte", Components = { } };
        style.Components.Clear();
        style.Components.Add(new StyleComponent { Name = "corte", Color = 2 });
        style.Hatch = new HatchSettings { Pattern = "AR-CONC", Scale = 0.02 };
        style.Side = ProjectionSide.Direito;
        var back = ProjectionStyle.FromJson(style.ToJson());
        Assert.Equal(ProjectionStyle.ComponentNames.Count, back.Components.Count);
        Assert.Equal((short)2, back.Component("corte").Color);
        Assert.Equal("AR-CONC", back.Hatch.Pattern);
        Assert.Equal(ProjectionSide.Direito, back.Side);
        Assert.Contains("\"Direito\"", style.ToJson());
    }

    // alinhamento no eixo X, estaca 100 na origem
    private static readonly AlignmentFrame Frame = new(Vec3.Zero, Vec3.UnitX, 100);

    [Fact]
    public void Mapeia_estaca_offset_e_elevacao()
    {
        var (s, o, z) = Frame.Map(new Vec3(5, -2, 98.5));
        Assert.Equal(105, s, 9);
        Assert.Equal(2, o, 9);   // y negativo = à direita de um eixo que vai para +X
        Assert.Equal(98.5, z, 9);
        Assert.Equal(-1, Frame.CutPlane.Normal.Y, 9);
    }

    [Fact]
    public void Visibilidade_por_lado_e_distancia()
    {
        Assert.True(ProfileProjection.Visible(-3, 0.75, ProjectionSide.Esquerdo, 10));
        Assert.False(ProfileProjection.Visible(3, 0.75, ProjectionSide.Esquerdo, 10));
        Assert.True(ProfileProjection.Visible(0.5, 0.75, ProjectionSide.Esquerdo, 10));   // cruza o eixo: em corte
        Assert.False(ProfileProjection.Visible(12, 0.75, ProjectionSide.Ambos, 10));
        Assert.True(ProfileProjection.Visible(10.5, 0.75, ProjectionSide.Ambos, 10));
    }

    [Fact]
    public void Pv_projetado_tem_silhueta_da_largura_e_altura()
    {
        var pv = DeviceDefinition.Load(Path.Combine("catalog", "exemplos", "pv_retangular.yaml"));
        var inst = pv.Evaluate();
        var placement = new Placement(new Vec3(20, 1, 0), 0);
        var lines = inst.Solids.Where(s => s.Name == "concreto").SelectMany(s => Wireframe.Of(s.Node, placement.ToAffine())).ToList();
        var outline = ProfileProjection.Outline(lines, Frame);
        Assert.Equal(120 - 0.75, outline.Min(p => p.Station), 9);
        Assert.Equal(120 + 0.75, outline.Max(p => p.Station), 9);
        Assert.Equal(99.85, outline.Min(p => p.Elevation), 9);
        Assert.Equal(101.5, outline.Max(p => p.Elevation), 9);
        Assert.Equal(0.75, ProfileProjection.HalfWidth(lines, Frame, -1), 9);

        var right = ProfileProjection.Lines(lines, Frame, ProjectionSide.Direito);
        var left = ProfileProjection.Lines(lines, Frame, ProjectionSide.Esquerdo);
        Assert.True(left.Count > right.Count);   // o PV está à esquerda (y = 1), só a borda passa de y < 0,25
    }

    [Fact]
    public void Modelo_de_rotulo_com_extras_da_instancia()
    {
        var pv = DeviceDefinition.Load(Path.Combine("catalog", "exemplos", "pv_retangular.yaml"));
        var text = pv.Evaluate().Render(Template.Parse("{nome} — {codigo}\nCT {elev_tampa:m:3}\nH {H:m:2}"),
            new Dictionary<string, object> { ["nome"] = "PV-7", ["elev_tampa"] = 101.5 });
        Assert.Equal("PV-7 — PV-120x120\nCT 101,500 m\nH 1,50 m", text);
    }
}
