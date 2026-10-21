using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Errors;
using Hydro.Core.Expressions;
using Hydro.Core.Flow;
using Hydro.Core.Results;

namespace Hydro.Core.Editing;

/// <summary>Operações de edição que não dependem do CAD: grips e reconciliação de valores
/// gravados com uma versão nova da definição.</summary>
public static class DeviceEditing
{
    /// <summary>Novo valor de um parâmetro após arrastar seu grip de <paramref name="localDelta"/>
    /// (no sistema local). Projeta o deslocamento no eixo do grip, multiplica pelo fator,
    /// respeita min/max e, havendo lista de valores, escolhe o valor permitido mais próximo.</summary>
    public static double GripValue(ParameterDef parameter, double current, Vec3 localDelta, GripInstance? instance = null)
    {
        var grip = parameter.Grip ?? throw new InvalidOperationException($"parâmetro '{parameter.Name}' não tem grip");
        double change;
        switch (grip.Kind)
        {
            case GripKind.Rotacionar when instance is not null:
                // ângulo em planta entre (grip − centro) e (grip + deslocamento − centro), anti-horário
                var (ax, ay) = (instance.Position.X - instance.Center.X, instance.Position.Y - instance.Center.Y);
                var (bx, by) = (ax + localDelta.X, ay + localDelta.Y);
                change = Math.Atan2((ax * by) - (ay * bx), (ax * bx) + (ay * by)) * 180 / Math.PI * grip.Factor;
                break;
            case GripKind.Perfil:
                change = localDelta.Z * grip.Factor * (grip.Up ? 1 : -1);
                break;
            default:
                var axis = grip.Axis switch
                {
                    "x" => localDelta.X,
                    "y" => localDelta.Y,
                    _ => localDelta.Z,
                };
                change = axis * grip.Factor;
                break;
        }

        var value = current + change;
        if (parameter.Min is { } min)
        {
            value = Math.Max(min, value);
        }

        if (parameter.Max is { } max)
        {
            value = Math.Min(max, value);
        }

        if (parameter.AllowedValues is { Count: > 0 } allowed)
        {
            value = allowed.OfType<double>().OrderBy(a => Math.Abs(a - value)).First();
        }

        return parameter.Type == ParameterType.Inteiro ? Math.Round(value) : value;
    }

    /// <summary>Edita um parâmetro como a paleta ou o grip fariam, antes da avaliação:
    /// <list type="number">
    /// <item>converte e valida o valor;</item>
    /// <item>com <c>predefinicoes</c>: escolher um código aplica as dimensões dele; editar uma dimensão
    /// que o código preenche muda o código para "generico" (ou desfaz a edição, no modo
    /// <c>reaplicar</c>);</item>
    /// <item>roda as regras de <c>ao_editar</c> do parâmetro editado (com <c>editado()</c> valendo o
    /// nome dele).</item>
    /// </list>
    /// O <c>fluxo</c> continua sem efeitos colaterais: tudo acontece aqui, nos valores de entrada.</summary>
    /// <param name="messages">o que mudou além do parâmetro editado.</param>
    public static Dictionary<string, object> Edit(DeviceDefinition definition, IReadOnlyDictionary<string, object> current, string parameter, object? value,
        out List<string> messages)
    {
        messages = new List<string>();
        var edited = definition.FindParameter(parameter)
                     ?? throw new ParameterValidationException(parameter, $"parâmetro desconhecido em '{definition.Id}'");
        var values = DeviceEvaluator.ResolveParameters(definition, current);
        values[parameter] = edited.Coerce(value);

        if (definition.Presets is { } presets)
        {
            if (parameter == presets.Parameter)
            {
                if (presets.ValuesOf((string)values[parameter]) is { } preset)
                {
                    foreach (var (name, v) in preset)
                    {
                        values[name] = v;
                    }
                }
            }
            else
            {
                KeepPreset(presets, values, parameter, messages);
            }
        }

        foreach (var rule in definition.EditRules.Where(r => r.Parameter == parameter))
        {
            var context = new EditContext(values, parameter);
            var results = rule.Assignments.Select(a => (a.Target, Value: a.Value.Evaluate(context))).ToList();
            foreach (var (target, result) in results)
            {
                var targetDef = definition.FindParameter(target)!;
                var coerced = targetDef.Coerce(result);
                if (!SameValue(values[target], coerced))
                {
                    values[target] = coerced;
                    messages.Add($"{target} = {Values.Format(coerced)} (recalculado ao editar {parameter})");
                    if (definition.Presets is { } p && target != p.Parameter)
                    {
                        KeepPreset(p, values, target, messages);
                    }
                }
            }
        }

        return values;
    }

