using Modeler.Core.Definition;
using Modeler.Core.Errors;

namespace Modeler.Core.Tests;

/// <summary>Erros de definição devem apontar arquivo, linha, coluna e campo.</summary>
public class DefinitionErrorTests
{
    private const string Header = """
        dispositivo: teste
        versao: 1.0.0
        categoria: pontual
        parametros:
          A: { tipo: comprimento, padrao: 1.0 }
        """;

    private static DefinitionException Fails(string yaml) =>
        Assert.ThrowsAny<DefinitionException>(() => DeviceDefinition.Parse(yaml, "teste.yaml"));

    [Fact]
    public void Expressao_invalida_aponta_linha_coluna_e_campo()
    {
        var ex = Fails(Header + """

            variaveis:
              X: A * 2
              Y: A - * 3
            """);

        Assert.Equal(8, ex.Line);
        Assert.Equal(10, ex.Column); // o '*' inesperado
        Assert.Equal("variaveis.Y", ex.Field);
        Assert.StartsWith("teste.yaml(8,10): variaveis.Y: expressão inválida", ex.Message);
        Assert.Contains("'*' inesperado", ex.Message);
    }

    [Fact]
    public void Dependencia_circular_e_detectada()
    {
        var ex = Assert.IsType<CircularDependencyException>(Fails(Header + """

            variaveis:
              P: Q + 1
              Q: R * 2
              R: P - A
              S: A + 1
            """));

        Assert.Equal(new[] { "P", "Q", "R", "P" }, ex.Cycle);
        Assert.Contains("dependência circular: P -> Q -> R -> P", ex.Message);
        Assert.Equal(7, ex.Line);
    }

    [Fact]
    public void Auto_referencia_e_ciclo()
    {
        var ex = Assert.IsType<CircularDependencyException>(Fails(Header + """

            variaveis:
              S: S + 1
            """));
        Assert.Equal(new[] { "S", "S" }, ex.Cycle);
    }

    [Fact]
    public void Variaveis_sao_resolvidas_em_qualquer_ordem_de_declaracao()
    {
        var def = DeviceDefinition.Parse(Header + """

            variaveis:
              C: B * 2
              B: A + 1
            """);
        Assert.Equal(new[] { "B", "C" }, def.Variables.Select(v => v.Name));
        Assert.Equal(4.0, def.Evaluate().Variable("C"), 9);
    }

    [Fact]
    public void Erro_de_sintaxe_YAML_aponta_a_linha()
    {
        var ex = Fails("""
            dispositivo: teste
            versao: 1.0.0
            categoria: pontual
            parametros:
              A:
                tipo: comprimento
               padrao: 1
            """);

        Assert.Equal(7, ex.Line);
        Assert.Contains("erro de sintaxe YAML", ex.Message);
        Assert.StartsWith("teste.yaml(7,", ex.Message);
    }

    [Fact]
    public void Campo_desconhecido_aponta_linha_e_sugere()
    {
        var ex = Fails("""
            dispositivo: teste
            versao: 1.0.0
            categoria: pontual
            parametros:
              A:
                tipo: comprimento
                padrao: 1
                minimo: 0.5
            """);

        Assert.Equal(8, ex.Line);
        Assert.Equal("parametros.A.minimo", ex.Field);
        Assert.Contains("campo desconhecido 'minimo' (você quis dizer 'min'?)", ex.Message);
    }

    [Fact]
    public void Nome_desconhecido_aponta_coluna_e_sugere()
    {
        var ex = Fails(Header + """

            variaveis:
              X: 2 * Ab
            """);

        Assert.Equal(7, ex.Line);
        Assert.Equal(10, ex.Column);
        Assert.Contains("nome desconhecido 'Ab' (você quis dizer 'A'?)", ex.Message);
    }

    [Fact]
    public void Funcao_desconhecida_e_acusada_na_carga()
    {
        var ex = Fails(Header + """

            variaveis:
              X: raizq(A)
            """);
        Assert.Contains("função desconhecida 'raizq' (você quis dizer 'raiz'?)", ex.Message);
    }

    [Theory]
    [InlineData("  A: { tipo: comprimento, padrao: 5, max: 2 }", "parametros.A.padrao", "valor padrão inválido")]
    [InlineData("  A: { tipo: comprimento, padrao: '1,5' }", "parametros.A.padrao", "use ponto como separador decimal")]
    [InlineData("  A: { tipo: comprimento, padrao: 1, unidade: cm }", "parametros.A.unidade", "não suportada")]
    [InlineData("  A: { tipo: area, padrao: 1, unidade: m }", "parametros.A.unidade", "o tipo exige 'm2'")]
    [InlineData("  A: { tipo: decimal, padrao: 1 }", "parametros.A.tipo", "tipo 'decimal' desconhecido")]
    [InlineData("  A: { tipo: texto, padrao: x, min: 1 }", "parametros.A.min", "só se aplica a parâmetros numéricos")]
    [InlineData("  A: { tipo: comprimento }", "parametros.A.padrao", "campo obrigatório 'padrao' ausente")]
    [InlineData("  A: { tipo: comprimento, padrao: 1, min: 3, max: 2 }", "parametros.A.min", "maior que max")]
    [InlineData("  ou: { tipo: comprimento, padrao: 1 }", "parametros.ou", "palavra reservada")]
    public void Erros_de_parametro(string line, string field, string fragment)
    {
        var ex = Fails("dispositivo: teste\nversao: 1.0.0\ncategoria: pontual\nparametros:\n" + line + "\n");
        Assert.Equal(field, ex.Field);
        Assert.Equal(5, ex.Line);
        Assert.Contains(fragment, ex.Message);
    }

    [Theory]
    [InlineData("categoria: pontual\nversao: 1.0.0\n", "dispositivo", "campo obrigatório 'dispositivo' ausente")]
    [InlineData("dispositivo: x\nversao: 1.0.0\ncategoria: aereo\n", "categoria", "use 'pontual' ou 'linear'")]
    [InlineData("dispositivo: pv retangular\nversao: 1.0.0\ncategoria: pontual\n", "dispositivo", "só letras, números")]
    public void Erros_de_cabecalho(string yaml, string field, string fragment)
    {
        var ex = Fails(yaml);
        Assert.Equal(field, ex.Field);
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Arquivo_vazio()
    {
        Assert.Contains("vazio", Fails("").Message);
    }
}
