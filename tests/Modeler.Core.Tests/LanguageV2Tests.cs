using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;
using Modeler.Core.Text;

namespace Modeler.Core.Tests;

/// <summary>Linguagem v2: perfis, extrusão, corte, escolha, existe/ou, mensagem, metadados,
/// modelos de texto, predefinições e ao_editar.</summary>
public class LanguageV2Tests
{
    private const string Header = """
        dispositivo: teste
        versao: 1.0.0
        categoria: pontual
        """;

    private static DeviceDefinition Def(string body) => DeviceDefinition.Parse(Header + "\n" + body, "v2.yaml");

    private static DefinitionException Fails(string body) =>
        Assert.ThrowsAny<DefinitionException>(() => DeviceDefinition.Parse(Header + "\n" + body, "v2.yaml"));

    private static object Eval(string text, IReadOnlyDictionary<string, object>? values = null) => Expression.Evaluate(text, values);

    // ------------------------------------------------------------------ geometria

    [Fact]
    public void Extrudar_retangulo_tem_o_volume_do_prisma()
    {
        var node = Assert.IsType<ExtrudeNode>(Eval("extrudar(retangulo(2, 3), 1.5)"));
        Assert.Equal(9.0, NaiveVolume.Of(node), 9);
        Assert.Equal(new Vec3(0, 0, 1.5), node.Vector);
    }

    [Fact]
    public void Polilinha_em_L_tem_a_area_certa_em_qualquer_sentido()
    {
        var ccw = (Profile)Eval("polilinha([[0,0], [2,0], [2,1], [1,1], [1,2], [0,2]])");
        var cw = (Profile)Eval("polilinha([[0,2], [1,2], [1,1], [2,1], [2,0], [0,0]])");
        Assert.Equal(3.0, ccw.Area(), 9);
        Assert.Equal(3.0, cw.Area(), 9);
        Assert.Equal(1.0, ccw.ArcNormal.Z, 9);
        Assert.Equal(1.0, cw.ArcNormal.Z, 9);
    }

    [Fact]
    public void Convexidade_positiva_e_anti_horaria_vista_de_cima()
    {
        // quadrado 2×2 com o trecho de baixo em semicírculo para fora (bulge 1 = anti-horário)
        var a = (Profile)Eval("polilinha([[0,0], [2,0], [2,2], [0,2]], convexidades = [1])");
        Assert.Equal(4 + (Math.PI / 2), a.Area(), 6);
        Assert.True(a.Sample().Min(p => p.Y) < -0.99);

        // o mesmo arco com os vértices em sentido horário continua para fora (convexidade − no trecho 0→2)
        var b = (Profile)Eval("polilinha([[2,0], [0,0], [0,2], [2,2]], convexidades = [-1])");
        Assert.Equal(4 + (Math.PI / 2), b.Area(), 6);
    }

    [Fact]
    public void Circulo_e_dois_arcos()
    {
        var c = (Profile)Eval("circulo([1, 1, 100], 0.5)");
        Assert.Equal(Math.PI * 0.25, c.Area(), 9);
        Assert.Equal(100, c.Sample().Average(p => p.Z), 9);
        var volume = NaiveVolume.Of((CsgNode)Eval("extrudar(circulo([0, 0, 0], 0.5), 2)"));
        Assert.Equal(Math.PI * 0.25 * 2, volume, 9);
    }

    [Fact]
    public void Perfil_com_vertices_3d_fora_do_plano_e_erro()
    {
        var ex = Assert.Throws<EvaluationException>(() => Eval("polilinha([[0,0,0], [1,0,0], [1,1,0], [0,1,1]])"));
        Assert.Contains("mesmo plano", ex.Message);
    }

    [Fact]
    public void Extrudar_exige_perfil_fechado()
    {
        var ex = Assert.Throws<EvaluationException>(() => Eval("extrudar(polilinha([[0,0], [1,0], [1,1]], fechada = falso), 1)"));
        Assert.Contains("fechado", ex.Message);
    }

