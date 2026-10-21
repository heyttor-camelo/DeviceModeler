using System.Globalization;
using System.Text;
using Hydro.Core.Csg;
using Hydro.Core.Results;

namespace Hydro.Core.Export;

/// <summary>Instância posicionada no desenho, para relatórios e exportações.</summary>
public sealed record PlacedInstance(string Name, Vec3 Position, DeviceInstance Instance);

/// <summary>Saídas em texto: tabela de quantitativos (CSV) e nós SWMM (.inp).</summary>
public static class Reports
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>CSV com separador ';': uma linha por dispositivo × quantitativo, mais os totais.</summary>
    public static string QuantitiesCsv(IEnumerable<PlacedInstance> devices)
    {
        var list = devices.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("dispositivo;tipo;versao;quantitativo;unidade;valor");
        foreach (var d in list)
        {
            foreach (var q in d.Instance.Quantities)
            {
                sb.AppendLine(string.Join(';', Csv(d.Name), d.Instance.Definition.Id, d.Instance.Definition.Version, q.Name, q.Unit,
                    q.Value.ToString("0.0000", Inv)));
            }
        }

        foreach (var total in Totals(list))
        {
            sb.AppendLine(string.Join(';', "TOTAL", "", "", total.Name, total.Unit, total.Value.ToString("0.0000", Inv)));
        }

        return sb.ToString();
    }

    /// <summary>Soma por nome de quantitativo (e unidade).</summary>
    public static List<Quantity> Totals(IEnumerable<PlacedInstance> devices) =>
        devices.SelectMany(d => d.Instance.Quantities)
            .GroupBy(q => (q.Name, q.Unit))
            .Select(g => new Quantity(g.Key.Name, g.Sum(q => q.Value), g.Key.Unit, null))
            .OrderBy(q => q.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Seções [JUNCTIONS] e [COORDINATES] do SWMM 5 para os dispositivos com junção.</summary>
    public static string SwmmJunctions(IEnumerable<PlacedInstance> devices)
    {
        var junctions = devices
            .SelectMany(d => d.Instance.Hydraulics.OfType<SwmmJunction>().Select(j => (d, j)))
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine("[JUNCTIONS]");
        sb.AppendLine(";;Name           Elevation  MaxDepth   InitDepth  SurDepth   Aponded");
        foreach (var (d, j) in junctions)
        {
            sb.AppendLine(string.Create(Inv,
                $"{SwmmName(d.Name),-16} {j.InvertElevation,-10:0.###} {j.MaxDepth,-10:0.###} {j.InitialDepth,-10:0.###} {j.SurchargeDepth,-10:0.###} {j.PondedArea:0.###}"));
        }

        sb.AppendLine();
        sb.AppendLine("[COORDINATES]");
        sb.AppendLine(";;Node           X-Coord            Y-Coord");
        foreach (var (d, _) in junctions)
        {
            sb.AppendLine(string.Create(Inv, $"{SwmmName(d.Name),-16} {d.Position.X,-18:0.###} {d.Position.Y:0.###}"));
        }

        return sb.ToString();
    }

    /// <summary>Nomes SWMM não podem ter espaços nem ';'.</summary>
    public static string SwmmName(string name)
    {
        var cleaned = new string(name.Select(c => char.IsWhiteSpace(c) || c == ';' ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "NO" : cleaned;
    }

    private static string Csv(string text) => text.Contains(';') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
