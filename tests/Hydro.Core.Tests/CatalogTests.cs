using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Editing;
using Hydro.Core.Results;

namespace Hydro.Core.Tests;

/// <summary>Os dispositivos do catálogo carregam e avaliam com todos os valores de lista e todos os
/// códigos; mais conferências específicas de cada um.</summary>
public class CatalogTests
{
    private static string Path(string relative) => System.IO.Path.Combine("catalog", relative);

    public static IEnumerable<object[]> Files() =>
        Directory.EnumerateFiles("catalog", "*.yaml", SearchOption.AllDirectories).Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Files))]
    public void Todos_os_valores_de_lista_avaliam(string file)
    {
        var def = DeviceDefinition.Load(file);
        var baseInstance = def.Evaluate();
        Assert.NotEmpty(baseInstance.Solids);
        foreach (var parameter in def.Parameters.Where(p => p.AllowedValues is { Count: > 0 }))
        {
            foreach (var value in parameter.AllowedValues!)
            {
                var inst = def.Evaluate(new Dictionary<string, object> { [parameter.Name] = value });
                Assert.NotEmpty(inst.Solids);
                foreach (var solid in inst.Solids)
                {
                    Assert.NotEmpty(PlanOutline.Of(solid.Node));
                }

                Assert.All(inst.Quantities, q => Assert.True(q.Value >= 0, $"{file}: {parameter.Name}={value}: {q.Name} = {q.Value}"));
            }
        }
    }

    [Fact]
    public void Caixa_de_ligacao_codigos_tampas_e_redundancia()
    {
        var def = DeviceDefinition.Load(Path("drenagem/caixa_ligacao.yaml"));
        Assert.Equal("Caixa de ligação", def.Family);
        var inst = def.Evaluate();
        Assert.Equal("CL-80x80", inst.Code);
        Assert.Equal(new[] { "concreto", "tampa" }, inst.Solids.Select(s => s.Name));

        // corpo: externo 1,10 × 1,10 × (1,00 − 0,08 + 0,15) − interno 0,80 × 0,80 × 0,92
        var expected = (1.1 * 1.1 * 1.07) - (0.8 * 0.8 * 0.92);
        Assert.Equal(expected, inst.Quantities["concreto_m3"].Value, 9);
        Assert.Equal(expected, NaiveVolume.Of(inst.Solids["concreto"].Node), 9);

        var tampao = def.Evaluate(new Dictionary<string, object> { ["TipoTampa"] = "Tampao" });
        Assert.True(tampao.Solids.Contains("tampao"));
        Assert.Equal("Caixa CL-80x80 com tampa Tampao", Assert.Single(tampao.Log));

        // editar L muda o código para genérico; o código passa a ser calculado
        var edited = DeviceEditing.Edit(def, inst.Parameters, "L", 0.70, out _);
        Assert.Equal("generico", edited["Catalogo"]);
        Assert.Equal("CL-70x80", def.Evaluate(edited).Code);

        // H move o fundo
        var deeper = DeviceEditing.Edit(def, inst.Parameters, "H", 1.50, out _);
        Assert.Equal(99.5, (double)deeper["CotaFundo"], 9);
        Assert.Empty(def.Evaluate(deeper).Warnings);
    }

    [Fact]
    public void Caixa_de_ligacao_com_tubo()
    {
        var def = DeviceDefinition.Load(Path("drenagem/caixa_ligacao.yaml"));
        var inst = def.Evaluate(new Dictionary<string, object>
        {
            ["Conexoes"] = new[] { new ConnectionInput("T1", 180, 100.05, 0.30, 0.36) },
        });
        var connector = Assert.Single(inst.Connectors);
        Assert.Equal(-0.55, connector.Position.X, 9);
        Assert.Equal(100.2, connector.Position.Z, 9);
        Assert.Equal("tubo", connector.Filter);
        var junction = Assert.IsType<SwmmJunction>(Assert.Single(inst.Hydraulics));
        Assert.Equal(1.0, junction.MaxDepth, 9);
    }

    [Fact]
    public void Boca_de_bueiro_simples_dupla_e_tripla()
    {
        var def = DeviceDefinition.Load(Path("drenagem/boca_bueiro.yaml"));
        var simples = def.Evaluate();
        Assert.Equal("BSTC-100", simples.Code);
        Assert.Equal(new[] { "testa", "alas", "soleira" }, simples.Solids.Select(s => s.Name));
        Assert.Equal(new[] { "tubo_1", "jusante" }, simples.Connectors.Select(c => c.Name));

        var triplo = def.Evaluate(new Dictionary<string, object> { ["Tipo"] = "Triplo", ["Catalogo"] = "D60" });
        Assert.Equal("BTTC-60", triplo.Code);
        Assert.Equal(3, triplo.Connectors.Count(c => c.Filter == "tubo"));
        Assert.Equal(-(0.72 + 0.30), triplo.Connectors["tubo_1"].Position.Y, 9);

        // a soleira e a testa conferem com o volume da árvore (a testa desconta os furos de Parede + 0,10)
        var v = simples.Variables;
        Assert.Equal((double)v["vol_soleira"], NaiveVolume.Of(simples.Solids["soleira"].Node), 9);
        var de = 1.0 + (2 * 0.08);
        Assert.Equal((double)v["vol_testa"] - (Math.PI * de * de / 4 * 0.10), NaiveVolume.Of(simples.Solids["testa"].Node), 9);

        // as alas são cortadas pela rampa: a normal aponta para baixo (fica a parte de baixo)
        var alas = Assert.IsType<UnionNode>(simples.Solids["alas"].Node);
        var cut = Assert.IsType<CutNode>(alas.Children[0]);
        Assert.True(cut.Plane.Normal.Z < 0);
        Assert.True(cut.Plane.SignedDistance(new Vec3(0, 0, (double)v["CotaTopo"] - 0.01)) > 0);
        Assert.True(cut.Plane.SignedDistance(new Vec3(0, 0, (double)v["CotaTopo"] + 0.01)) < 0);

        var generico = def.Evaluate(DeviceEditing.Edit(def, simples.Parameters, "D", 0.90, out _));
        Assert.Equal("BSTC-90 (genérico)", generico.Code);
    }

    [Fact]
    public void Boca_de_lobo_por_tipo_de_entrada()
    {
        var def = DeviceDefinition.Load(Path("drenagem/boca_lobo_simples.yaml"));
        var combinada = def.Evaluate();
        Assert.Equal("BLCG-100", combinada.Code);
        Assert.Equal(new[] { "grelha", "guia_chapeu", "concreto" }, combinada.Solids.Select(s => s.Name));
        var guia = def.Evaluate(new Dictionary<string, object> { ["Tipo"] = "Guia" });
        Assert.Equal(new[] { "laje", "guia_chapeu", "concreto" }, guia.Solids.Select(s => s.Name));
        Assert.Equal(0.0, guia.Quantities["grelha_un"].Value);
        var grelha = def.Evaluate(new Dictionary<string, object> { ["Tipo"] = "Grelha" });
        Assert.Equal(0.0, grelha.Quantities["guia_chapeu_m3"].Value);

        // o chanfro tira o canto de baixo do lado da rua
        var cut = Assert.IsType<CutNode>(combinada.Solids["guia_chapeu"].Node);
        Assert.True(cut.Plane.SignedDistance(new Vec3(0, 0.30, 100.15)) < 0);
        Assert.True(cut.Plane.SignedDistance(new Vec3(0, 0.40, 100.25)) > 0);
        Assert.Equal(1.30 * 0.30 * 0.15 - (1.30 * 0.05 * 0.05 / 2), combinada.Quantities["guia_chapeu_m3"].Value, 9);
    }

    [Fact]
    public void Dica_da_caixa_formata_em_pt_br()
    {
        var def = DeviceDefinition.Load(Path("drenagem/caixa_ligacao.yaml"));
        var text = def.Evaluate().Render(def.Templates.Tooltip!);
        Assert.Equal("CL-80x80 (Concreto)\nElevação da tampa: 101,000 m\nElevação do fundo: 100,000 m\nAltura útil: 1,00 m", text);
        Assert.Equal("CL-3", def.Evaluate().Render(def.Templates.Name!, new Dictionary<string, object> { ["numero"] = 3 }));
    }
}
