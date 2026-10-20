using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;

namespace Modeler.Core.Editing;

/// <summary>Operações de edição que não dependem do CAD: grips e reconciliação de valores
/// gravados com uma versão nova da definição.</summary>
public static class DeviceEditing
{
    /// <summary>Novo valor de um parâmetro após arrastar seu grip de <paramref name="localDelta"/>
    /// (no sistema local). Projeta o deslocamento no eixo do grip, multiplica pelo fator,
    /// respeita min/max e, havendo lista de valores, escolhe o valor permitido mais próximo.</summary>
    public static double GripValue(ParameterDef parameter, double current, Vec3 localDelta)
    {
        var grip = parameter.Grip ?? throw new InvalidOperationException($"parâmetro '{parameter.Name}' não tem grip");
        var axis = grip.Axis switch
        {
            "x" => localDelta.X,
            "y" => localDelta.Y,
            _ => localDelta.Z,
        };

        var value = current + (axis * grip.Factor);
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
