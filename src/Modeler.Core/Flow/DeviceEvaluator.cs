using System.Text.RegularExpressions;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;
using Modeler.Core.Results;

namespace Modeler.Core.Flow;

/// <summary>Avalia uma definição: parâmetros → variáveis → validações → fluxo → saídas.</summary>
internal static partial class DeviceEvaluator
{
    /// <summary>Limite de iterações somadas de todos os 'para cada' (proteção contra laços enormes).</summary>
    public const int MaxIterations = 100_000;

    public static DeviceInstance Evaluate(DeviceDefinition definition, IReadOnlyDictionary<string, object>? input)
    {
        var functions = FunctionRegistry.Default;
        var parameters = ResolveParameters(definition, input);

        var globals = new Dictionary<string, object>(parameters, StringComparer.Ordinal);
        var globalScope = new GlobalScope(globals, functions);
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var variable in definition.Variables)
        {
            var value = variable.Expression.Evaluate(globalScope);
            globals[variable.Name] = value;
            variables[variable.Name] = value;
        }

        var warnings = new List<string>();
        foreach (var rule in definition.Validations)
        {
            if (!rule.Condition.EvaluateBool(globalScope, "a condição da validação"))
            {
                var message = Interpolate(rule.Message, globalScope);
                if (rule.Level == ValidationLevel.Aviso)
                {
                    warnings.Add(message);
                }
                else
                {
                    throw new EvaluationException(message, rule.Location, "validacoes");
                }
            }
        }

        var scope = new FlowScope(globals, functions);
        var iterations = 0;
        Execute(definition.Flow, scope, ref iterations);