    [Fact]
    public void Perfil_vertical_extrudado_na_horizontal()
    {
        // seção trapezoidal no plano XZ, extrudada em Y
        var node = (ExtrudeNode)Eval("extrudar(polilinha([[0,0,0], [2,0,0], [1.5,0,1], [0.5,0,1]]), 3, \"y\")");
        Assert.Equal(1.5 * 3, NaiveVolume.Of(node), 9);
    }

    [Fact]
    public void Plano3_e_cortar()
    {
        var plane = (Plane)Eval("plano3([0,0,1], [1,0,1], [0,1,1])");
        Assert.Equal(1.0, plane.Normal.Z, 9);
        var cut = Assert.IsType<CutNode>(Eval("cortar(prisma(1, 1, 2), plano3([0,0,1], [1,0,1], [0,1,1]))"));
        Assert.Equal(1.0, cut.Plane.SignedDistance(new Vec3(0, 0, 2)), 9);
        Assert.IsType<EmptyNode>(Eval("cortar(vazio(), plano([0,0,0], \"z\"))"));
        Assert.Throws<EvaluationException>(() => Eval("plano3([0,0,0], [1,0,0], [2,0,0])"));
    }

    [Fact]
    public void Vetores_somam_e_escalam()
    {
        Assert.Equal(new object[] { 1.0, 2.0, 3.5 }, (IReadOnlyList<object>)Eval("[0, 1, 3] + [1, 1, 0.5]"));
        Assert.Equal(new object[] { 2.0, 4.0, 6.0 }, (IReadOnlyList<object>)Eval("2 * [1, 2, 3]"));
        Assert.Equal(new object[] { 0.5, 1.0, 1.5 }, (IReadOnlyList<object>)Eval("[1, 2, 3] / 2"));
        Assert.Equal(5.0, (double)Eval("distancia([0,0,0], [3,4,0])"), 9);
        Assert.Equal(new object[] { 0.0, 0.0, 1.0 }, (IReadOnlyList<object>)Eval("cruz([1,0,0], [0,1,0])"));
    }

    [Fact]
    public void Planta_da_extrusao_vertical_mantem_o_perfil_concavo()
    {
        var node = (CsgNode)Eval("extrudar(polilinha([[0,0], [2,0], [2,1], [1,1], [1,2], [0,2]]), 1)");
        var part = Assert.Single(PlanOutline.Of(node));
        Assert.Equal(6, part.Points.Count);
    }

    // ------------------------------------------------------------------ nulo, existe, ou

    [Fact]
    public void Existe_e_ou_absorvem_so_a_falta_de_valor()
    {
        var values = new Dictionary<string, object>
        {
            ["c"] = new Dictionary<string, object> { ["cota"] = 100.0 },
            ["lista"] = new List<object> { 1.0 },
        };
        Assert.True((bool)Eval("existe(c.cota)", values));
        Assert.False((bool)Eval("existe(c.sentido)", values));
        Assert.False((bool)Eval("existe(lista[3])", values));
        Assert.False((bool)Eval("existe(nulo)"));
        Assert.Equal("entrada", Eval("ou(c.sentido, 'entrada')", values));
        Assert.Equal(100.0, Eval("ou(c.cota, 0)", values));
        Assert.Equal(7.0, Eval("ou(nulo, lista[5], 7)", values));
        Assert.True((bool)Eval("nulo == nulo"));
        Assert.False((bool)Eval("nulo == 0"));
        // erro de verdade continua sendo erro
        Assert.Throws<EvaluationException>(() => Eval("ou(1 / 0, 2)"));
    }

    [Fact]
    public void Operador_ou_continua_valendo()
    {
        Assert.True((bool)Eval("falso ou (1 < 2)"));
        Assert.True((bool)Eval("ou(nulo, verdadeiro) ou falso"));
    }

    [Fact]
    public void Nulo_e_palavra_reservada()
    {
        var ex = Fails("""
            parametros:
              nulo: { tipo: numero, padrao: 1 }
            """);
        Assert.Contains("reservada", ex.Message);
    }