    /// <summary>Uma dimensão do código foi alterada: muda o código para "generico" ou desfaz a alteração.</summary>
    private static void KeepPreset(PresetsDef presets, Dictionary<string, object> values, string changed, List<string> messages)
    {
        if (values[presets.Parameter] is not string code || presets.ValuesOf(code) is not { } preset ||
            !preset.TryGetValue(changed, out var fixedValue) || SameValue(fixedValue, values[changed]))
        {
            return;
        }

        if (presets.Mode == PresetEditMode.Reaplicar)
        {
            values[changed] = fixedValue;
            messages.Add($"{changed} é fixado pelo código {code} ({Values.Format(fixedValue)}); escolha 'generico' para editar");
        }
        else
        {
            values[presets.Parameter] = PresetsDef.Generic;
            messages.Add($"{presets.Parameter}: {code} → {PresetsDef.Generic} ({changed} editado)");
        }
    }

    private static bool SameValue(object a, object b) => (a, b) switch
    {
        (double x, double y) => Values.NearlyEqual(x, y),
        _ => Equals(a, b),
    };

    private sealed class EditContext : IEvaluationContext
    {
        private readonly IReadOnlyDictionary<string, object> _values;

        public EditContext(IReadOnlyDictionary<string, object> values, string edited)
        {
            _values = values;
            Edited = edited;
        }

        public FunctionRegistry Functions => FunctionRegistry.Default;

        public string? Edited { get; }

        public bool TryGetValue(string name, out object value) => _values.TryGetValue(name, out value!);
    }

    /// <summary>Ajusta valores gravados a uma definição (possivelmente mais nova): descarta parâmetros
    /// que não existem mais e troca valores que deixaram de ser válidos pelo padrão.</summary>
    /// <param name="problems">mensagens do que foi descartado/substituído.</param>
    public static Dictionary<string, object> Reconcile(DeviceDefinition definition, IReadOnlyDictionary<string, object> stored, out List<string> problems)
    {
        problems = new List<string>();
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in stored)
        {
            var parameter = definition.FindParameter(name);
            if (parameter is null)
            {
                problems.Add($"parâmetro '{name}' não existe mais na definição {definition.Id} {definition.Version}; valor {Values.Format(value)} descartado");
                continue;
            }

            try
            {
                result[name] = parameter.Coerce(value);
            }
            catch (ParameterValidationException ex)
            {
                problems.Add($"{ex.Reason}; usado o padrão {Values.Format(parameter.Default)}");
            }
        }

        return result;
    }

    /// <summary>Soma <paramref name="dz"/> a todos os parâmetros do tipo cota — usado quando a
    /// entidade foi movida verticalmente no desenho.</summary>
    public static Dictionary<string, object> ShiftElevations(DeviceDefinition definition, IReadOnlyDictionary<string, object> values, double dz)
    {
        var result = new Dictionary<string, object>(values, StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            if (parameter.Type == ParameterType.Cota)
            {
                var current = result.TryGetValue(parameter.Name, out var v) && v is double d ? d : (double)parameter.Default;
                result[parameter.Name] = current + dz;
            }
            else if (parameter.Type == ParameterType.Conexoes && result.TryGetValue(parameter.Name, out var list) && list is IReadOnlyList<object> connections)
            {
                result[parameter.Name] = connections.Select(c => (object)ShiftConnection(c, dz)).ToList();
            }
        }

        return result;
    }

    private static object ShiftConnection(object connection, double dz)
    {
        if (connection is not IReadOnlyDictionary<string, object> record || record.GetValueOrDefault("cota") is not double cota)
        {
            return connection;
        }

        return new Dictionary<string, object>(record, StringComparer.Ordinal) { ["cota"] = cota + dz };
    }
}