        var solids = scope.Solids.Concat(EvaluateSolids(definition.Outputs, scope)).ToList();
        var duplicated = solids.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            throw new EvaluationException($"sólido '{duplicated.Key}' definido mais de uma vez", null, "saidas.solidos");
        }

        var connectors = scope.Connectors.Concat(EvaluateConnectors(definition.Outputs, scope)).ToList();
        var hydraulics = definition.Outputs.Hydraulics.Select(h => EvaluateHydraulic(h, scope)).ToList();
        var quantities = definition.Outputs.Quantities
            .Select(q => new Quantity(q.Name, q.Value.EvaluateNumber(scope, $"o quantitativo '{q.Name}'"), q.Unit, q.Description))
            .ToList();
        var grips = EvaluateGrips(definition, globalScope);

        return new DeviceInstance(
            definition,
            parameters,
            variables,
            new NamedList<NamedSolid>(solids.Where(s => s.Node is not EmptyNode)),
            new NamedList<Connector>(connectors),
            hydraulics,
            new NamedList<Quantity>(quantities),
            grips,
            warnings);
    }

    private static Dictionary<string, object> ResolveParameters(DeviceDefinition definition, IReadOnlyDictionary<string, object>? input)
    {
        if (input is not null)
        {
            var unknown = input.Keys.Where(k => definition.FindParameter(k) is null).ToList();
            if (unknown.Count > 0)
            {
                throw new ParameterValidationException(unknown[0],
                    $"parâmetro desconhecido; parâmetros de '{definition.Id}': {string.Join(", ", definition.Parameters.Select(p => p.Name))}");
            }
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            result[parameter.Name] = input is not null && input.TryGetValue(parameter.Name, out var raw)
                ? parameter.Coerce(raw)
                : parameter.Default;
        }

        return result;
    }

    private static void Execute(IReadOnlyList<Statement> statements, FlowScope scope, ref int iterations)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignStatement assign:
                    scope.Set(assign.Name, assign.Value.Evaluate(scope));
                    break;
                case CallStatement call:
                    call.Call.Evaluate(scope);
                    break;
                case IfStatement branch:
                    Execute(branch.Condition.EvaluateBool(scope, "a condição de 'se'") ? branch.Then : branch.Else, scope, ref iterations);
                    break;
                case ForEachStatement loop:
                    var source = loop.Source.Evaluate(scope);
                    if (source is not IReadOnlyList<object> items)
                    {
                        throw new EvaluationException(
                            $"'para cada' precisa de uma lista, mas '{loop.Source.Text.Trim()}' é {Values.TypeName(source)}",
                            loop.Source.Location, loop.Field);
                    }

                    var hadPrevious = scope.TryGetLocal(loop.Variable, out var previous);
                    foreach (var item in items)
                    {
                        if (++iterations > MaxIterations)
                        {
                            throw new EvaluationException($"limite de {MaxIterations} iterações excedido", loop.Location, loop.Field);
                        }

                        scope.Set(loop.Variable, item);
                        Execute(loop.Body, scope, ref iterations);
                    }

                    if (hadPrevious)
                    {
                        scope.Set(loop.Variable, previous);
                    }
                    else
                    {
                        scope.Remove(loop.Variable);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<NamedSolid> EvaluateSolids(OutputsDef outputs, FlowScope scope)
    {
        foreach (var output in outputs.Solids)
        {
            var value = output.Solid.Evaluate(scope);
            if (value is not CsgNode node)
            {
                throw new EvaluationException(
                    $"o sólido '{output.Name}' deve resultar em sólido, mas resultou em {Values.TypeName(value)}", output.Location, output.Solid.Field);
            }

            yield return new NamedSolid(output.Name, node, output.Material);
        }
    }

    private static IEnumerable<Connector> EvaluateConnectors(OutputsDef outputs, FlowScope scope)
    {
        foreach (var output in outputs.Connectors)
        {
            var position = Vector(output.Position, scope, "posicao");
            var direction = Vector(output.Direction, scope, "direcao");
            if (direction.Length < 1e-12)
            {
                throw new EvaluationException("direcao do conector não pode ser o vetor nulo", output.Direction.Location, output.Direction.Field);
            }

            var diameter = output.Diameter.EvaluateNumber(scope, "o diâmetro do conector");
            if (diameter <= 0)
            {
                throw new EvaluationException("diâmetro do conector deve ser maior que zero", output.Diameter.Location, output.Diameter.Field);
            }

            yield return new Connector(output.Name, position, direction.Normalized(), diameter, output.Filter);
        }
    }

    private static HydraulicNode EvaluateHydraulic(HydraulicDef definition, FlowScope scope) => definition switch
    {
        SwmmJunctionDef j => new SwmmJunction(
            j.InvertElevation.EvaluateNumber(scope, "cota_fundo"),
            NonNegative(j.MaxDepth, scope, "profundidade_maxima"),
            j.InitialDepth is null ? 0 : NonNegative(j.InitialDepth, scope, "profundidade_inicial"),
            j.SurchargeDepth is null ? 0 : NonNegative(j.SurchargeDepth, scope, "sobrecarga"),
            j.PondedArea is null ? 0 : NonNegative(j.PondedArea, scope, "area_alagamento")),
        _ => throw new EvaluationException($"nó hidráulico não suportado: {definition.GetType().Name}", definition.Location, "saidas.hidraulica"),
    };

    private static IReadOnlyList<GripInstance> EvaluateGrips(DeviceDefinition definition, IEvaluationContext scope)
    {
        var grips = new List<GripInstance>();
        foreach (var parameter in definition.Parameters)
        {
            if (parameter.Grip is not { } grip)
            {
                continue;
            }

            var position = grip.Position is null ? Vec3.Zero : Vector(grip.Position, scope, "posição do grip");
            var direction = grip.Axis switch
            {
                "x" => Vec3.UnitX,
                "y" => Vec3.UnitY,
                _ => Vec3.UnitZ,
            };
            grips.Add(new GripInstance(parameter.Name, position, direction, grip.Factor));
        }

        return grips;
    }

    private static double NonNegative(Expression expression, IEvaluationContext scope, string what)
    {
        var value = expression.EvaluateNumber(scope, what);
        return value >= 0
            ? value
            : throw new EvaluationException($"{what} não pode ser negativa ({Values.FormatNumber(value)})", expression.Location, expression.Field);
    }

    private static Vec3 Vector(Expression expression, IEvaluationContext scope, string what)
    {
        var value = expression.Evaluate(scope);
        return Values.TryToVec3(value, out var v)
            ? v
            : throw new EvaluationException($"{what} deve ser [x, y, z], mas é {Values.TypeName(value)}", expression.Location, expression.Field);
    }

    /// <summary>Substitui <c>{nome}</c> pelo valor do parâmetro/variável.</summary>
    private static string Interpolate(string message, IEvaluationContext scope) =>
        PlaceholderRegex().Replace(message, m =>
            scope.TryGetValue(m.Groups[1].Value, out var value) ? Values.Format(value) : m.Value);

    [GeneratedRegex(@"\{([\p{L}_][\p{L}\p{Nd}_]*)\}")]
    private static partial Regex PlaceholderRegex();
}
