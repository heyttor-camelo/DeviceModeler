using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;

namespace Modeler.Core.Tests;

/// <summary>Suporte no Core ao comportamento no DWG (F2): impacto dos parâmetros na geometria,
/// aramado da prévia dos grips e grips de rotação e de perfil.</summary>
public class BehaviorTests
{
    private static DeviceDefinition Load(string relative) => DeviceDefinition.Load(Path.Combine("catalog", relative));

    [Fact]
    public void Folga_e_lastro_nao_mexem_na_geometria_do_pv()
    {
        var pv = Load("exemplos/pv_retangular.yaml");
        var geometry = pv.GeometryParameters;
        Assert.Contains("L", geometry);
        Assert.Contains("CotaTampa", geometry);
        Assert.Contains("Conexoes", geometry);   // furos
        Assert.Contains("tampa", geometry);      // diâmetro do tampão (saidas.solidos)
        Assert.DoesNotContain("folga", geometry);
        Assert.DoesNotContain("lastro", geometry);

        var before = pv.Evaluate().Parameters;
        var after = pv.Evaluate(new Dictionary<string, object> { ["folga"] = 0.5, ["lastro"] = 0.2 }).Parameters;
        Assert.False(pv.ChangesGeometry(before, after));
        Assert.True(pv.ChangesGeometry(before, pv.Evaluate(new Dictionary<string, object> { ["L"] = 1.5 }).Parameters));
    }

    [Fact]
    public void Impacto_segue_condicoes_escolha_e_laco()
    {
        var def = DeviceDefinition.Parse("""
            dispositivo: t
            versao: 1
            categoria: pontual
            parametros:
              a: { tipo: comprimento, padrao: 1 }
              b: { tipo: comprimento, padrao: 1 }
              cond: { tipo: booleano, padrao: verdadeiro }
              tipo: { tipo: texto, valores: [x, y], padrao: x }
              n: { tipo: inteiro, padrao: 2 }
              so_qtd: { tipo: numero, padrao: 3 }
            variaveis:
              w: a * 2
            fluxo:
              - s = vazio()
              - se: cond
                entao:
                  - s = prisma(w, 1, 1)
              - escolha: tipo
                casos:
                  x: [ "k = b" ]
                  y: [ "k = 1" ]
              - para cada: i em intervalo(0, n)
                faca:
                  - s = unir(s, mover(prisma(k, 1, 1), [i * 2, 0, 0]))
              - expor_solido("s", s)
            saidas:
              quantitativos:
                q_un: so_qtd
            """);
        Assert.Equal(new[] { "a", "b", "cond", "tipo", "n" }.OrderBy(x => x), def.GeometryParameters.OrderBy(x => x));
    }

    [Fact]
    public void Aramado_do_prisma_tem_dois_aneis_e_quatro_arestas()
    {
        var lines = Wireframe.Of(new PrismNode(2, 1, 3, new Vec3(0, 0, 100)));
        Assert.Equal(6, lines.Count);
        Assert.Equal(2, lines.Count(l => l.Closed));
        Assert.Equal(103, lines[1].Points[0].Z, 9);
        var holes = Wireframe.Of(new SubtractNode(new PrismNode(1, 1, 1, Vec3.Zero), new CsgNode[] { new CylinderNode(0.2, 1, Vec3.Zero, Vec3.UnitZ) }));
        Assert.Contains(holes, l => l.Removed);
    }

    [Fact]
    public void Aramado_dos_dispositivos_do_catalogo()
    {
        foreach (var file in Directory.EnumerateFiles("catalog", "*.yaml", SearchOption.AllDirectories))
        {
            var inst = DeviceDefinition.Load(file).Evaluate();
            Assert.All(inst.Solids, s => Assert.NotEmpty(Wireframe.Of(s.Node)));
        }
    }

    private const string GripBody = """
        dispositivo: g
        versao: 1
        categoria: pontual
        parametros:
          Ang: { tipo: angulo, padrao: 0, min: -90, max: 90, grip: { tipo: rotacionar, posicao: "[1, 0, 100]", centro: "[0, 0, 100]" } }
          CotaFundo: { tipo: cota, padrao: 100, grip: { tipo: perfil, posicao: "[0, 0, CotaFundo]", sentido: cima } }
          Prof: { tipo: comprimento, padrao: 1, grip: { tipo: perfil, posicao: "[0, 0, 99]", sentido: baixo } }
        fluxo:
          - expor_solido("s", rotacionar(prisma(1, 1, 1, [0, 0, CotaFundo]), Ang))
        """;

    [Fact]
    public void Grip_de_rotacao_mede_o_angulo_em_planta()
    {
        var def = DeviceDefinition.Parse(GripBody);
        var grip = def.Evaluate().Grips.Single(g => g.Parameter == "Ang");
        Assert.Equal(GripKind.Rotacionar, grip.Kind);
        Assert.Equal(new Vec3(0, 0, 100), grip.Center);
        // de (1, 0) para (1, 1): 45° anti-horário
        Assert.Equal(45, DeviceEditing.GripValue(def.FindParameter("Ang")!, 0, new Vec3(0, 1, 0), grip), 9);
        // além do máximo: limitado a 90
        Assert.Equal(90, DeviceEditing.GripValue(def.FindParameter("Ang")!, 80, new Vec3(-2, 1, 0), grip), 9);
    }

    [Fact]
    public void Grip_de_perfil_anda_na_vertical()
    {
        var def = DeviceDefinition.Parse(GripBody);
        var inst = def.Evaluate();
        var fundo = inst.Grips.Single(g => g.Parameter == "CotaFundo");
        Assert.Equal(GripKind.Perfil, fundo.Kind);
        Assert.Equal(100.5, DeviceEditing.GripValue(def.FindParameter("CotaFundo")!, 100, new Vec3(3, 3, 0.5), fundo), 9);
        var prof = inst.Grips.Single(g => g.Parameter == "Prof");
        Assert.Equal(1.5, DeviceEditing.GripValue(def.FindParameter("Prof")!, 1, new Vec3(0, 0, -0.5), prof), 9);
    }

    [Fact]
    public void Grip_de_rotacao_exige_angulo_e_posicao()
    {
        var notAngle = Assert.ThrowsAny<DefinitionException>(() => DeviceDefinition.Parse(GripBody.Replace("Ang: { tipo: angulo, padrao: 0, min: -90, max: 90,", "Ang: { tipo: comprimento, padrao: 0,")));
        Assert.Contains("tipo angulo", notAngle.Message);
        var axis = Assert.ThrowsAny<DefinitionException>(() => DeviceDefinition.Parse(GripBody.Replace("tipo: rotacionar, posicao", "tipo: rotacionar, eixo: x, posicao")));
        Assert.Contains("'eixo' não se aplica", axis.Message);
        var unknown = Assert.ThrowsAny<DefinitionException>(() => DeviceDefinition.Parse(GripBody.Replace("tipo: perfil, posicao: \"[0, 0, CotaFundo]\"", "tipo: perfil, posicao: \"[0, 0, CotaFundu]\"")));
        Assert.Contains("CotaFundu", unknown.Message);
    }
}
