using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Ui.Common;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Errors;
using Modeler.Core.Results;
using CoreValues = Modeler.Core.Expressions.Values;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>Categoria Catálogo: as definições YAML da pasta do catálogo (e os arquivos com erro), com
/// a planta do dispositivo nos valores padrão no monitor.</summary>
internal sealed class CatalogCategory : IPanelCategory
{
    private List<PanelRow> _rows = new();

    public string Id => "CATALOGO";

    public string Icon => "CATALOGO";

    public string Title => "Catálogo";

    public string Keyword => "Catalogo";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (_rows.Count(r => r.Level < 2), _rows.Count(r => r.Level == 2));

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("PASTA", "Pasta do catálogo", "Catálogo",
            "Mostra ou troca a pasta com as definições .yaml (gravada em %APPDATA%\\DeviceModeler\\settings.json).",
            new[] { "Sim abre a escolha de pasta; Padrão volta à pasta do plugin." }, "DM_CATALOGO "),
        new PanelCommand("LOG", "Abrir a pasta no Explorer", "Catálogo",
            "Abre a pasta do catálogo para editar ou acrescentar definições.", Array.Empty<string>(), Run: () =>
            {
                Directory.CreateDirectory(DeviceCatalog.Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", DeviceCatalog.Folder) { UseShellExecute = true });
            }),
        new PanelCommand("INSERIR", "Inserir do catálogo", "Uso",
            "Abre a janela de inserção com o catálogo.", Array.Empty<string>(), "DM_INSERIR "),
    };

    public string[] Columns => new[] { "Dispositivo", "Id", "Versão", "Categoria", "Arquivo" };

    public double[] Widths => new double[] { 200, 150, 60, 70, 300 };

    public IReadOnlyList<PanelRow> Rows => _rows;

    public void Load(IPanelHost host)
    {
        var definitions = DeviceCatalog.LoadAll(out var errors);
        _rows = definitions.Select(d => new PanelRow("def:" + d.Id, "CATALOGO", new[]
        {
            d.Name ?? d.Id, d.Id, d.Version, d.Category.ToString().ToLowerInvariant(), Path.GetFileName(d.SourceFile ?? ""),
        }, d)).ToList();
        _rows.AddRange(errors.Select((e, i) => new PanelRow("err:" + i.ToString(CultureInfo.InvariantCulture), "VERIFICACAO",
            new[] { "(arquivo com erro)", "", "", "", e }, e, 2, e)));
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        switch (row?.Data)
        {
            case DeviceDefinition definition:
                yield return new PanelTab("Propriedades", "Propriedades", () => Properties(host, definition));
                yield return new PanelTab("Parametros", $"Parâmetros ({definition.Parameters.Count})", () => new PanelUi(host.Theme)
                    .Table(new[] { "Parâmetro", "Tipo", "Padrão", "Mín.", "Máx.", "Unidade" }, definition.Parameters.Select(p => new[]
                    {
                        p.Label is null ? p.Name : $"{p.Name} ({p.Label})",
                        p.Type.ToString().ToLowerInvariant(),
                        p.Default is IReadOnlyList<object> list ? $"{list.Count} item(ns)" : CoreValues.Format(p.Default),
                        p.Min is { } min ? CoreValues.FormatNumber(min) : "",
                        p.Max is { } max ? CoreValues.FormatNumber(max) : "",
                        p.Unit,
                    }))
                    .Element);
                yield return new PanelTab("Saidas", "Saídas", () => Outputs(host, definition));
                break;
            case string error:
                yield return new PanelTab("Propriedades", "Erro", () => new PanelUi(host.Theme).Section("Arquivo com erro").Note(error, warn: true).Element);
                break;
            default:
                yield return new PanelTab("Propriedades", "Propriedades", () => new PanelUi(host.Theme).Section("Catálogo")
                    .Prop("Pasta", DeviceCatalog.Folder)
                    .Prop("Definições", _rows.Count(r => r.Level < 2).ToString(CultureInfo.InvariantCulture))
                    .Prop("Arquivos com erro", _rows.Count(r => r.Level == 2).ToString(CultureInfo.InvariantCulture), warn: _rows.Any(r => r.Level == 2))
                    .Note("Selecione uma definição para ver os parâmetros e a planta com os valores padrão.")
                    .Element);
                break;
        }
    }

