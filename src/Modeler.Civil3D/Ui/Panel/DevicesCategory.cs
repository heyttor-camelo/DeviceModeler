using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui.Common;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Persistence;
using Modeler.Core.Results;
using CorePlacement = Modeler.Core.Geometry.Placement;
using CoreValues = Modeler.Core.Expressions.Values;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>Dispositivo lido do desenho para o painel.</summary>
internal sealed record DeviceItem(
    string Handle,
    string Name,
    DeviceRecord Record,
    DeviceDefinition? Definition,
    DeviceInstance? Instance,
    CorePlacement Placement,
    string? Error,
    bool Stale);

/// <summary>Categoria Dispositivos: a lista dos dispositivos do desenho, a planta deles no monitor e
/// as abas Propriedades, Variáveis, Quantitativos, Conectores e Avisos.</summary>
internal sealed class DevicesCategory : IPanelCategory
{
    /// <summary>Instâncias já avaliadas, por handle + parâmetros + hash da definição (o painel não
    /// reavalia o que não mudou).</summary>
    private static readonly Dictionary<string, (string Key, DeviceInstance Instance)> Evaluated = new(StringComparer.Ordinal);

    private List<PanelRow> _rows = new();

    public string Id => "DISPOSITIVOS";

    public string Icon => "DISPOSITIVOS";

    public string Title => "Dispositivos";

