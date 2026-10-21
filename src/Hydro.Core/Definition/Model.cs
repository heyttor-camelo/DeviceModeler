using Hydro.Core.Text;
using Hydro.Core.Errors;
using Hydro.Core.Expressions;

namespace Hydro.Core.Definition;

/// <summary>Variável calculada a partir de parâmetros e de outras variáveis.</summary>
public sealed record VariableDef(string Name, Expression Expression, string? Unit, string? Description);

public enum ValidationLevel
{
    Erro,
    Aviso,
}

/// <summary>Regra verificada após as variáveis. <c>Message</c> é um modelo com campos <c>{expressão:formato}</c>.</summary>
public sealed record ValidationDef(Expression Condition, Template Message, ValidationLevel Level, SourceLocation Location);

/// <summary>Instrução do fluxo.</summary>
public abstract record Statement(SourceLocation Location, string Field);

/// <summary><c>nome = expressão</c></summary>
public sealed record AssignStatement(string Name, Expression Value, SourceLocation Location, string Field) : Statement(Location, Field);

/// <summary>Chamada de saída: <c>expor_solido(...)</c> ou <c>conector(...)</c>.</summary>
public sealed record CallStatement(Expression Call, SourceLocation Location, string Field) : Statement(Location, Field);

public sealed record IfStatement(Expression Condition, IReadOnlyList<Statement> Then, IReadOnlyList<Statement> Else, SourceLocation Location, string Field)
    : Statement(Location, Field);

/// <summary><c>para cada: c em Lista</c> + <c>faca: [...]</c></summary>
public sealed record ForEachStatement(string Variable, Expression Source, IReadOnlyList<Statement> Body, SourceLocation Location, string Field)
    : Statement(Location, Field);

/// <summary><c>escolha: valor</c> + <c>casos: {chave: [...]}</c> + <c>padrao: [...]</c>. As chaves são
/// números ou textos, comparadas com a mesma regra de <c>==</c>.</summary>
public sealed record ChooseStatement(
    Expression Value,
    IReadOnlyList<(object Key, IReadOnlyList<Statement> Body)> Cases,
    IReadOnlyList<Statement>? Default,
    SourceLocation Location,
    string Field) : Statement(Location, Field);

/// <summary><c>mensagem: "texto {x}"</c>: registra uma linha em <c>inst.Log</c> (não é aviso).</summary>
public sealed record MessageStatement(Template Text, SourceLocation Location, string Field) : Statement(Location, Field);

public enum PresetEditMode
{
    /// <summary>Editar uma dimensão do código muda o código para "generico" (a edição fica).</summary>
    Generico,

    /// <summary>Editar uma dimensão do código é desfeito: o código manda (comportamento do SOLIDOS).</summary>
    Reaplicar,
}

/// <summary>Bloco <c>predefinicoes</c>: um parâmetro de texto escolhe um código do catálogo, que
/// preenche as dimensões. O código <see cref="Generic"/> deixa as dimensões livres.</summary>
public sealed class PresetsDef
{
    public const string Generic = "generico";

    internal PresetsDef(string parameter, PresetEditMode mode, IReadOnlyDictionary<string, object> common,
        IReadOnlyList<(string Code, IReadOnlyDictionary<string, object> Values)> items)
    {
        Parameter = parameter;
        Mode = mode;
        Common = common;
        Items = items;
    }

    /// <summary>Parâmetro de texto com o código (os valores permitidos são "generico" + os códigos).</summary>
    public string Parameter { get; }

    public PresetEditMode Mode { get; }

    /// <summary>Valores aplicados por todos os códigos (antes dos do item).</summary>
    public IReadOnlyDictionary<string, object> Common { get; }

    public IReadOnlyList<(string Code, IReadOnlyDictionary<string, object> Values)> Items { get; }

    public IEnumerable<string> Codes => Items.Select(i => i.Code);

    /// <summary>Parâmetros que algum código preenche.</summary>
    public IEnumerable<string> Covered => Common.Keys.Concat(Items.SelectMany(i => i.Values.Keys)).Distinct(StringComparer.Ordinal);

    /// <summary>Valores do código (comuns + item); nulo para "generico" ou código desconhecido.</summary>
    public IReadOnlyDictionary<string, object>? ValuesOf(string code)
    {
        var item = Items.FirstOrDefault(i => i.Code == code);
        if (item.Code is null)
        {
            return null;
        }

        var result = new Dictionary<string, object>(Common, StringComparer.Ordinal);
        foreach (var (name, value) in item.Values)
        {
            result[name] = value;
        }

        return result;
    }
}

/// <summary>Regra de <c>ao_editar</c>: quando <see cref="Parameter"/> é editado, cada atribuição
/// recalcula outro parâmetro antes da avaliação (redundâncias como CotaFundo ↔ H).</summary>
public sealed record EditRuleDef(string Parameter, IReadOnlyList<(string Target, Expression Value)> Assignments, SourceLocation Location);

/// <summary>Modelos de texto da definição (<c>modelos</c>): nome da instância e dica do cursor.</summary>
public sealed record TemplatesDef(Template? Name, Template? Tooltip);

public sealed record SolidOutputDef(string Name, Expression Solid, string? Material, SourceLocation Location);

public sealed record ConnectorOutputDef(string Name, Expression Position, Expression Direction, Expression Diameter, string? Filter, SourceLocation Location);

public abstract record HydraulicDef(SourceLocation Location);

public sealed record SwmmJunctionDef(
    Expression InvertElevation,
    Expression MaxDepth,
    Expression? InitialDepth,
    Expression? SurchargeDepth,
    Expression? PondedArea,
    SourceLocation Location) : HydraulicDef(Location);

public sealed record QuantityDef(string Name, Expression Value, string Unit, string? Description, SourceLocation Location);

/// <summary>Seção <c>saidas</c>. Avaliada no fim, enxergando parâmetros, variáveis e nomes do fluxo.</summary>
public sealed class OutputsDef
{
    public IReadOnlyList<SolidOutputDef> Solids { get; internal set; } = Array.Empty<SolidOutputDef>();

    public IReadOnlyList<ConnectorOutputDef> Connectors { get; internal set; } = Array.Empty<ConnectorOutputDef>();

    public IReadOnlyList<HydraulicDef> Hydraulics { get; internal set; } = Array.Empty<HydraulicDef>();

    public IReadOnlyList<QuantityDef> Quantities { get; internal set; } = Array.Empty<QuantityDef>();
}
