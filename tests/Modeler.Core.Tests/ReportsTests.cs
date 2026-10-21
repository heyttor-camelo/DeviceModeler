using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Export;

namespace Modeler.Core.Tests;

public class ReportsTests
{
    private static readonly DeviceDefinition Pv =
        DeviceDefinition.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "exemplos", "pv_retangular.yaml"));

    private static List<PlacedInstance> TwoPvs() => new()
    {
        new PlacedInstance("PV-01", new Vec3(1000, 2000, 0), Pv.Evaluate()),
        new PlacedInstance("PV 02", new Vec3(1030.5, 2000, 0), Pv.Evaluate(new Dictionary<string, object> { ["CotaFundo"] = 99.5 })),
    };

    [Fact]
    public void Totais_somam_por_quantitativo()
    {
        var totals = Reports.Totals(TwoPvs());
        var concrete = totals.Single(t => t.Name == "concreto_m3");
        var second = (1.5 * 1.5 * 2.15) - (1.2 * 1.2 * 2.0);
        Assert.Equal(1.5525 + second, concrete.Value, 9);
        Assert.Equal("m3", concrete.Unit);
    }

    [Fact]
    public void Csv_tem_cabecalho_linhas_e_total()
    {
        var lines = Reports.QuantitiesCsv(TwoPvs()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("dispositivo;tipo;versao;quantitativo;unidade;valor", lines[0]);
        Assert.Contains("PV-01;pv_retangular;1.1.0;concreto_m3;m3;1.5525", lines);
        Assert.Contains(lines, l => l.StartsWith("TOTAL;;;concreto_m3;m3;"));
    }

    [Fact]
    public void Swmm_gera_juncoes_e_coordenadas()
    {
        var inp = Reports.SwmmJunctions(TwoPvs());
        Assert.Contains("[JUNCTIONS]", inp);
        Assert.Contains("[COORDINATES]", inp);
        var junction = inp.Split('\n').Single(l => l.StartsWith("PV_02 ") && l.Contains("99.5"));
        Assert.Contains("2", junction.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2]); // prof. máx. = 2
        Assert.Contains(inp.Split('\n'), l => l.StartsWith("PV-01") && l.Contains("1000") && l.Contains("2000"));
    }
}
