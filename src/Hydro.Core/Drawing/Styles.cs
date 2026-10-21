using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hydro.Core.Drawing;

/// <summary>Linha da tabela de componentes de um estilo (estudo C §3.1): casada pelo nome com o sólido
/// exposto pela definição (<c>expor_solido("concreto", ...)</c> → componente "concreto"). "Visível"
/// é diferente de camada desligada: o componente invisível nem entra no bloco. Campos nulos usam o
/// padrão (camada <c>HY-&lt;NOME&gt;</c>, cor pela camada, material do sólido).</summary>
public sealed record StyleComponent
{
    public string Name { get; init; } = "*";

    public bool Visible { get; init; } = true;

    public string? Layer { get; init; }

    /// <summary>Cor ACI (1 a 255); nula = da camada.</summary>
    public short? Color { get; init; }

    public string? Material { get; init; }

    public string? Linetype { get; init; }

    /// <summary>Transparência em % (0 a 90); nula = da camada.</summary>
    public int? Transparency { get; init; }

    public string? Description { get; init; }
}

/// <summary>Estilo de dispositivo: a tabela de componentes. O componente "*" vale para os sólidos sem
/// linha própria.</summary>
public sealed class DeviceStyle
{
    public const string DefaultName = "Padrão";

    public string Name { get; set; } = DefaultName;

    public string? Description { get; set; }

    public List<StyleComponent> Components { get; set; } = new();

    /// <summary>Componente do sólido (pelo nome, sem diferenciar maiúsculas), o genérico "*" ou nulo.</summary>
    public StyleComponent? Resolve(string solidName) =>
        Components.FirstOrDefault(c => string.Equals(c.Name, solidName, StringComparison.OrdinalIgnoreCase))
        ?? Components.FirstOrDefault(c => c.Name == "*");

    public string ToJson() => JsonSerializer.Serialize(this, StyleJson.Options);

    public static DeviceStyle FromJson(string json) => JsonSerializer.Deserialize<DeviceStyle>(json, StyleJson.Options) ?? new DeviceStyle();
}

public enum ProjectionSide
{
    Ambos,
    Esquerdo,
    Direito,
}

/// <summary>Hachura da face cortada.</summary>
public sealed record HatchSettings
{
    public string Pattern { get; init; } = "ANSI31";

    public double Scale { get; init; } = 0.5;

    /// <summary>Ângulo do padrão, em graus.</summary>
    public double Angle { get; init; }
}

/// <summary>Estilo de projeção em ProfileView (estudo C §4.2): componentes corte (arestas da face
/// cortada pelo plano do alinhamento), hachura da face cortada, contorno externo (silhueta do
/// dispositivo na vista), linhas ocultas (arestas fora do plano) e eixo, cada um com camada, cor,
/// tipo de linha e visibilidade; mais o lado e a distância máxima padrão.</summary>
public sealed class ProjectionStyle
{
    public const string DefaultName = "Padrão";

    public static readonly IReadOnlyList<string> ComponentNames = new[] { "corte", "hachura", "contorno", "ocultas", "eixo" };

    public string Name { get; set; } = DefaultName;

    public string? Description { get; set; }

    public List<StyleComponent> Components { get; set; } = Defaults();

    public HatchSettings Hatch { get; set; } = new();

    public ProjectionSide Side { get; set; } = ProjectionSide.Ambos;

    /// <summary>Distância máxima do dispositivo ao alinhamento (m); acima dela ele não aparece.</summary>
    public double MaxDistance { get; set; } = 10;

    public StyleComponent Component(string name) =>
        Components.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Defaults().First(c => c.Name == name);

    public static List<StyleComponent> Defaults() => new()
    {
        new() { Name = "corte", Layer = "HY-PERFIL-CORTE", Color = 7, Description = "arestas da face cortada pelo plano do alinhamento" },
        new() { Name = "hachura", Layer = "HY-PERFIL-HACHURA", Color = 8, Description = "hachura da face cortada" },
        new() { Name = "contorno", Layer = "HY-PERFIL-CONTORNO", Color = 4, Description = "silhueta das peças na vista" },
        new() { Name = "ocultas", Layer = "HY-PERFIL-OCULTAS", Color = 9, Linetype = "HIDDEN", Visible = false, Description = "arestas das peças fora do plano" },
        new() { Name = "eixo", Layer = "HY-PERFIL-EIXO", Color = 1, Linetype = "CENTER", Description = "eixo vertical do dispositivo" },
    };

    public string ToJson() => JsonSerializer.Serialize(this, StyleJson.Options);

    public static ProjectionStyle FromJson(string json)
    {
        var style = JsonSerializer.Deserialize<ProjectionStyle>(json, StyleJson.Options) ?? new ProjectionStyle();
        // componentes que faltam (estilo gravado por uma versão anterior) voltam com o padrão
        foreach (var missing in Defaults().Where(d => style.Components.All(c => !string.Equals(c.Name, d.Name, StringComparison.OrdinalIgnoreCase))))
        {
            style.Components.Add(missing);
        }

        return style;
    }
}

internal static class StyleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