    // ------------------------------------------------------------------ escolha e mensagem

    private const string ChooseBody = """
        parametros:
          Tipo: { tipo: texto, valores: [Simples, Duplo, Triplo], padrao: Simples }
        fluxo:
          - escolha: Tipo
            casos:
              Simples:
                - n = 1
              Duplo:
                - n = 2
            padrao:
              - n = 3
          - mensagem: "Tipo {Tipo} com {n} tubo(s)"
        saidas:
          quantitativos:
            tubos_un: n
        """;

    [Fact]
    public void Escolha_com_padrao()
    {
        var def = Def(ChooseBody);
        Assert.Equal(1.0, def.Evaluate().Quantities["tubos_un"].Value);
        Assert.Equal(2.0, def.Evaluate(new Dictionary<string, object> { ["Tipo"] = "Duplo" }).Quantities["tubos_un"].Value);
        var triplo = def.Evaluate(new Dictionary<string, object> { ["Tipo"] = "Triplo" });
        Assert.Equal(3.0, triplo.Quantities["tubos_un"].Value);
        Assert.Equal("Tipo Triplo com 3 tubo(s)", Assert.Single(triplo.Log));
    }

    [Fact]
    public void Escolha_sem_padrao_precisa_de_todos_os_casos()
    {
        var ex = Fails(ChooseBody.Replace("    padrao:\n      - n = 3\n", ""));
        Assert.Contains("faltam casos para Triplo", ex.Message);
    }

    [Fact]
    public void Caso_fora_dos_valores_e_erro_com_sugestao()
    {
        var ex = Fails(ChooseBody.Replace("      Duplo:", "      Duplu:"));
        Assert.Contains("'Duplu' não está nos valores", ex.Message);
        Assert.Contains("você quis dizer 'Duplo'", ex.Message);
    }

    [Fact]
    public void Escolha_numerica_sem_caso_e_erro_de_avaliacao()
    {
        var def = Def("""
            parametros:
              n: { tipo: inteiro, padrao: 2 }
            fluxo:
              - escolha: n
                casos:
                  1: [ "x = 10" ]
                  2: [ "x = 20" ]
            saidas:
              quantitativos:
                x_un: x
            """);
        Assert.Equal(20.0, def.Evaluate().Quantities["x_un"].Value);
        var ex = Assert.Throws<EvaluationException>(() => def.Evaluate(new Dictionary<string, object> { ["n"] = 5 }));
        Assert.Contains("nenhum caso para o valor '5'", ex.Message);
    }

    // ------------------------------------------------------------------ modelos e formatação

    [Theory]
    [InlineData("{H:m:3}", "1,500 m")]
    [InlineData("{H:cm:0}", "150 cm")]
    [InlineData("{H:mm}", "1.500,0 mm")]
    [InlineData("{i:%:2}", "0,50 %")]
    [InlineData("{H:m:2:n}", "1,50")]
    [InlineData("{H:m:2:p}", "1.50 m")]
    [InlineData("{V:m3:1}", "12.345,7 m³")]
    [InlineData("{Q:m3/s:3}", "0,125 m³/s")]
    [InlineData("{est:estaca}", "12 + 3,45")]
    [InlineData("{d:m:2:sinal}", "+0,30 m")]
    [InlineData("{t:maiusc}", "PV-01")]
    [InlineData("PV {H}", "PV 1.5")]
    [InlineData("{{literal}} {H*2}", "{literal} 3")]
    [InlineData("{se(H > 1, 'alto', 'baixo')}", "alto")]
    public void Modelo_formata_valores(string text, string expected)
    {
        var values = new Dictionary<string, object>
        {
            ["H"] = 1.5, ["i"] = 0.005, ["V"] = 12345.678, ["Q"] = 0.125, ["est"] = 243.45, ["d"] = 0.3, ["t"] = "pv-01",
        };
        Assert.Equal(expected, Template.Parse(text).Render(new DictionaryContext(values)));
    }

