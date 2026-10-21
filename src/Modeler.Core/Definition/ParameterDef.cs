using System.Collections;
using System.Globalization;
using Modeler.Core.Errors;
using Modeler.Core.Expressions;
using Modeler.Core.Results;

namespace Modeler.Core.Definition;

public enum DeviceCategory
{
    /// <summary>Dispositivo inserido num ponto (PV, caixa, boca de bueiro).</summary>
    Pontual,

    /// <summary>Dispositivo ao longo de um caminho (sarjeta, valeta) — geometria por seção na Fase 2.</summary>
    Linear,
}

public enum ParameterType
{
    Numero,
    Inteiro,
    Comprimento,
    Cota,
    Area,
    Volume,
    Vazao,
    Angulo,
    Texto,
    Booleano,

    /// <summary>Lista de conexões de tubo: registros {nome, angulo, cota, diametro, diametro_externo?, ref?}.</summary>
    Conexoes,
}

/// <summary>De onde a Fase 2 pode obter o valor automaticamente (como targets do Subassembly Composer).</summary>
public enum TargetKind
{
    Nenhum,
    Superficie,
    Alinhamento,
    Perfil,
    Polilinha,
    Tubo,
}

/// <summary>Grip linear ligado a um parâmetro. <see cref="Position"/> é avaliada com os valores da instância.</summary>
public sealed record GripDef(string Axis, Expression? Position, double Factor)
{
    public GripKind Kind { get; init; } = GripKind.Esticar;

    /// <summary>Centro da rotação (<c>tipo: rotacionar</c>); nulo = a origem, na elevação do grip.</summary>
    public Expression? Center { get; init; }

    /// <summary>Grip de perfil: arrastar para cima aumenta o valor (falso: para baixo aumenta).</summary>
    public bool Up { get; init; } = true;
}

/// <summary>Tipo do grip, com o desenho do SOLIDOS: esticar = seta verde na direção; rotacionar =
/// círculo ciano ligado ao centro; perfil = losango ciano que só anda na vertical (vista de perfil).</summary>
public enum GripKind
{
    Esticar,
    Rotacionar,
    Perfil,
}

/// <summary>Declaração de um parâmetro de entrada.</summary>
public sealed class ParameterDef
{
    internal ParameterDef(string name, ParameterType type, string unit, SourceLocation location)
    {
        Name = name;
        Type = type;
        Unit = unit;
        Location = location;
    }

    public string Name { get; }

    public ParameterType Type { get; }

    /// <summary>Unidade canônica: m, m2, m3, m3/s, grau, un ou vazio.</summary>
    public string Unit { get; }

    public object Default { get; internal set; } = 0.0;

    public double? Min { get; internal set; }

    public double? Max { get; internal set; }

    /// <summary>Lista de valores permitidos (números ou textos); nula = livre.</summary>
    public IReadOnlyList<object>? AllowedValues { get; internal set; }

    public GripDef? Grip { get; internal set; }

    public TargetKind Target { get; internal set; }

    public string? Label { get; internal set; }

    public string? Group { get; internal set; }

    public string? Description { get; internal set; }

    public SourceLocation Location { get; }

    public bool IsNumeric => Type is not (ParameterType.Texto or ParameterType.Booleano or ParameterType.Conexoes);

    /// <summary>Converte e valida um valor informado. Lança <see cref="ParameterValidationException"/>.</summary>
    public object Coerce(object? raw)
    {
        var value = Convert(Values.Normalize(raw));
        Validate(value);
        return value;
    }

    private object Convert(object? raw)
    {
        switch (Type)
        {
            case ParameterType.Texto:
                return raw as string ?? throw Fail($"esperado texto, recebido {Describe(raw)}");
            case ParameterType.Booleano:
                return raw switch
                {
                    bool b => b,
                    string s when ParseBool(s) is { } b => b,
                    _ => throw Fail($"esperado verdadeiro/falso, recebido {Describe(raw)}"),
                };
            case ParameterType.Conexoes:
                return ConvertConnections(raw);
            default:
                var number = raw switch
                {
                    double d => d,
                    string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                    _ => throw Fail($"esperado número, recebido {Describe(raw)}"),
                };
                if (!double.IsFinite(number))
                {
                    throw Fail("valor não finito");
                }

                if (Type == ParameterType.Inteiro && !Values.NearlyEqual(number, Math.Round(number)))
                {
                    throw Fail($"esperado inteiro, recebido {Values.FormatNumber(number)}");
                }

                return Type == ParameterType.Inteiro ? Math.Round(number) : number;
        }
    }

