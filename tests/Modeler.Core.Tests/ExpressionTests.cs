using Modeler.Core.Csg;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;

namespace Modeler.Core.Tests;

public class ExpressionTests
{
    private static object Eval(string text, Dictionary<string, object>? values = null) => Expression.Evaluate(text, values);

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("2 ^ -1", 0.5)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 4", 2)]
    [InlineData("1.5e1 + 0.5", 15.5)]
    public void Aritmetica_respeita_precedencia(string text, double expected) =>
        Assert.Equal(expected, (double)Eval(text), 9);

    [Theory]
    [InlineData("min(3, 1, 2)", 1)]
    [InlineData("max([3, 1, 2])", 3)]
    [InlineData("abs(-2.5)", 2.5)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("round(2.3456, 2)", 2.35)]
    [InlineData("round(2.5)", 3)]
    [InlineData("ceil(1.01)", 2)]
    [InlineData("floor(1.99)", 1)]
    [InlineData("sin(pi / 2)", 1)]
    [InlineData("cos(0)", 1)]
    [InlineData("tan(rad(45))", 1)]
    [InlineData("graus(pi)", 180)]
    [InlineData("soma(intervalo(0, 4))", 6)]
    [InlineData("tamanho([1, 2, 3])", 3)]
    public void Funcoes_matematicas(string text, double expected) =>
        Assert.Equal(expected, (double)Eval(text), 9);

    [Theory]
    [InlineData("1 < 2 e 2 < 3", true)]
    [InlineData("1 > 2 ou 2 > 1", true)]
    [InlineData("nao (1 == 1)", false)]
    [InlineData("0.1 + 0.2 == 0.3", true)]
    [InlineData("\"T600\" == 'T600'", true)]
    [InlineData("3 != 3", false)]
    [InlineData("2 >= 2 and true", true)]
    public void Comparacoes_e_logica(string text, bool expected) =>
        Assert.Equal(expected, (bool)Eval(text));

    [Fact]
    public void Se_avalia_somente_o_ramo_escolhido()
    {
        // 1/x não é avaliado quando x == 0
        Assert.Equal(0.0, Eval("se(x > 0, 1 / x, 0)", new() { ["x"] = 0.0 }));
        Assert.Equal(0.5, Eval("se(x > 0, 1 / x, 0)", new() { ["x"] = 2.0 }));
    }

    [Fact]
    public void Registros_listas_e_pontos()
    {
        var c = new Dictionary<string, object> { ["diametro"] = 0.6, ["angulo"] = 90.0 };
        Assert.Equal(0.7, (double)Eval("c.diametro + 0.1", new() { ["c"] = c }), 9);
        Assert.Equal(20.0, Eval("[10, 20, 30][1]"));
        Assert.Equal(3.0, Eval("ponto(1, 2, 3).z"));
        var p = (IReadOnlyList<object>)Eval("polar(90, 2)");
        Assert.Equal(0, (double)p[0], 9);
        Assert.Equal(2, (double)p[1], 9);
    }

    [Fact]
    public void Funcoes_geometricas_montam_arvore_csg()
    {
        var node = (CsgNode)Eval("subtrair(prisma(2, 2, 1), mover(cilindro(0.5, 1, eixo = \"x\"), [0, 0, 0.25]))");
        var subtract = Assert.IsType<SubtractNode>(node);
        Assert.IsType<PrismNode>(subtract.Source);
        var move = Assert.IsType<TranslateNode>(Assert.Single(subtract.Tools));
        var cylinder = Assert.IsType<CylinderNode>(move.Child);
        Assert.Equal(Vec3.UnitX, cylinder.Axis);
        Assert.Equal(new Vec3(0, 0, 0.25), move.Offset);
    }

    [Fact]
    public void Array_z_e_vazio()
    {
        var array = Assert.IsType<ArrayNode>(Eval("array_z(prisma(1, 1, 0.1), 4, 0.3)"));
        Assert.Equal(4, array.Count);
        Assert.Equal(new Vec3(0, 0, 0.3), array.Step);
        Assert.IsType<EmptyNode>(Eval("array_z(prisma(1, 1, 0.1), 0, 0.3)"));
        Assert.IsType<PrismNode>(Eval("unir(vazio(), prisma(1, 1, 1))"));
    }

    [Theory]
    [InlineData("1 +", "expressão incompleta")]
    [InlineData("(1 + 2", "esperado ')'")]
    [InlineData("1 = 2", "use '=='")]
    [InlineData("1 < 2 < 3", "encadeadas")]
    [InlineData("3 $ 4", "caractere inesperado '$'")]
    [InlineData("'aberto", "aspas")]
    public void Erros_de_sintaxe_sao_claros(string text, string fragment)
    {
        var ex = Assert.Throws<DefinitionException>(() => Expression.Parse(text));
        Assert.Contains(fragment, ex.Message);
    }

    [Theory]
    [InlineData("1 / 0", "divisão por zero")]
    [InlineData("sqrt(-1)", "raiz de número negativo")]
    [InlineData("xyz + 1", "nome desconhecido 'xyz'")]
    [InlineData("foo(1)", "função desconhecida 'foo'")]
    [InlineData("1 + \"a\" * 2", "deve ser número")]
    [InlineData("prisma(1, 0, 1)", "prisma: 'B' deve ser maior que zero")]
    [InlineData("min(1, x = 2)", "não aceita argumentos nomeados")]
    [InlineData("cilindro(1)", "falta o argumento 'h'")]
    [InlineData("se(1, 2, 3)", "deve ser lógica")]
    public void Erros_de_avaliacao_sao_claros(string text, string fragment)
    {
        var ex = Assert.Throws<EvaluationException>(() => Eval(text));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Nomes_livres_excluem_funcoes_e_campos()
    {
        var e = Expression.Parse("max(a, c.diametro) + b[0]");
        Assert.Equal(new[] { "a", "b", "c" }, e.FreeNames.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Ordenacao_por_dependencia_e_ciclos()
    {
        var deps = new Dictionary<string, string[]>
        {
            ["C"] = new[] { "B" },
            ["B"] = new[] { "A", "X" },
            ["A"] = Array.Empty<string>(),
        };
        Assert.True(DependencyResolver.TryOrder(deps.Keys.ToList(), n => deps[n], out var order, out _));
        Assert.Equal(new[] { "A", "B", "C" }, order);

        deps["A"] = new[] { "C" };
        Assert.False(DependencyResolver.TryOrder(deps.Keys.ToList(), n => deps[n], out _, out var cycle));
        Assert.Equal(new[] { "C", "B", "A", "C" }, cycle);
    }
}