    [Fact]
    public void Litros_sao_recusados()
    {
        var ex = Assert.Throws<DefinitionException>(() => Template.Parse("{Q:l/s}"));
        Assert.Contains("litros", ex.Message);
    }

    [Fact]
    public void Chave_sem_par_aponta_coluna()
    {
        var ex = Fails("""
            parametros:
              H: { tipo: comprimento, padrao: 1 }
            validacoes:
              - condicao: H > 0
                mensagem: "H = {H m"
            """);
        Assert.Contains("'{' sem '}'", ex.Message);
        Assert.Equal("validacoes[0].mensagem", ex.Field);
    }

    [Fact]
    public void Nome_desconhecido_em_modelo_e_erro_de_carga()
    {
        var ex = Fails("""
            parametros:
              H: { tipo: comprimento, padrao: 1 }
            modelos:
              dica: "H = {Hh:m:2}"
            """);
        Assert.Contains("nome desconhecido 'Hh'", ex.Message);
    }

    [Fact]
    public void Metadados_codigo_e_modelos()
    {
        var def = Def("""
            familia: Caixa de ligação
            subtipo: Concreto
            codigo: "CL-{L*100:0}x{B*100:0}"
            modelos:
              nome: "{codigo}-{numero:0:p}"
              dica: "{familia} {codigo}\nH = {H:m:2} | {volume_m3:m3:3}"
            parametros:
              L: { tipo: comprimento, padrao: 0.6 }
              B: { tipo: comprimento, padrao: 0.8 }
              H: { tipo: comprimento, padrao: 1.25 }
            saidas:
              quantitativos:
                volume_m3: L * B * H
            """);
        Assert.Equal("Caixa de ligação", def.Family);
        Assert.Equal("Concreto", def.Subtype);
        var inst = def.Evaluate();
        Assert.Equal("CL-60x80", inst.Code);
        Assert.Equal("CL-60x80-7", inst.Render(def.Templates.Name!, new Dictionary<string, object> { ["numero"] = 7 }));
        Assert.Equal("Caixa de ligação CL-60x80\nH = 1,25 m | 0,600 m³", inst.Render(def.Templates.Tooltip!));
    }

    // ------------------------------------------------------------------ predefinições e ao_editar

    private const string PresetBody = """
        parametros:
          Catalogo: { tipo: texto, padrao: BB-60 }
          D: { tipo: comprimento, padrao: 0.6, min: 0.3, max: 2.0 }
          Parede: { tipo: comprimento, padrao: 0.15 }
          Comprimento: { tipo: comprimento, padrao: 1.0 }
        predefinicoes:
          parametro: Catalogo
          comuns: { Parede: 0.20 }
          itens:
            BB-60: { D: 0.60, Comprimento: 1.15 }
            BB-100: { D: 1.00, Comprimento: 1.51 }
        saidas:
          quantitativos:
            d_m: D
            parede_m: Parede
        """;

    [Fact]
    public void Codigo_preenche_as_dimensoes()
    {
        var def = Def(PresetBody);
        Assert.Equal(new object[] { "generico", "BB-60", "BB-100" }, def.FindParameter("Catalogo")!.AllowedValues!);
        var inst = def.Evaluate(new Dictionary<string, object> { ["Catalogo"] = "BB-100", ["D"] = 0.8 });
        Assert.Equal(1.0, inst.Parameters["D"]);
        Assert.Equal(0.2, inst.Parameters["Parede"]);
        Assert.Equal(1.51, inst.Parameters["Comprimento"]);

        var free = def.Evaluate(new Dictionary<string, object> { ["Catalogo"] = "generico", ["D"] = 0.8 });
        Assert.Equal(0.8, free.Parameters["D"]);
        Assert.Throws<ParameterValidationException>(() => def.Evaluate(new Dictionary<string, object> { ["Catalogo"] = "BB-999" }));
    }

