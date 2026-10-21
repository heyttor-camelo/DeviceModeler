using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Persistence;

namespace Modeler.Civil3D.Drawing;

/// <summary>Tabela nativa do AutoCAD com os dispositivos (estudo C §3.3): título, cabeçalho e uma linha
/// por dispositivo, com as colunas montadas por modelos de macros. A tabela guarda a seleção (todos
/// ou uma lista) e as colunas; ela se refaz quando um dispositivo dela muda. Editar o conteúdo das
/// células é desfeito na próxima atualização; largura de colunas e estilo de tabela ficam.</summary>
internal static class Tables
{
    public const string Layer = "DM-TABELAS";

    /// <summary>Colunas prontas: identificação e elevações; ou os quantitativos dos dispositivos.</summary>
    public static List<TableColumn> Preset(string name, IEnumerable<InstanceCache.Entry> entries) => name switch
    {
        "Quantitativos" => new List<TableColumn> { new("Nome", "{nome}"), new("Código", "{codigo}") }
            .Concat(entries.SelectMany(e => e.Instance.Quantities).GroupBy(q => q.Name).Select(g =>
                new TableColumn($"{g.Key} ({g.First().Unit})", $"{{{g.Key}:{(g.First().Unit.Length > 0 && g.First().Unit != "un" ? g.First().Unit + ":3" : "0")}:n}}")))
            .ToList(),
        _ => new List<TableColumn>
        {
            new("Nº", "{numero:0:p}"), new("Nome", "{nome}"), new("Dispositivo", "{dispositivo}"), new("Código", "{codigo}"),
            new("X", "{x:m:3:n:p}"), new("Y", "{y:m:3:n:p}"), new("Tampa (m)", "{elev_tampa:m:3:n}"), new("Fundo (m)", "{elev_fundo:m:3:n}"),
            new("Altura (m)", "{altura:m:2:n}"),
        },
    };

    public static Table Create(Transaction tr, Database db, Point3d position, OutputRecord record)
    {
        var table = new Table();
        table.SetDatabaseDefaults(db);
        table.LayerId = Layers.Ensure(tr, db, Layer, 7);
        table.TableStyle = db.Tablestyle;
        table.Position = position;
        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        space.AppendEntity(table);
        tr.AddNewlyCreatedDBObject(table, true);
        Fill(tr, db, table, record);
        record.Write(table, tr);
        return table;
    }

    /// <summary>Preenche (ou refaz) a tabela com os dispositivos atuais da seleção.</summary>
    public static void Fill(Transaction tr, Database db, Table table, OutputRecord record)
    {
        var devices = new List<(BlockReference Reference, InstanceCache.Entry Entry)>();
        var references = record.AllDevices
            ? DeviceService.AllDevices(tr, db)
            : record.Devices.Select(h => CivilLinks.TryGet<BlockReference>(tr, db, h, out var r) ? r : null).OfType<BlockReference>().ToList();
        foreach (var reference in references)
        {
            if (InstanceCache.Get(tr, reference) is { } entry)
            {
                devices.Add((reference, entry));
            }
        }

        var rows = devices.Select(d => record.Columns.Select(c => OutputText.Render(c.Template, d.Reference, d.Entry).Replace("\n", " ")).ToArray()).ToList();
        if (record.SortColumn >= 0 && record.SortColumn < record.Columns.Count)
        {
            rows = rows.OrderBy(r => SortKey(r[record.SortColumn])).ThenBy(r => r[record.SortColumn], StringComparer.Ordinal).ToList();
        }

        var columns = Math.Max(1, record.Columns.Count);
        table.UpgradeOpen();
        table.SuppressRegenerateTable(true);
        try
        {
            table.SetSize(rows.Count + 2, columns);
            var title = record.Title.Length > 0 ? record.Title : $"Dispositivos ({rows.Count})";
            if (columns > 1)
            {
                table.MergeCells(CellRange.Create(table, 0, 0, 0, columns - 1));
            }

            table.Cells[0, 0].TextString = title;
            for (var c = 0; c < record.Columns.Count; c++)
            {
                table.Cells[1, c].TextString = record.Columns[c].Title;
            }

            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < record.Columns.Count; c++)
                {
                    var cell = table.Cells[r + 2, c];
                    cell.TextString = rows[r][c];
                    if (LooksNumeric(rows[r][c]))
                    {
                        cell.Alignment = CellAlignment.MiddleRight;
                    }
                }
            }
        }
        finally
        {
            table.SuppressRegenerateTable(false);
        }

        table.GenerateLayout();
    }

    private static (int, double) SortKey(string text) =>
        double.TryParse(text.Replace(".", "").Replace(',', '.').Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (0, number)
            : (1, 0);

    private static bool LooksNumeric(string text) => text.Length > 0 && (char.IsDigit(text[0]) || (text[0] is '-' or '+' && text.Length > 1 && char.IsDigit(text[1])));
}
