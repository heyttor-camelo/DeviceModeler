namespace Hydro.Core.Expressions;

/// <summary>Ordenação topológica (DFS) de nomes que dependem uns dos outros.</summary>
public static class DependencyResolver
{
    /// <summary>Ordena <paramref name="names"/> de modo que cada um venha depois das suas dependências.
    /// Dependências fora de <paramref name="names"/> são ignoradas (parâmetros, constantes).
    /// A ordem relativa da declaração é preservada quando não há restrição.</summary>
    /// <returns>falso se houver ciclo; <paramref name="cycle"/> traz o caminho, ex.: [A, B, A].</returns>
    public static bool TryOrder(
        IReadOnlyList<string> names,
        Func<string, IEnumerable<string>> dependencies,
        out List<string> order,
        out List<string> cycle)
    {
        var known = new HashSet<string>(names, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = visitando, 2 = concluído
        var stack = new List<string>();
        var result = new List<string>();
        List<string>? found = null;

        bool Visit(string name)
        {
            if (state.TryGetValue(name, out var s))
            {
                if (s == 2)
                {
                    return true;
                }

                var start = stack.IndexOf(name);
                found = stack.Skip(start).Append(name).ToList();
                return false;
            }

            state[name] = 1;
            stack.Add(name);
            foreach (var dep in dependencies(name))
            {
                if (known.Contains(dep) && !Visit(dep))
                {
                    return false;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
            result.Add(name);
            return true;
        }

        foreach (var name in names)
        {
            if (!Visit(name))
            {
                order = result;
                cycle = found!;
                return false;
            }
        }

        order = result;
        cycle = new List<string>();
        return true;
    }
}
