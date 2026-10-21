using System.Text.RegularExpressions;

namespace Hydro.Core.Tests;

/// <summary>Conferências no código do plugin, que não roda fora do Civil 3D. Nasceu do estouro de
/// pilha de out/2026: uma troca em massa de <c>UpgradeOpen()</c> por <c>Writable()</c> fez o próprio
/// <c>Writable()</c> chamar a si mesmo, e o Civil 3D fechava no primeiro dispositivo do HY_REDE (o
/// .NET encerra o processo no estouro de pilha, sem passar por catch nenhum).</summary>
public class PluginSourceTests
{
    private static string PluginFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Hydro.Civil3D");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/Hydro.Civil3D não encontrado acima da pasta dos testes");
    }

    private static IEnumerable<string> PluginSources() =>
        Directory.EnumerateFiles(PluginFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Writable_usa_UpgradeOpen_e_nao_chama_a_si_mesmo()
    {
        var source = File.ReadAllText(Path.Combine(PluginFolder(), "Infra", "DbExtensions.cs"));
        var body = Body(source, source.IndexOf("void Writable(this", StringComparison.Ordinal));
        Assert.Contains("obj.UpgradeOpen()", body);
        Assert.DoesNotContain(".Writable()", body);
    }

    [Fact]
    public void Nenhum_metodo_de_extensao_do_plugin_chama_a_si_mesmo_pelo_parametro()
    {
        var declaration = new Regex(@"static\s+[\w<>\[\]?,\s]+?\s+(\w+)\s*(?:<[^>]*>)?\(\s*this\s+[\w.<>\[\]?]+\s+(\w+)", RegexOptions.Compiled);
        var problems = new List<string>();
        foreach (var file in PluginSources())
        {
            var source = File.ReadAllText(file);
            foreach (Match match in declaration.Matches(source))
            {
                var (name, self) = (match.Groups[1].Value, match.Groups[2].Value);
                if (Body(source, match.Index).Contains($"{self}.{name}(", StringComparison.Ordinal))
                {
                    problems.Add($"{Path.GetFileName(file)}: {name} chama {self}.{name}()");
                }
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>Corpo do método que começa em <paramref name="start"/>: entre as chaves, ou a expressão
    /// até o ';' num método com <c>=&gt;</c>.</summary>
    private static string Body(string source, int start)
    {
        Assert.True(start >= 0, "declaração não encontrada");
        var open = source.IndexOf('{', start);
        var arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            var end = source.IndexOf(';', arrow);
            return source[(arrow + 2)..(end < 0 ? source.Length : end)];
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return source[(open + 1)..i];
            }
        }

        return source[open..];
    }
}
