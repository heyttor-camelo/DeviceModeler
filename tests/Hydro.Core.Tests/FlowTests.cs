using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Errors;

namespace Hydro.Core.Tests;

public class FlowTests
{
    private const string Header = """
        dispositivo: teste
        versao: 1.0.0
        categoria: pontual
        parametros:
          n: { tipo: inteiro, padrao: 3, min: 0, max: 10 }
          modo: { tipo: texto, valores: [a, b], padrao: a }
        """;

    private static DeviceDefinition Def(string body) => DeviceDefinition.Parse(Header + "\n" + body, "fluxo.yaml");

    [Fact]
    public void Se_entao_senao_e_para_cada()
    {
        var def = Def("""
            fluxo:
              - pecas = vazio()
              - total = 0
              - para cada: i em intervalo(0, n)
                faca:
                  - total = total + i
                  - pecas = unir(pecas, mover(prisma(1, 1, 1), [2 * i, 0, 0]))
              - se: modo == "a"
                então:
                  - rotulo = "A"
                senão:
                  - rotulo = "B"
              - expor_solido("pecas", pecas)
            saidas:
              quantitativos:
                total_un: total
                rotulo_b: se(rotulo == "B", 1, 0)
            """);

        var a = def.Evaluate();
        Assert.Equal(3.0, a.Quantities["total_un"].Value);
        Assert.Equal("un", a.Quantities["total_un"].Unit);
        Assert.Equal(0.0, a.Quantities["rotulo_b"].Value);
        var union = Assert.IsType<UnionNode>(a.Solids["pecas"].Node);
        Assert.Equal(3, union.Children.Count);
        Assert.Equal(3.0, NaiveVolume.Of(union), 9);

        var b = def.Evaluate(new Dictionary<string, object> { ["modo"] = "b", ["n"] = 0 });
        Assert.Equal(1.0, b.Quantities["rotulo_b"].Value);
        Assert.False(b.Solids.Contains("pecas")); // sólido vazio não é exposto
    }

    [Fact]
    public void Expressao_pode_continuar_na_linha_seguinte()
    {
        var def = Def("""
            fluxo:
              - corpo = subtrair(prisma(2, 2, 2),
                                 prisma(1, 1, 1, [0, 0, 1]))
              - expor_solido("corpo", corpo)
            """);
        Assert.Equal(7.0, NaiveVolume.Of(def.Evaluate().Solids["corpo"].Node), 9);
    }

    [Fact]
    public void Intersectar_e_rotacionar()
    {
        var def = Def("""
            fluxo:
              - s = intersectar(prisma(2, 2, 2), rotacionar(cilindro(1, 4, [0, 0, -1]), 90, "x", [0, 0, 1]))
              - expor_solido("s", s)
            """);
        var node = Assert.IsType<IntersectNode>(def.Evaluate().Solids["s"].Node);
        var rotate = Assert.IsType<RotateNode>(node.Children[1]);
        Assert.Equal(Vec3.UnitX, rotate.Axis);
        Assert.Equal(new Vec3(0, 0, 1), rotate.Center);
    }

    [Fact]
    public void Atribuir_a_parametro_e_proibido()
    {
        var ex = Assert.ThrowsAny<DefinitionException>(() => Def("""
            fluxo:
              - n = 4
            """));
        Assert.Contains("não é possível atribuir a 'n'", ex.Message);
        Assert.Equal("fluxo[0]", ex.Field);
        Assert.Equal(8, ex.Line);
    }

    [Fact]
    public void Instrucao_sem_efeito_e_rejeitada()
    {
        var ex = Assert.ThrowsAny<DefinitionException>(() => Def("""
            fluxo:
              - prisma(1, 1, 1)
            """));
        Assert.Contains("instrução sem efeito", ex.Message);
    }

    [Fact]
    public void Nome_usado_antes_de_existir_e_acusado_na_carga()
    {
        var ex = Assert.ThrowsAny<DefinitionException>(() => Def("""
            fluxo:
              - a = prisma(1, 1, 1)
              - b = subtrair(a, furo)
            """));
        Assert.Contains("nome desconhecido 'furo'", ex.Message);
        Assert.Equal("fluxo[1]", ex.Field);
        Assert.Equal(9, ex.Line);
        Assert.Equal(21, ex.Column);
    }

    [Fact]
    public void Erro_em_tempo_de_execucao_aponta_a_instrucao()
    {
        var def = Def("""
            fluxo:
              - h = n - 3
              - p = prisma(1, 1, h)
            """);
        var ex = Assert.Throws<EvaluationException>(() => def.Evaluate());
        Assert.Contains("prisma: 'H' deve ser maior que zero (recebido 0)", ex.Message);
        Assert.Equal(9, ex.Line);
        Assert.Equal("fluxo[1]", ex.Field);
    }

    [Fact]
    public void Saidas_so_podem_ser_expostas_no_fluxo()
    {
        var def = Def("""
            variaveis:
              x: expor_solido("a", prisma(1, 1, 1))
            """);
        var ex = Assert.Throws<EvaluationException>(() => def.Evaluate());
        Assert.Contains("só pode ser usada no 'fluxo'", ex.Message);
    }

    [Fact]
    public void Solido_duplicado_e_erro()
    {
        var def = Def("""
            fluxo:
              - expor_solido("a", prisma(1, 1, 1))
              - expor_solido("a", prisma(2, 2, 2))
            """);
        Assert.Contains("já foi exposto", Assert.Throws<EvaluationException>(() => def.Evaluate()).Message);
    }

    [Fact]
    public void Conectores_declarativos_e_juncao()
    {
        var def = Def("""
            saidas:
              conectores:
                - nome: saida
                  posicao: "[0.5, 0, 100]"
                  direcao: "[2, 0, 0]"
                  diametro: 0.4
                  filtro: concreto
              hidraulica:
                swmm_juncao:
                  cota_fundo: 99.5
                  profundidade_maxima: 1.8
                  sobrecarga: 0.5
            """);
        var inst = def.Evaluate();
        var connector = Assert.Single(inst.Connectors);
        Assert.Equal(Vec3.UnitX, connector.Direction);
        Assert.Equal("concreto", connector.Filter);
        var junction = Assert.IsType<Hydro.Core.Results.SwmmJunction>(Assert.Single(inst.Hydraulics));
        Assert.Equal(0.5, junction.SurchargeDepth);
    }
}