    private void Validate(object value)
    {
        if (value is double d)
        {
            var belowMin = Min is { } min && d < min && !Values.NearlyEqual(d, min);
            var aboveMax = Max is { } max && d > max && !Values.NearlyEqual(d, max);
            if (belowMin || aboveMax)
            {
                var unit = Unit.Length > 0 ? " " + Unit : "";
                var low = Min is { } a ? Values.FormatNumber(a) : "-∞";
                var high = Max is { } b ? Values.FormatNumber(b) : "+∞";
                throw Fail($"valor {Values.FormatNumber(d)}{unit} fora do intervalo permitido [{low}; {high}]");
            }
        }

        if (AllowedValues is { Count: > 0 } allowed)
        {
            var ok = allowed.Any(a => (a, value) switch
            {
                (double x, double y) => Values.NearlyEqual(x, y),
                (string x, string y) => x == y,
                _ => false,
            });
            if (!ok)
            {
                throw Fail($"valor '{Values.Format(value)}' não está na lista de valores permitidos: {string.Join(", ", allowed.Select(Values.Format))}");
            }
        }
    }

    private IReadOnlyList<object> ConvertConnections(object? raw)
    {
        if (raw is null)
        {
            return Array.Empty<object>();
        }

        if (raw is string || raw is not IEnumerable items)
        {
            throw Fail($"esperado lista de conexões, recebido {Describe(raw)}");
        }

        var result = new List<object>();
        var index = 0;
        foreach (var item in items)
        {
            index++;
            var record = item switch
            {
                ConnectionInput c => new Dictionary<string, object?>
                {
                    ["nome"] = c.Name, ["angulo"] = c.Angle, ["cota"] = c.InvertElevation, ["diametro"] = c.Diameter,
                    ["diametro_externo"] = c.OuterDiameter, ["ref"] = c.Reference,
                },
                IReadOnlyDictionary<string, object> d => d.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                IDictionary<string, object> d => d.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                _ => throw Fail($"conexão {index}: esperado ConnectionInput ou dicionário, recebido {Describe(item)}"),
            };
            result.Add(NormalizeConnection(record, index));
        }

        return result;
    }

    private IReadOnlyDictionary<string, object> NormalizeConnection(Dictionary<string, object?> record, int index)
    {
        double Number(string key)
        {
            if (!record.TryGetValue(key, out var v) || Values.Normalize(v) is not double d || !double.IsFinite(d))
            {
                throw Fail($"conexão {index}: campo '{key}' numérico obrigatório");
            }

            return d;
        }

        var unknown = record.Keys.Except(ConnectionFields).ToList();
        if (unknown.Count > 0)
        {
            throw Fail($"conexão {index}: campo(s) desconhecido(s) {string.Join(", ", unknown)} (use {string.Join(", ", ConnectionFields)})");
        }

        var diameter = Number("diametro");
        if (diameter <= 0)
        {
            throw Fail($"conexão {index}: diametro deve ser maior que zero");
        }

        // Opcionais: diâmetro externo (padrão = diametro) e referência ao objeto de origem (handle do tubo na Fase 2).
        var outer = record.TryGetValue("diametro_externo", out var o) && o is not null ? Number("diametro_externo") : diameter;
        if (outer < diameter)
        {
            throw Fail($"conexão {index}: diametro_externo menor que diametro");
        }

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["nome"] = record.TryGetValue("nome", out var n) && n is string s && s.Length > 0 ? s : $"C{index}",
            ["angulo"] = Number("angulo"),
            ["cota"] = Number("cota"),
            ["diametro"] = diameter,
            ["diametro_externo"] = outer,
            ["ref"] = record.TryGetValue("ref", out var r) && r is string text ? text : "",
        };
    }

    /// <summary>Campos aceitos num registro de conexão.</summary>
    public static readonly IReadOnlyList<string> ConnectionFields = new[] { "nome", "angulo", "cota", "diametro", "diametro_externo", "ref" };

    internal static bool? ParseBool(string text) => text.Trim().ToLowerInvariant() switch
    {
        "verdadeiro" or "true" or "sim" => true,
        "falso" or "false" or "nao" or "não" => false,
        _ => null,
    };

    private ParameterValidationException Fail(string reason) => new(Name, reason);

    private static string Describe(object? value) => value is null ? "nulo" : $"{Values.TypeName(value)} '{Values.Format(value)}'";
}
