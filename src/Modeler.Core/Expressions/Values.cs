using System.Globalization;
using Modeler.Core.Csg;

namespace Modeler.Core.Expressions;

/// <summary>Valores em tempo de execução: <see cref="double"/>, <see cref="bool"/>, <see cref="string"/>,
/// lista (<c>IReadOnlyList&lt;object&gt;</c>), registro (<c>IReadOnlyDictionary&lt;string, object&gt;</c>)
/// e sólido (<see cref="CsgNode"/>).</summary>
public static class Values
{
    public static string TypeName(object? value) => value switch
    {
        null => "nulo",
        double => "número",
        bool => "lógico",
        string => "texto",
        CsgNode => "sólido",
        IReadOnlyDictionary<string, object> => "registro",
        IReadOnlyList<object> => "lista",
        _ => value.GetType().Name,
    };

    public static string Format(object? value) => value switch
    {
        null => "nulo",
        double d => FormatNumber(d),
        bool b => b ? "verdadeiro" : "falso",
        string s => s,
        CsgNode n => n.GetType().Name,
        IReadOnlyDictionary<string, object> r => "{" + string.Join(", ", r.Select(kv => kv.Key + ": " + Format(kv.Value))) + "}",
        IReadOnlyList<object> l => "[" + string.Join(", ", l.Select(Format)) + "]",
        _ => value.ToString() ?? "",
    };

    public static string FormatNumber(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>Igualdade numérica com tolerância relativa de 1e-9 (absorve erros de ponto flutuante).</summary>
    public static bool NearlyEqual(double a, double b) =>
        Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

    public static bool TryToVec3(object? value, out Vec3 vector)
    {
        if (value is IReadOnlyList<object> { Count: 3 } list && list[0] is double x && list[1] is double y && list[2] is double z)
        {
            vector = new Vec3(x, y, z);
            return true;
        }

        vector = default;
        return false;
    }

    public static IReadOnlyList<object> FromVec3(Vec3 v) => new object[] { v.X, v.Y, v.Z };

    /// <summary>Converte números .NET (int, float, decimal...) para double; demais valores passam direto.</summary>
    public static object? Normalize(object? value) => value switch
    {
        double d => d,
        int or long or short or byte or float or decimal or uint or ulong or ushort or sbyte => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => value,
    };
}
