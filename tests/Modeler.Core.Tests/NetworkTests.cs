using Modeler.Core.Csg;
using Modeler.Core.Network;
using Modeler.Core.Results;

namespace Modeler.Core.Tests;

/// <summary>Rede (F3): casamento de conectores com filtro, direção e modos de ponta; grafo de fluxo,
/// ciclos e numeração.</summary>
public class NetworkTests
{
    private static readonly Connector Montante = new("montante", new Vec3(-1, 0, 100.5), new Vec3(-1, 0, 0), 0.6, "tubo");
    private static readonly Connector Jusante = new("jusante", new Vec3(1, 0, 100.3), new Vec3(1, 0, 0), 0.6, "tubo");
    private static readonly Connector Valeta = new("valeta", new Vec3(0, 1, 101), new Vec3(0, 1, 0), 0.5, "valeta, descida");

    private static readonly Connector[] All = { Montante, Jusante, Valeta };

    [Fact]
    public void Filtro_aceita_lista_sem_maiusculas()
    {
        Assert.True(ConnectorMatch.Accepts(Valeta, "Descida"));
        Assert.True(ConnectorMatch.Accepts(Valeta, "valeta"));
        Assert.False(ConnectorMatch.Accepts(Valeta, "tubo"));
        Assert.True(ConnectorMatch.Accepts(Montante with { Filter = null }, "qualquer"));
    }

    [Fact]
    public void Direcao_impede_tubo_pelo_lado_de_dentro()
    {
        Assert.True(ConnectorMatch.Faces(Jusante, new Vec3(10, 2, 99)));
        Assert.False(ConnectorMatch.Faces(Jusante, new Vec3(-10, 0, 99)));
    }

    [Fact]
    public void Ajustar_pega_o_mais_proximo_aceito()
    {
        // tubo vindo de +X: só o conector de jusante aceita a direção
        var r = ConnectorMatch.Choose(All, new Vec3(0.8, 0.1, 100), new Vec3(20, 0, 99.9), "tubo", ConnectorChoice.AjustarPosicao, EndElevation.Corrigir);
        Assert.Equal("jusante", r!.Connector.Name);
        Assert.Equal(new Vec3(1, 0, 100.3), r.End);
    }

    [Fact]
    public void Manter_elevacao_so_move_em_planta()
    {
        var r = ConnectorMatch.Choose(All, new Vec3(0.8, 0.1, 99.7), new Vec3(20, 0, 99.9), "tubo", ConnectorChoice.AjustarPosicao, EndElevation.Manter);
        Assert.Equal(new Vec3(1, 0, 99.7), r!.End);
    }

    [Fact]
    public void Melhor_conector_minimiza_o_comprimento()
    {
        var both = new[] { Montante with { Direction = Vec3.Zero }, Jusante with { Direction = Vec3.Zero } };
        var r3 = ConnectorMatch.Choose(both, new Vec3(1, 0, 100), new Vec3(-30, 0, 120), "tubo", ConnectorChoice.Melhor3D, EndElevation.Corrigir);
        Assert.Equal("montante", r3!.Connector.Name);
        var fixo = ConnectorMatch.Choose(both, new Vec3(-1, 0, 100), new Vec3(-30, 0, 120), "tubo", ConnectorChoice.Fixo, EndElevation.Corrigir, current: "jusante");
        Assert.Equal("jusante", fixo!.Connector.Name);
        Assert.Null(ConnectorMatch.Choose(All, Vec3.Zero, new Vec3(0, 10, 0), "esgoto", ConnectorChoice.AjustarPosicao, EndElevation.Corrigir));
    }

    [Theory]
    [InlineData("ajustar_corrigir", ConnectorChoice.AjustarPosicao, EndElevation.Corrigir)]
    [InlineData("melhor3d_manter", ConnectorChoice.Melhor3D, EndElevation.Manter)]
    [InlineData("fixo", ConnectorChoice.Fixo, EndElevation.Corrigir)]
    public void Modo_em_texto(string text, ConnectorChoice choice, EndElevation elevation)
    {
        Assert.Equal((choice, elevation), ConnectorMatch.ParseMode(text));
        Assert.Equal((choice, elevation), ConnectorMatch.ParseMode(ConnectorMatch.FormatMode(choice, elevation)));
    }

    // rede em Y: A → C, B → C, C → D (descarga); A tem o ramal mais longo
    private static FlowGraph Y() => new(
        new[] { new FlowNode("a", "A", 0, 0), new FlowNode("b", "B", 0, 10), new FlowNode("c", "C", 10, 5), new FlowNode("d", "D", 20, 5) },
        new[]
        {
            new FlowEdge("p1", "c", "a", 99.0, 99.5, 50),   // desenhado ao contrário: o fluxo é de a para c
            new FlowEdge("p2", "b", "c", 99.4, 99.0, 10),
            new FlowEdge("p3", "c", "d", 98.9, 98.5, 10),
        });

    [Fact]
    public void Numeracao_global_vai_da_cabeceira_mais_distante_a_descarga()
    {
        var numbers = Y().Number();
        Assert.Equal(1, numbers["a"]);
        Assert.Equal(2, numbers["b"]);
        Assert.Equal(3, numbers["c"]);
        Assert.Equal(4, numbers["d"]);
        var inverse = Y().Number(NumberingMode.Invertido);
        Assert.Equal(1, inverse["d"]);
    }

    [Fact]
    public void Sentido_pelas_elevacoes_de_fundo()
    {
        var g = Y();
        Assert.Equal("c", Assert.Single(g.Downstream("a")).Direction.Down);
        Assert.Equal(2, g.Upstream("c").Count);
        Assert.Empty(g.Downstream("d"));
        Assert.Empty(g.CycleNodes());
        Assert.Single(g.Components());
    }

    [Fact]
    public void Ciclo_e_componentes_separados()
    {
        var g = new FlowGraph(
            new[] { new FlowNode("a", "A", 0, 0), new FlowNode("b", "B", 1, 0), new FlowNode("x", "X", 50, 50) },
            new[] { new FlowEdge("p1", "a", "b", 100, 100, 1), new FlowEdge("p2", "b", "a", 100, 100, 1) });
        Assert.Equal(new[] { "a", "b" }, g.CycleNodes().OrderBy(x => x));
        Assert.Equal(2, g.Components().Count);
        Assert.Equal(3, g.Number().Count);
    }
}