    [Fact]
    public void Editar_dimensao_muda_o_codigo_para_generico()
    {
        var def = Def(PresetBody);
        var current = def.Evaluate().Parameters;
        var values = DeviceEditing.Edit(def, current, "D", 0.8, out var messages);
        Assert.Equal("generico", values["Catalogo"]);
        Assert.Equal(0.8, values["D"]);
        Assert.Contains(messages, m => m.Contains("BB-60 → generico"));

        // o mesmo valor do código não muda nada
        var same = DeviceEditing.Edit(def, current, "D", 0.6, out _);
        Assert.Equal("BB-60", same["Catalogo"]);

        // escolher o código de novo reaplica as dimensões
        var back = DeviceEditing.Edit(def, values, "Catalogo", "BB-100", out _);
        Assert.Equal(1.0, back["D"]);
    }

    [Fact]
    public void Modo_reaplicar_desfaz_a_edicao()
    {
        var def = Def(PresetBody.Replace("  parametro: Catalogo", "  parametro: Catalogo\n  ao_editar_dimensao: reaplicar"));
        var values = DeviceEditing.Edit(def, def.Evaluate().Parameters, "D", 0.8, out var messages);
        Assert.Equal("BB-60", values["Catalogo"]);
        Assert.Equal(0.6, values["D"]);
        Assert.Contains(messages, m => m.Contains("fixado pelo código"));
    }

    [Fact]
    public void Erros_de_predefinicoes()
    {
        Assert.Contains("'Dd' não existe", Fails(PresetBody.Replace("BB-100: { D: 1.00", "BB-100: { Dd: 1.00")).Message);
        Assert.Contains("fora do intervalo", Fails(PresetBody.Replace("D: 1.00", "D: 5.00")).Message);
        Assert.Contains("não é um código", Fails(PresetBody.Replace("padrao: BB-60 }", "padrao: XX }")).Message);
        Assert.Contains("reservado", Fails(PresetBody.Replace("BB-100:", "generico:")).Message);
    }

    private const string RedundantBody = """
        parametros:
          CotaTampa: { tipo: cota, padrao: 101.5 }
          CotaFundo: { tipo: cota, padrao: 100.0 }
          H: { tipo: comprimento, padrao: 1.5, min: 0.5 }
        ao_editar:
          CotaFundo: { H: CotaTampa - CotaFundo }
          H: { CotaFundo: CotaTampa - H }
          CotaTampa: { H: CotaTampa - CotaFundo }
        saidas:
          quantitativos:
            ultimo_un: se(editado() == "H", 1, 0)
        """;

    [Fact]
    public void Ao_editar_resolve_redundancias()
    {
        var def = Def(RedundantBody);
        var current = def.Evaluate().Parameters;
        var a = DeviceEditing.Edit(def, current, "H", 2.0, out var messages);
        Assert.Equal(99.5, (double)a["CotaFundo"], 9);
        Assert.Contains(messages, m => m.StartsWith("CotaFundo = 99.5"));

        var b = DeviceEditing.Edit(def, current, "CotaFundo", 99.0, out _);
        Assert.Equal(2.5, (double)b["H"], 9);

        Assert.Equal(1.0, def.Evaluate(a, edited: "H").Quantities["ultimo_un"].Value);
        Assert.Equal(0.0, def.Evaluate(a).Quantities["ultimo_un"].Value);
    }

    [Fact]
    public void Ao_editar_valida_o_resultado()
    {
        var def = Def(RedundantBody);
        var ex = Assert.Throws<ParameterValidationException>(() => DeviceEditing.Edit(def, def.Evaluate().Parameters, "CotaFundo", 101.3, out _));
        Assert.Equal("H", ex.Parameter);
    }

    [Fact]
    public void Ao_editar_nao_reescreve_o_proprio_parametro()
    {
        Assert.Contains("o próprio", Fails(RedundantBody.Replace("H: { CotaFundo: CotaTampa - H }", "H: { H: 2 }")).Message);
    }
}
