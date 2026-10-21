using Hydro.Core.Definition;
using Hydro.Core.Editing;

namespace Hydro.Core.Tests;

/// <summary>Valor digitado com o grip quente (HY_GRIP): texto → valor final do parâmetro, limites e
/// o texto da linha de comando.</summary>
public class GripInputTests
{
    private static readonly DeviceDefinition Pv = DeviceDefinition.Load(Path.Combine("catalog", "exemplos", "pv_retangular.yaml"));

    private static readonly DeviceDefinition Mixed = DeviceDefinition.Parse("""
        dispositivo: t
        versao: 1
        categoria: pontual
        parametros:
          Ang: { tipo: angulo, padrao: 0, min: -180, max: 180, grip: { tipo: rotacionar, posicao: "[1, 0, 0]" } }
          N: { tipo: inteiro, padrao: 2, min: 1, max: 6, grip: { eixo: x, posicao: "[N, 0, 0]" } }
          D: { tipo: comprimento, padrao: 0.6, valores: [0.4, 0.6, 0.8, 1.0], grip: { eixo: y, posicao: "[0, D, 0]" } }
        fluxo:
          - expor_solido("s", prisma(1, 1, 1))
        """);

    private static ParameterDef L => Pv.FindParameter("L")!;

    [Theory]
    [InlineData("1,50", 1.50)]
    [InlineData("1.50", 1.50)]
    [InlineData(" 1,5 ", 1.50)]
    [InlineData("1,50 m", 1.50)]
    [InlineData("1.5m", 1.50)]
    [InlineData("2", 2.00)]
    public void Numero_digitado_e_o_valor_final_com_virgula_ou_ponto(string text, double expected)
    {
        Assert.True(DeviceEditing.TryParseGripInput(L, text, out var value, out var message));
        Assert.Equal(expected, value, 9);
        Assert.Equal("", message);
    }

    [Fact]
    public void Grip_simetrico_recebe_a_dimensao_final_nao_o_deslocamento()
    {
        // L tem grip com fator 2: arrastar 0,15 m soma 0,30 m; digitar 1,50 deixa L = 1,50
        Assert.Equal(2, L.Grip!.Factor);
        Assert.True(DeviceEditing.TryParseGripInput(L, "1,50", out var value, out _));
        var values = DeviceEditing.Edit(Pv, Pv.Evaluate().Parameters, "L", value, out _);
        Assert.Equal(1.50, (double)Pv.Evaluate(values, "L").Parameters["L"], 9);
    }

    [Theory]
    [InlineData("0,5", 0.80, "mínimo")]
    [InlineData("9", 3.00, "máximo")]
    public void Fora_dos_limites_fica_no_limite_e_avisa(string text, double expected, string reason)
    {
        Assert.True(DeviceEditing.TryParseGripInput(L, text, out var value, out var message));
        Assert.Equal(expected, value, 9);
        Assert.Contains(reason, message);
        Assert.StartsWith("L: ", message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,234.5")]
    [InlineData("")]
    [InlineData("1,5 cm")]
    public void Texto_que_nao_e_numero_e_recusado(string text)
    {
        Assert.False(DeviceEditing.TryParseGripInput(L, text, out _, out var message));
        Assert.Contains("não é um número", message);
    }

    [Fact]
    public void Angulo_inteiro_e_lista_respeitam_o_parametro()
    {
        var ang = Mixed.FindParameter("Ang")!;
        Assert.True(DeviceEditing.TryParseGripInput(ang, "45°", out var a, out _));
        Assert.Equal(45, a, 9);
        Assert.True(DeviceEditing.TryParseGripInput(ang, "-30,5 grau", out a, out _));
        Assert.Equal(-30.5, a, 9);

        Assert.True(DeviceEditing.TryParseGripInput(Mixed.FindParameter("N")!, "3,6", out var n, out var note));
        Assert.Equal(4, n);
        Assert.Contains("arredondado", note);

        Assert.True(DeviceEditing.TryParseGripInput(Mixed.FindParameter("D")!, "0,75", out var d, out note));
        Assert.Equal(0.8, d, 9);
        Assert.Contains("valor permitido mais próximo", note);
    }

    [Fact]
    public void Linha_de_comando_mostra_nome_valor_e_unidade_em_pt_br()
    {
        Assert.Equal("L = 1,20 m — arraste ou digite o novo valor", DeviceEditing.GripPrompt(L, 1.2));
        Assert.Equal("CotaFundo = 100,00 m — arraste ou digite o novo valor", DeviceEditing.GripPrompt(Pv.FindParameter("CotaFundo")!, 100));
        Assert.Equal("45,0°", DeviceEditing.FormatGripValue(Mixed.FindParameter("Ang")!, 45));
        Assert.Equal("3", DeviceEditing.FormatGripValue(Mixed.FindParameter("N")!, 3));
        Assert.Equal("1,235 m", DeviceEditing.FormatGripValue(L, 1.2346));
    }

    [Fact]
    public void Arrasto_continua_com_fator_e_limites()
    {
        Assert.Equal(1.50, DeviceEditing.GripValue(L, 1.20, new Hydro.Core.Csg.Vec3(0.15, 0, 0)), 9);
        Assert.Equal(3.00, DeviceEditing.GripValue(L, 1.20, new Hydro.Core.Csg.Vec3(5, 0, 0)), 9);
    }
}
