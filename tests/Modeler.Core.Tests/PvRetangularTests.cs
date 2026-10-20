using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using Modeler.Core.Results;

namespace Modeler.Core.Tests;

public class PvRetangularTests
{
    private static readonly string PvPath = Path.Combine(AppContext.BaseDirectory, "catalog", "exemplos", "pv_retangular.yaml");

    private static readonly DeviceDefinition Pv = DeviceDefinition.Load(PvPath);

    [Fact]
    public void Carrega_a_definicao()
    {
        Assert.Equal("pv_retangular", Pv.Id);
        Assert.Equal("1.0.0", Pv.Version);
        Assert.Equal(DeviceCategory.Pontual, Pv.Category);
        Assert.Equal(new[] { "L", "B", "esp", "CotaFundo", "CotaTampa", "tampa", "folga", "lastro", "Conexoes" }, Pv.Parameters.Select(p => p.Name));
        Assert.Equal(64, Pv.Hash.Length);

        var tampa = Pv.FindParameter("tampa")!;
        Assert.Equal(ParameterType.Texto, tampa.Type);
        Assert.Equal(new object[] { "T600", "T900" }, tampa.AllowedValues);
        Assert.Equal(TargetKind.Superficie, Pv.FindParameter("CotaTampa")!.Target);
        Assert.Equal("m", Pv.FindParameter("L")!.Unit);
    }

    [Fact]
    public void Avalia_com_valores_padrao()
    {
        var inst = Pv.Evaluate();

        Assert.Equal(1.5, inst.Variable("H"), 9);
        Assert.Equal(1.5, inst.Variable("Lext"), 9);
        Assert.Equal(new[] { "concreto", "tampa" }, inst.Solids.Select(s => s.Name));
        Assert.Equal("Concreto", inst.Solids["concreto"].Material);
        Assert.Empty(inst.Connectors);
        Assert.Empty(inst.Warnings);

        var junction = Assert.IsType<SwmmJunction>(Assert.Single(inst.Hydraulics));
        Assert.Equal(100.0, junction.InvertElevation, 9);
        Assert.Equal(1.5, junction.MaxDepth, 9);

        // corpo = prisma externo − vazio interno
        var body = Assert.IsType<SubtractNode>(inst.Solids["concreto"].Node);
        AssertPrism(Assert.IsType<PrismNode>(body.Source), 1.5, 1.5, 1.65, 99.85);
        AssertPrism(Assert.IsType<PrismNode>(Assert.Single(body.Tools)), 1.2, 1.2, 1.5, 100.0);
    }

    private static void AssertPrism(PrismNode prism, double l, double b, double h, double baseZ)
    {
        Assert.Equal(l, prism.Length, 9);
        Assert.Equal(b, prism.Width, 9);
        Assert.Equal(h, prism.Height, 9);
        Assert.Equal(0, prism.Base.X, 9);
        Assert.Equal(0, prism.Base.Y, 9);
        Assert.Equal(baseZ, prism.Base.Z, 9);
    }