    public string Keyword => "Dispositivos";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (_rows.Count, _rows.Count(r => r.Level > 0));

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("INSERIR", "Inserir dispositivo", "Lançamento",
            "Abre a janela do catálogo com os parâmetros e o resumo ao vivo; depois pede o ponto e a rotação.",
            new[] { "Escolha o dispositivo e ajuste os parâmetros.", "OK, clique o ponto e gire." }, "DM_INSERIR "),
        new PanelCommand("EDITAR", "Editar parâmetros", "Lançamento",
            "Mesma janela, com os valores atuais; o dispositivo se refaz ao confirmar.",
            new[] { "Selecione o dispositivo (ou marque-o na lista antes)." }, "DM_EDITAR "),
        new PanelCommand("CONECTAR", "Conectar tubos", "Lançamento",
            "Liga tubos da Pipe Network ao dispositivo: furos, conectores e elevação de fundo pelo tubo mais baixo.",
            new[] { "Selecione o dispositivo.", "Selecione os tubos.", "Substituir ou Adicionar." }, "DM_CONECTAR "),
        new PanelCommand("SUPERFICIE", "Elevação pela superfície", "Lançamento",
            "Parâmetros com 'alvo: superficie' (elevação da tampa) passam a valer a elevação da superfície no ponto.",
            new[] { "Selecione os dispositivos (Enter = todos).", "Selecione a superfície." }, "DM_SUPERFICIE "),
        new PanelCommand("ATUALIZAR", "Atualizar", "Atualização",
            "Relê os tubos ligados e o catálogo e refaz os dispositivos; avisa quando a definição mudou.",
            new[] { "Selecione os dispositivos (Enter = todos)." }, "DM_ATUALIZAR "),
        new PanelCommand("CONECTOR", "Atualização automática", "Atualização",
            "Com a atualização automática ligada, mover a ponta de um tubo ligado refaz o dispositivo ao fim do comando.",
            Array.Empty<string>(), Menu: new[]
            {
                new PanelMenuItem("Ligar", "DM_AUTO Ligar ", Checked: () => PipeWatcher.Enabled),
                new PanelMenuItem("Desligar", "DM_AUTO Desligar ", Checked: () => !PipeWatcher.Enabled),
            }),
        new PanelCommand("INFO", "Informações", "Saídas",
            "Parâmetros, variáveis, quantitativos, conectores e nó SWMM na linha de comando.",
            new[] { "Selecione o dispositivo." }, "DM_INFO "),
        new PanelCommand("QUANTITATIVOS", "Quantitativos", "Saídas",
            "Totais por quantitativo (m³, m², un) e CSV detalhado.",
            new[] { "Selecione os dispositivos (Enter = todos).", "Salve o CSV se quiser." }, "DM_QUANTITATIVOS "),
        new PanelCommand("SWMM", "Exportar SWMM", "Saídas",
            "Grava [JUNCTIONS] e [COORDINATES] de todos os dispositivos num .inp do SWMM 5.",
            new[] { "Escolha o arquivo." }, "DM_SWMM "),
    };

    public string[] Columns => new[] { "Nome", "Código", "Dispositivo", "Versão", "X", "Y", "Rotação", "Conexões", "Situação" };

    public double[] Widths => new double[] { 110, 110, 150, 60, 90, 90, 60, 70, 200 };

    public IReadOnlyList<PanelRow> Rows => _rows;

    public void Load(IPanelHost host)
    {
        var db = host.Document?.Database;
        _rows = new List<PanelRow>();
        if (db is null)
        {
            return;
        }

        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        foreach (var reference in DeviceService.AllDevices(tr, db))
        {
            var item = Read(tr, reference);
            var level = item.Error is not null ? 2 : item.Stale || item.Instance?.Warnings.Count > 0 ? 1 : 0;
            var hint = item.Error ?? (item.Stale ? "a definição mudou desde a geração: use Atualizar" : item.Instance?.Warnings.FirstOrDefault());
            var connections = item.Instance is null ? 0 : ConnectionCount(item.Instance);
            _rows.Add(new PanelRow(item.Handle, "DISPOSITIVOS", new[]
            {
                item.Name,
                item.Instance?.Code ?? "",
                item.Definition?.Name ?? item.Record.DefinitionId,
                item.Record.DefinitionVersion,
                N(item.Placement.Origin.X),
                N(item.Placement.Origin.Y),
                N(item.Placement.RotationDegrees, 2) + "°",
                connections.ToString(CultureInfo.InvariantCulture),
                item.Error ?? (item.Stale ? "definição alterada" : item.Instance!.Warnings.Count > 0 ? $"{item.Instance.Warnings.Count} aviso(s)" : "OK"),
            }, item, level, hint));
        }

        tr.Commit();
        var alive = _rows.Select(r => r.Id).ToHashSet();
        foreach (var gone in Evaluated.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            Evaluated.Remove(gone);
        }
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        if (row?.Data is not DeviceItem item)
        {
            yield return new PanelTab("Propriedades", "Propriedades", () => Summary(host));
            yield break;
        }

        yield return new PanelTab("Propriedades", "Propriedades", () => Properties(host, item));
        if (item.Instance is { } instance)
        {
            yield return new PanelTab("Variaveis", $"Variáveis ({instance.Variables.Count})", () => new PanelUi(host.Theme)
                .Table(new[] { "Variável", "Valor" }, instance.Variables.Where(v => v.Value is double or string or bool)
                    .Select(v => new[] { v.Key, CoreValues.Format(v.Value) }))
                .Element);
            yield return new PanelTab("Quantitativos", $"Quantitativos ({instance.Quantities.Count})", () => new PanelUi(host.Theme)
                .Table(new[] { "Quantitativo", "Valor", "Unidade" }, instance.Quantities.Select(q => new[] { q.Name, N(q.Value, 4), q.Unit }))
                .Element);
            yield return new PanelTab("Conectores", $"Conectores ({instance.Connectors.Count})", () => new PanelUi(host.Theme)
                .Table(new[] { "Conector", "X", "Y", "Elevação", "Ø (m)", "Filtro" }, instance.Connectors.Select(c =>
                {
                    var w = item.Placement.ToWorld(c.Position);
                    return new[] { c.Name, N(w.X), N(w.Y), N(w.Z), N(c.Diameter), c.Filter ?? "" };
                }))
                .Element);
        }

        var warnings = item.Instance?.Warnings ?? Array.Empty<string>();
        yield return new PanelTab("Avisos", $"Avisos ({warnings.Count + (item.Error is null ? 0 : 1) + (item.Stale ? 1 : 0)})", () =>
        {
            var ui = new PanelUi(host.Theme);
            if (item.Error is not null)
            {
                ui.Note("ERRO: " + item.Error, warn: true);
            }

            if (item.Stale)
            {
                ui.Note("A definição mudou desde que o dispositivo foi gerado. Use Atualizar.", warn: true);
            }

            foreach (var warning in warnings)
            {
                ui.Note("AVISO: " + warning, warn: true);
            }

            if (item.Error is null && !item.Stale && warnings.Count == 0)
            {
                ui.Note("Nenhum aviso.");
            }

            return ui.Element;
        });
    }

    public void Draw(MonitorView view, PanelRow? selected)
    {
        if (_rows.Count == 0)
        {
            view.Message = "Nenhum dispositivo no desenho.\nUse Inserir dispositivo (DM_INSERIR).";
            return;
        }

        foreach (var row in _rows)
        {
            var item = (DeviceItem)row.Data!;
            var isSelected = selected is not null && selected.Id == row.Id;
            DrawDevice(view, item, isSelected);
        }
    }

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row)
    {
        yield return new PanelMenuItem("Ir para (zoom e seleção)", $"DM_IR {row.Id} ");
        yield return new PanelMenuItem("Editar parâmetros", $"DM_IR {row.Id} DM_EDITAR ");
        yield return new PanelMenuItem("Conectar tubos", $"DM_IR {row.Id} DM_CONECTAR ");
        yield return new PanelMenuItem("Atualizar", $"DM_IR {row.Id} DM_ATUALIZAR ");
        yield return new PanelMenuItem("Informações na linha de comando", $"DM_IR {row.Id} DM_INFO ");
    }

    /// <summary>Desenha um dispositivo em planta: contorno das peças, furos tracejados, conectores e nome.</summary>
    internal static void DrawDevice(MonitorView view, DeviceItem item, bool selected)
    {
        var stroke = selected ? HydroTheme.Highlight : HydroTheme.Brush(200, 205, 210);
        if (item.Instance is null)
        {
            var o = item.Placement.Origin;
            view.Marker((o.X, o.Y), HydroTheme.Brush(240, 90, 90), 9);
            view.Text((o.X, o.Y), item.Name, HydroTheme.Brush(240, 90, 90));
            return;
        }

        var affine = item.Placement.ToAffine();
        foreach (var solid in item.Instance.Solids)
        {
            foreach (var part in PlanOutline.Of(solid.Node, affine))
            {
                view.Polyline(part.Points, part.Removed ? HydroTheme.Brush(90, 200, 240) : stroke, selected ? 1.6 : 1, closed: true, dashed: part.Removed);
            }
        }

        foreach (var connector in item.Instance.Connectors)
        {
            var w = item.Placement.ToWorld(connector.Position);
            var d = item.Placement.DirectionToWorld(connector.Direction);
            var length = Math.Max(connector.Diameter * 2, 0.5);
            view.Polyline(new[] { (w.X, w.Y), (w.X + (d.X * length), w.Y + (d.Y * length)) }, HydroTheme.Brush(90, 200, 240), 2);
            view.Marker((w.X, w.Y), HydroTheme.Brush(230, 90, 230), 6);
        }

        view.Text((item.Placement.Origin.X, item.Placement.Origin.Y), item.Name, selected ? HydroTheme.Highlight : HydroTheme.GraphicText);
    }

    private static DeviceItem Read(Transaction tr, BlockReference reference)
    {
        var handle = reference.Handle.ToString();
        var record = DeviceRecord.TryRead(reference, tr)!;
        var placement = DeviceService.PlacementOf(reference);
        try
        {
            var definition = Catalog.DeviceCatalog.Find(record.DefinitionId, record.SourceFile);
            if (definition is null)
            {
                return new DeviceItem(handle, record.Name, record, null, null, placement, $"definição '{record.DefinitionId}' não encontrada no catálogo", false);
            }

            var key = definition.Hash + "|" + ParameterCodec.Serialize(record.Values);
            DeviceInstance instance;
            if (Evaluated.TryGetValue(handle, out var cached) && cached.Key == key)
            {
                instance = cached.Instance;
            }
            else
            {
                instance = definition.Evaluate(Core.Editing.DeviceEditing.Reconcile(definition, record.Values, out _));
                Evaluated[handle] = (key, instance);
            }

            return new DeviceItem(handle, record.Name, record, definition, instance, placement, null, record.DefinitionHash != definition.Hash);
        }
        catch (Exception ex) when (ex is Core.Errors.ModelerException or InvalidOperationException)
        {
            HydroLog.Warn("Painel", $"{record.Name}: {ex.Message}");
            return new DeviceItem(handle, record.Name, record, null, null, placement, ex.Message, false);
        }
    }

    private static int ConnectionCount(DeviceInstance instance) =>
        instance.Definition.Parameters.Where(p => p.Type == ParameterType.Conexoes)
            .Sum(p => instance.Parameters[p.Name] is IReadOnlyList<object> list ? list.Count : 0);

    private System.Windows.UIElement Summary(IPanelHost host)
    {
        var ui = new PanelUi(host.Theme).Section("Dispositivos no desenho")
            .Prop("Quantidade", _rows.Count.ToString(CultureInfo.InvariantCulture))
            .Prop("Com aviso ou erro", _rows.Count(r => r.Level > 0).ToString(CultureInfo.InvariantCulture))
            .Prop("Atualização automática", PipeWatcher.Enabled ? "ligada" : "desligada");
        foreach (var group in _rows.Select(r => (DeviceItem)r.Data!).GroupBy(i => i.Definition?.Name ?? i.Record.DefinitionId))
        {
            ui.Prop(group.Key, group.Count().ToString(CultureInfo.InvariantCulture));
        }

        return ui.Note("Selecione um dispositivo na lista para ver as propriedades.")
            .Action("Inserir dispositivo", () => host.Send("DM_INSERIR ", "Inserir: siga a linha de comando."))
            .Element;
    }

    private static System.Windows.UIElement Properties(IPanelHost host, DeviceItem item)
    {
        var ui = new PanelUi(host.Theme).Section("Identificação")
            .Prop("Nome", item.Name)
            .Prop("Código", item.Instance?.Code ?? "(sem código)")
            .Prop("Dispositivo", item.Definition?.Name ?? item.Record.DefinitionId)
            .Prop("Família", item.Definition?.Family ?? "")
            .Prop("Definição", $"{item.Record.DefinitionId} {item.Record.DefinitionVersion}", warn: item.Stale)
            .Prop("Arquivo", item.Definition?.SourceFile ?? item.Record.SourceFile)
            .Prop("Handle", item.Handle)
            .Section("Posição")
            .Prop("X", N(item.Placement.Origin.X))
            .Prop("Y", N(item.Placement.Origin.Y))
            .Prop("Rotação", N(item.Placement.RotationDegrees, 2) + "°");
        if (item.Error is not null)
        {
            ui.Note("ERRO: " + item.Error, warn: true);
        }

        if (item.Instance is { } instance)
        {
            foreach (var group in instance.Definition.Parameters.GroupBy(p => p.Group ?? "Parâmetros"))
            {
                ui.Section(group.Key);
                foreach (var parameter in group)
                {
                    var value = instance.Parameters[parameter.Name];
                    var text = value is IReadOnlyList<object> list ? $"{list.Count} item(ns)" : CoreValues.Format(value) + (parameter.Unit.Length > 0 && value is double ? " " + parameter.Unit : "");
                    ui.Prop(parameter.Label ?? parameter.Name, text, parameter.Description);
                }
            }
        }

        return ui.Action("Editar", () => host.Send($"DM_IR {item.Handle} DM_EDITAR ", "Editar: siga a linha de comando."))
            .Action("Ir para", () => host.Send($"DM_IR {item.Handle} ", $"{item.Name} selecionado."))
            .Action("Atualizar", () => host.Send($"DM_IR {item.Handle} DM_ATUALIZAR ", "Atualizando..."))
            .Element;
    }

    internal static string N(double value, int digits = 3) => value.ToString("F" + digits, CultureInfo.InvariantCulture);
}
