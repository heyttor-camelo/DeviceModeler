using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modeler.Core.Expressions;

namespace Modeler.Core.Persistence;

/// <summary>Serializa os valores de parâmetros (números, textos, lógicos e listas de registros)
/// em JSON invariante — é o que a Fase 2 grava no Xrecord da entidade.</summary>
public static class ParameterCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize(IReadOnlyDictionary<string, object> values)
    {
        var root = new JsonObject();
        foreach (var (name, value) in values)
        {
            root[name] = ToNode(value, name);
        }

        return root.ToJsonString(Options);
    }

    public static Dictionary<string, object> Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("parâmetros gravados não são um objeto JSON");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in node)
        {
            result[name] = FromNode(value, name);
        }

        return result;
    }

    private static JsonNode? ToNode(object? value, string path) => Values.Normalize(value) switch
    {
        double d when double.IsFinite(d) => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        IReadOnlyDictionary<string, object> record => ToObject(record, path),
        IReadOnlyList<object> list => new JsonArray(list.Select((v, i) => ToNode(v, $"{path}[{i}]")).ToArray()),
        null => null,
        var other => throw new NotSupportedException($"{path}: valor do tipo {other.GetType().Name} não pode ser gravado"),
    };

    private static JsonObject ToObject(IReadOnlyDictionary<string, object> record, string path)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in record)
        {
            obj[key] = ToNode(value, $"{path}.{key}");
        }

        return obj;
    }

    private static object FromNode(JsonNode? node, string path) => node switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray a => a.Select((n, i) => FromNode(n, $"{path}[{i}]")).ToList(),
        JsonObject o => o.ToDictionary(kv => kv.Key, kv => FromNode(kv.Value, $"{path}.{kv.Key}"), StringComparer.Ordinal),
        null => throw new FormatException($"{path}: valor nulo"),
        _ => throw new FormatException($"{path}: valor JSON não suportado ({node.ToJsonString()})"),
    };

    /// <summary>Formata um número para exibição/arquivos (ponto decimal, até 6 casas).</summary>
    public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