    [Fact]
    public void H_e_calculado_a_partir_das_cotas()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object> { ["CotaFundo"] = 98.0, ["CotaTampa"] = 101.0 });
        Assert.Equal(3.0, inst.Variable("H"), 9);
        Assert.Equal(3.0, ((SwmmJunction)inst.Hydraulics[0]).MaxDepth, 9);
    }

    [Theory]
    [InlineData(1.50, 0)]
    [InlineData(2.00, 0)]
    [InlineData(2.10, 6)]
    [InlineData(2.50, 7)]
    [InlineData(3.00, 9)]
    public void Degraus_so_aparecem_quando_H_maior_que_2(double h, int expected)
    {
        var inst = Pv.Evaluate(new Dictionary<string, object> { ["CotaFundo"] = 100.0, ["CotaTampa"] = 100.0 + h });

        Assert.Equal(expected, inst.Quantities["degraus_un"].Value, 9);
        if (expected == 0)
        {
            Assert.False(inst.Solids.Contains("degraus"));
            return;
        }

        var steps = Assert.IsType<ArrayNode>(inst.Solids["degraus"].Node);
        Assert.Equal(expected, steps.Count);
        Assert.Equal(new Vec3(0, 0, 0.30), steps.Step);
        var rung = Assert.IsType<CylinderNode>(steps.Child);
        Assert.Equal(100.30, rung.Base.Z, 9);
        // último degrau a no máximo H - 0,30 do fundo
        Assert.True(0.30 * expected <= h - 0.30 + 1e-9);
    }

    [Fact]
    public void Concreto_bate_com_calculo_manual()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object>
        {
            ["L"] = 1.0, ["B"] = 1.5, ["esp"] = 0.20, ["CotaFundo"] = 95.0, ["CotaTampa"] = 97.4,
        });

        const double L = 1.0, B = 1.5, esp = 0.20, H = 2.4;
        var manual = ((L + (2 * esp)) * (B + (2 * esp)) * (H + esp)) - (L * B * H); // externo − vazio
        Assert.Equal(manual, inst.Quantities["concreto_m3"].Value, 9);
        Assert.Equal("m3", inst.Quantities["concreto_m3"].Unit);

        // a árvore CSG do sólido tem o mesmo volume
        Assert.Equal(manual, NaiveVolume.Of(inst.Solids["concreto"].Node), 9);
    }

    [Fact]
    public void Escavacao_e_forma_batem_com_calculo_manual()
    {
        var inst = Pv.Evaluate();
        Assert.Equal(1.5525, inst.Quantities["concreto_m3"].Value, 9);
        Assert.Equal(2.1 * 2.1 * 1.75, inst.Quantities["escavacao_m3"].Value, 9);
        Assert.Equal((2 * 3.0 * 1.65) + (2 * 2.4 * 1.5), inst.Quantities["forma_m2"].Value, 9);
        Assert.Equal("m2", inst.Quantities["forma_m2"].Unit);
    }

    [Fact]
    public void Lista_de_tampa_define_o_tampao()
    {
        var t900 = Pv.Evaluate(new Dictionary<string, object> { ["tampa"] = "T900" });
        Assert.Equal(0.90, Assert.IsType<CylinderNode>(t900.Solids["tampa"].Node).Diameter, 9);
    }

    [Fact]
    public void Cada_conexao_gera_furo_e_conector()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object>
        {
            ["Conexoes"] = new[]
            {
                new ConnectionInput("entrada", 180, 100.20, 0.40),
                new ConnectionInput("saida", 90, 100.00, 0.60),
            },
        });

        Assert.Equal(2, inst.Connectors.Count);
        var entrada = inst.Connectors["entrada"];
        Assert.Equal(-0.75, entrada.Position.X, 9);
        Assert.Equal(0.0, entrada.Position.Y, 9);
        Assert.Equal(100.40, entrada.Position.Z, 9);
        Assert.Equal(-1.0, entrada.Direction.X, 9);
        Assert.Equal(0.40, entrada.Diameter, 9);

        var saida = inst.Connectors["saida"];
        Assert.Equal(0.75, saida.Position.Y, 9);
        Assert.Equal(1.0, saida.Direction.Y, 9);

        // corpo = ((externo − interno) − furo1) − furo2
        var outer = Assert.IsType<SubtractNode>(inst.Solids["concreto"].Node);
        var hole2 = Assert.IsType<RotateNode>(Assert.Single(outer.Tools));
        Assert.Equal(90, hole2.AngleDegrees, 9);
        Assert.Equal(0.70, Assert.IsType<CylinderNode>(hole2.Child).Diameter, 9);
        var inner = Assert.IsType<SubtractNode>(outer.Source);
        Assert.IsType<RotateNode>(Assert.Single(inner.Tools));

        var holes = Math.PI / 4 * ((0.5 * 0.5) + (0.7 * 0.7)) * 0.15;
        Assert.Equal(1.5525 - holes, inst.Quantities["concreto_m3"].Value, 9);
    }

    [Fact]
    public void Conexoes_aceitam_dicionarios()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object>
        {
            ["Conexoes"] = new List<Dictionary<string, object>>
            {
                new() { ["angulo"] = 0, ["cota"] = 100.0, ["diametro"] = 0.3 },
            },
        });
        Assert.Equal("C1", Assert.Single(inst.Connectors).Name);
    }

    [Fact]
    public void Grips_ficam_nas_posicoes_declaradas()
    {
        var inst = Pv.Evaluate();
        var gripL = inst.Grips.Single(g => g.Parameter == "L");
        Assert.Equal(new Vec3(0.75, 0, 101.5), gripL.Position);
        Assert.Equal(Vec3.UnitX, gripL.Direction);
        Assert.Equal(2, gripL.Factor);
        Assert.Equal(3, inst.Grips.Count);
    }

    [Fact]
    public void Parametros_resolvidos_ficam_na_instancia()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object> { ["L"] = 2 });
        Assert.Equal(2.0, inst.Parameters["L"]);
        Assert.Equal("T600", inst.Parameters["tampa"]);
    }

    [Fact]
    public void Parametro_fora_do_intervalo_gera_erro_claro()
    {
        var ex = Assert.Throws<ParameterValidationException>(() => Pv.Evaluate(new Dictionary<string, object> { ["L"] = 5.0 }));
        Assert.Equal("L", ex.Parameter);
        Assert.Contains("valor 5 m fora do intervalo permitido [0.8; 3]", ex.Message);
    }

    [Fact]
    public void Valor_fora_da_lista_gera_erro_claro()
    {
        var ex = Assert.Throws<ParameterValidationException>(() => Pv.Evaluate(new Dictionary<string, object> { ["tampa"] = "T700" }));
        Assert.Contains("'T700' não está na lista de valores permitidos: T600, T900", ex.Message);
    }

    [Theory]
    [InlineData("L", "abc", "esperado número")]
    [InlineData("tampa", 600, "esperado texto")]
    [InlineData("Lx", 1.0, "parâmetro desconhecido")]
    public void Tipo_errado_ou_parametro_inexistente(string name, object value, string fragment)
    {
        var ex = Assert.Throws<ParameterValidationException>(() => Pv.Evaluate(new Dictionary<string, object> { [name] = value }));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Validacao_de_erro_interrompe_e_de_aviso_registra()
    {
        var ex = Assert.Throws<EvaluationException>(() =>
            Pv.Evaluate(new Dictionary<string, object> { ["CotaFundo"] = 100.0, ["CotaTampa"] = 100.3 }));
        Assert.Contains("Altura útil H = 0.3 m menor que o mínimo", ex.Message);
        Assert.True(ex.Line > 0);

        var deep = Pv.Evaluate(new Dictionary<string, object> { ["CotaFundo"] = 90.0, ["CotaTampa"] = 96.5 });
        Assert.Contains("acima de 6 m", Assert.Single(deep.Warnings));
    }
}
