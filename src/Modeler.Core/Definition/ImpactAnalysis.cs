using Modeler.Core.Expressions;

namespace Modeler.Core.Definition;

/// <summary>Impacto dos parâmetros (técnica 2 do estudo E): quais parâmetros chegam aos sólidos de
/// saída. Mudar só os outros (folga de escavação, lastro, nome, código sem dimensão) não precisa
/// refazer a geometria do bloco, só o registro. Análise estática, para trás, pelo grafo de
/// dependências: atribuições, condições de <c>se</c>/<c>escolha</c> que envolvem algo necessário,
/// fontes de <c>para cada</c>, variáveis e o <c>codigo</c>.</summary>
internal static class ImpactAnalysis
{
    public static IReadOnlySet<string> GeometryParameters(DeviceDefinition definition)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var solid in definition.Outputs.Solids)
        {
            needed.UnionWith(solid.Solid.FreeNames.Keys);
        }

        var variables = definition.Variables.ToDictionary(v => v.Name, v => v.Expression, StringComparer.Ordinal);
        int before;
        do
        {
            before = needed.Count;
            Walk(definition.Flow, needed);
            foreach (var name in needed.ToList())
            {
                if (variables.TryGetValue(name, out var expression))
                {
                    needed.UnionWith(expression.FreeNames.Keys);
                }
                else if (name == "codigo" && definition.CodeTemplate is { } code)
                {
                    foreach (var part in code.Expressions)
                    {
                        needed.UnionWith(part.FreeNames.Keys);
                    }
                }
            }
        }
        while (needed.Count != before);

        return definition.Parameters.Select(p => p.Name).Where(needed.Contains).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Percorre as instruções de trás para frente acrescentando o que alimenta algo
    /// necessário. Devolve verdadeiro se o bloco mexe em algo necessário (ou expõe um sólido).</summary>
    private static bool Walk(IReadOnlyList<Statement> statements, HashSet<string> needed)
    {
        var touches = false;
        for (var i = statements.Count - 1; i >= 0; i--)
        {
            switch (statements[i])
            {
                case AssignStatement assign when needed.Contains(assign.Name):
                    needed.UnionWith(assign.Value.FreeNames.Keys);
                    touches = true;
                    break;
                case CallStatement { Call.Root: CallNode { Name: "expor_solido" } } call:
                    needed.UnionWith(call.Call.FreeNames.Keys);
                    touches = true;
                    break;
                case IfStatement branch:
                    if (Walk(branch.Then, needed) | Walk(branch.Else, needed))
                    {
                        needed.UnionWith(branch.Condition.FreeNames.Keys);
                        touches = true;
                    }

                    break;
                case ChooseStatement choose:
                    var any = false;
                    foreach (var (_, body) in choose.Cases)
                    {
                        any |= Walk(body, needed);
                    }

                    if (choose.Default is { } otherwise)
                    {
                        any |= Walk(otherwise, needed);
                    }

                    if (any)
                    {
                        needed.UnionWith(choose.Value.FreeNames.Keys);
                        touches = true;
                    }

                    break;
                case ForEachStatement loop:
                    // duas passadas: o que o corpo precisa numa volta pode vir da volta anterior
                    var inside = Walk(loop.Body, needed) | Walk(loop.Body, needed);
                    if (inside || needed.Contains(loop.Variable))
                    {
                        needed.UnionWith(loop.Source.FreeNames.Keys);
                        touches = true;
                    }

                    break;
            }
        }

        return touches;
    }
}