    public void Draw(MonitorView view, PanelRow? selected)
    {
        if (selected?.Data is not DeviceDefinition definition)
        {
            view.Message = _rows.Count == 0 ? $"Nenhuma definição em {DeviceCatalog.Folder}." : "Selecione uma definição para ver a planta com os valores padrão.";
            return;
        }

        DeviceInstance instance;
        try
        {
            instance = definition.Evaluate();
        }
        catch (ModelerException ex)
        {
            view.Message = "Os valores padrão não avaliam:\n" + ex.Message;
            return;
        }

        foreach (var solid in instance.Solids)
        {
            foreach (var part in PlanOutline.Of(solid.Node))
            {
                view.Polyline(part.Points, part.Removed ? HydroTheme.Brush(90, 200, 240) : HydroTheme.Brush(200, 205, 210), 1, closed: true, dashed: part.Removed);
            }
        }

        foreach (var connector in instance.Connectors)
        {
            view.Marker((connector.Position.X, connector.Position.Y), HydroTheme.Brush(230, 90, 230), 6);
            view.Text((connector.Position.X, connector.Position.Y), connector.Name, HydroTheme.Brush(230, 90, 230), 10);
        }

        foreach (var grip in instance.Grips)
        {
            view.Marker((grip.Position.X, grip.Position.Y), HydroTheme.Brush(90, 200, 240), 7);
        }

        view.Text((0, 0), definition.Name ?? definition.Id, HydroTheme.GraphicText);
    }

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row)
    {
        if (row.Data is DeviceDefinition definition && definition.SourceFile is { } file)
        {
            yield return new PanelMenuItem("Abrir o arquivo", Run: () => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }));
            yield return new PanelMenuItem("Mostrar na pasta", Run: () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }));
        }
    }

    private static System.Windows.UIElement Properties(IPanelHost host, DeviceDefinition d)
    {
        var ui = new PanelUi(host.Theme).Section("Definição")
            .Prop("Nome", d.Name ?? d.Id)
            .Prop("Id", d.Id)
            .Prop("Versão", d.Version)
            .Prop("Categoria", d.Category.ToString().ToLowerInvariant());
        ui.Prop("Arquivo", d.SourceFile ?? "").Prop("Hash", d.Hash[..Math.Min(12, d.Hash.Length)]);
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            ui.Section("Descrição").Note(d.Description.Trim());
        }

        return ui.Action("Inserir", () => host.Send("DM_INSERIR ", "Inserir: siga a linha de comando.")).Element;
    }

    private static System.Windows.UIElement Outputs(IPanelHost host, DeviceDefinition definition)
    {
        var ui = new PanelUi(host.Theme);
        try
        {
            var instance = definition.Evaluate();
            ui.Section("Sólidos (valores padrão)").Table(new[] { "Sólido", "Material" }, instance.Solids.Select(s => new[] { s.Name, s.Material ?? "" }))
                .Section("Quantitativos").Table(new[] { "Quantitativo", "Valor", "Unidade" }, instance.Quantities.Select(q => new[] { q.Name, DevicesCategory.N(q.Value, 4), q.Unit }))
                .Section("Conectores").Table(new[] { "Conector", "Ø (m)", "Filtro" }, instance.Connectors.Select(c => new[] { c.Name, DevicesCategory.N(c.Diameter), c.Filter ?? "" }));
        }
        catch (ModelerException ex)
        {
            ui.Note("Os valores padrão não avaliam: " + ex.Message, warn: true);
        }

        return ui.Element;
    }
}
