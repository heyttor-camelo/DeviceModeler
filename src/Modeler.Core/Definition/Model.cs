using Modeler.Core.Errors;
using Modeler.Core.Expressions;

namespace Modeler.Core.Definition;

/// <summary>Variável calculada a partir de parâmetros e de outras variáveis.</summary>
public sealed record VariableDef(string Name, Expression Expression, string? Unit, string? Description);

public enum ValidationLevel
{
    Erro,
    Aviso,
}

/// <summary>Regra verificada após as variáveis. <c>Message</c> aceita <c>{nome}</c> para interpolar valores.</summary>
public sealed record ValidationDef(Expression Condition, string Message, ValidationLevel Level, SourceLocation Location);

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
