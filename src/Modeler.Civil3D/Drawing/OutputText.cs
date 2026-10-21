using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Core.Errors;
using Modeler.Core.Text;

namespace Modeler.Civil3D.Drawing;

/// <summary>Texto das saídas (rótulos, tabelas): modelos com campos <c>{expressão:formato}</c> sobre
/// parâmetros, variáveis e quantitativos do dispositivo, mais os nomes que só o desenho conhece:
/// <c>nome</c>, <c>numero</c>, <c>rede</c>, <c>x</c>, <c>y</c>, <c>rotacao</c>, <c>elev_tampa</c>,
/// <c>elev_fundo</c> e <c>altura</c>.</summary>
internal static class OutputText
{
    public static readonly IReadOnlyList<string> DrawingNames = new[] { "nome", "numero", "rede", "x", "y", "rotacao", "elev_tampa", "elev_fundo", "altura" };

    public static Dictionary<string, object> Extras(BlockReference reference, InstanceCache.Entry entry)
    {
        var (rim, sump) = NetworkSync.Elevations(entry);
        return new Dictionary<string, object>
        {
            ["nome"] = entry.Record.Name,
            ["numero"] = (double)entry.Record.Number,
            ["rede"] = entry.Record.Network,
            ["x"] = reference.Position.X,
            ["y"] = reference.Position.Y,
            ["rotacao"] = reference.Rotation * 180 / System.Math.PI,
            ["elev_tampa"] = rim,
            ["elev_fundo"] = sump,
            ["altura"] = rim - sump,
        };
    }

    /// <summary>Texto do modelo para o dispositivo; um campo que não avalia vira "-" (a definição pode
    /// não ter aquele nome).</summary>
    public static string Render(string template, BlockReference reference, InstanceCache.Entry entry)
    {
        Template parsed;
        try
        {
            parsed = Template.Parse(template);
        }
        catch (ModelerException ex)
        {
            return "(modelo inválido: " + ex.Reason + ")";
        }

        try
        {
            return entry.Instance.Render(parsed, Extras(reference, entry));
        }
        catch (ModelerException)
        {
            // campo a campo, para mostrar o que dá
            var parts = new List<string>();
            foreach (var line in template.Split('\n'))
            {
                try
                {
                    parts.Add(entry.Instance.Render(Template.Parse(line), Extras(reference, entry)));
                }
                catch (ModelerException)
                {
                    parts.Add("-");
                }
            }

            return string.Join("\n", parts);
        }
    }

    public static Point3d Position(BlockReference reference) => reference.Position;
}
