using System.Globalization;
using Hydro.Core.Expressions;

namespace Hydro.Core.Text;

/// <summary>Formato de um campo de macro: <c>{H:m:3}</c>, <c>{D:cm:0}</c>, <c>{i:%:2}</c>,
/// <c>{est:estaca}</c>, <c>{codigo:maiusc}</c>. As partes separadas por ':' podem vir em qualquer
/// ordem: unidade (m, cm, mm, km, m2, cm2, ha, km2, m3, m3/s, %, grau, un, kg, t), número de
/// casas, <c>n</c> (sem o símbolo da unidade), <c>p</c> (ponto decimal, sem milhar),
/// <c>sinal</c> (sempre com + ou −), <c>estaca</c> (20 m) e, para textos, <c>maiusc</c>,
/// <c>minusc</c> ou <c>titulo</c>. Os valores estão sempre nas unidades de base do modelador
/// (m, m², m³, m³/s, graus; declividade como fração) e a conversão é só na saída.</summary>
public sealed record FormatSpec(string? Unit, int? Decimals, bool NoSymbol, bool Point, bool Sign, bool Station, string? Case)
{
    public static readonly IReadOnlyDictionary<string, (double Factor, string Symbol)> Units =
        new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["m"] = (1, " m"),
            ["cm"] = (100, " cm"),
            ["mm"] = (1000, " mm"),
            ["km"] = (0.001, " km"),
            ["m2"] = (1, " m²"),
            ["m²"] = (1, " m²"),
            ["cm2"] = (1e4, " cm²"),
            ["ha"] = (1e-4, " ha"),
            ["km2"] = (1e-6, " km²"),
            ["m3"] = (1, " m³"),
            ["m³"] = (1, " m³"),
            ["m3/s"] = (1, " m³/s"),
            ["m³/s"] = (1, " m³/s"),
            ["%"] = (100, " %"),
            ["grau"] = (1, "°"),
            ["°"] = (1, "°"),
            ["un"] = (1, " un"),
            ["kg"] = (1, " kg"),
            ["t"] = (0.001, " t"),
        };

    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Interpreta o texto depois do primeiro ':' do campo. Erro com o motivo se algo não for reconhecido.</summary>
    public static FormatSpec Parse(string text)
    {
        string? unit = null;
        int? decimals = null;
        bool noSymbol = false, point = false, sign = false, station = false;
        string? textCase = null;
        foreach (var raw in text.Split(':'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                decimals = n <= 10 ? n : throw new FormatException($"no máximo 10 casas decimais (recebido {n})");
            }
            else if (Units.ContainsKey(part))
            {
                unit = unit is null ? part : throw new FormatException($"duas unidades no mesmo campo ('{unit}' e '{part}')");
            }
            else
            {
                switch (part.ToLowerInvariant())
                {
                    case "n":
                        noSymbol = true;
                        break;
                    case "p":
                        point = true;
                        break;
                    case "sinal":
                        sign = true;
                        break;
                    case "estaca":
                        station = true;
                        break;
                    case "maiusc" or "minusc" or "titulo":
                        textCase = part.ToLowerInvariant();
                        break;
                    case "l" or "l/s":
                        throw new FormatException("litros não são usados no modelador; use m3 ou m3/s");
                    default:
                        throw new FormatException($"formato '{part}' desconhecido (use uma unidade, o número de casas, n, p, sinal, estaca, maiusc, minusc ou titulo)");
                }
            }
        }

        return new FormatSpec(unit, decimals, noSymbol, point, sign, station, textCase);
    }

    public string Format(object value)
    {
        if (value is not double number)
        {
            var text = Values.Format(value);
            return Case switch
            {
                "maiusc" => text.ToUpper(Brazil),
                "minusc" => text.ToLower(Brazil),
                "titulo" => Brazil.TextInfo.ToTitleCase(text.ToLower(Brazil)),
                _ => text,
            };
        }

        if (Station)
        {
            return FormatStation(number);
        }

        var (factor, symbol) = Unit is null ? (1.0, "") : Units[Unit];
        var scaled = number * factor;
        var digits = Decimals ?? DefaultDecimals(Unit);
        var culture = Point ? CultureInfo.InvariantCulture : Brazil;
        var body = scaled.ToString((Point ? "F" : "N") + digits.ToString(CultureInfo.InvariantCulture), culture);
        if (Sign && scaled > 0 && !body.StartsWith('-'))
        {
            body = "+" + body;
        }

        if (body.StartsWith('-') && IsZero(body))
        {
            body = body[1..];
        }

        return NoSymbol ? body : body + symbol;
    }

    private string FormatStation(double meters)
    {
        var digits = Decimals ?? 2;
        var stations = Math.Floor(Math.Round(meters, digits) / 20);
        var rest = Math.Round(meters - (stations * 20), digits);
        var culture = Point ? CultureInfo.InvariantCulture : Brazil;
        return stations.ToString("0", CultureInfo.InvariantCulture) + " + " + rest.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), culture);
    }

    private static int DefaultDecimals(string? unit) => unit?.ToLowerInvariant() switch
    {
        null => 3,
        "un" => 0,
        "mm" or "cm" => 1,
        _ => 2,
    };

    private static bool IsZero(string text) => text.All(ch => !char.IsDigit(ch) || ch == '0');
}
