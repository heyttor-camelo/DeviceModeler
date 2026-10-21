using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Ui.Common;

namespace Hydro.Civil3D.Ui.Panel;

/// <summary>Categoria Verificação: os problemas da auditoria (HY_AUDITAR) e os avisos das validações
/// de cada dispositivo, com o dispositivo destacado no monitor.</summary>
internal sealed class AuditCategory : IPanelCategory
{
    private readonly DevicesCategory _devices;
    private List<PanelRow> _rows = new();

    public AuditCategory(DevicesCategory devices)
    {
        _devices = devices;
    }

    public string Id => "VERIFICACAO";

    public string Icon => "VERIFICACAO";

    public string Title => "Verificação";

    public string Keyword => "Verificacao";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (_rows.Count, _rows.Count(r => r.Level > 0));

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("AUDITAR", "Auditar e corrigir", "Verificação",
            "Confere registros, definições, blocos compartilhados, nomes repetidos, conexões para tubos apagados e superfícies seguidas; oferece as correções automáticas.",
            new[] { "Leia a lista na linha de comando.", "Corrigir aplica as correções." }, "HY_AUDITAR "),
        new PanelCommand("ATUALIZAR", "Regenerar todos", "Verificação",
            "Relê tubos e catálogo e refaz todos os dispositivos.", new[] { "Enter = todos." }, "HY_ATUALIZAR "),
        new PanelCommand("CONECTOR", "Osnap de conector", "Desenho",
            "Liga ou desliga o osnap que prende o cursor nos conectores dos dispositivos.", Array.Empty<string>(), Menu: new[]
            {
                new PanelMenuItem("Ligar", "HY_OSNAP Ligar ", Checked: () => ConnectorSnap.Active),
                new PanelMenuItem("Desligar", "HY_OSNAP Desligar ", Checked: () => !ConnectorSnap.Active),
            }),
    };

    public string[] Columns => new[] { "Dispositivo", "Tipo", "Problema", "Correção" };

    public double[] Widths => new double[] { 120, 90, 380, 200 };

    public IReadOnlyList<PanelRow> Rows => _rows;

    public void Load(IPanelHost host)
    {
        _rows = new List<PanelRow>();
        var db = host.Document?.Database;
        if (db is null)
        {
            return;
        }

        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        var issues = DeviceAudit.Run(tr, db);
        tr.Commit();
        _rows = issues.Select((issue, i) => new PanelRow(
            "aud:" + i.ToString(CultureInfo.InvariantCulture), "VERIFICACAO",
            new[] { issue.Device, issue.Kind, issue.Message, issue.Fix ?? "" },
            issue, issue.Level switch { AuditLevel.Erro => 2, AuditLevel.Aviso => 1, _ => 0 }, issue.Message)).ToList();
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        yield return new PanelTab("Propriedades", "Propriedades", () =>
        {
            var ui = new PanelUi(host.Theme);
            if (row?.Data is AuditIssue issue)
            {
                ui.Section("Problema").Prop("Dispositivo", issue.Device).Prop("Tipo", issue.Kind).Prop("Nível", issue.Level.ToString())
                    .Note(issue.Message, warn: issue.Level != AuditLevel.Info);
                if (issue.Fix is not null)
                {
                    ui.Prop("Correção automática", issue.Fix).Action("Auditar e corrigir", () => host.Send("HY_AUDITAR ", "Auditoria: responda na linha de comando."));
                }

                if (issue.Handle.Length > 0)
                {
                    ui.Action("Ir para", () => host.Send($"HY_IR {issue.Handle} ", $"{issue.Device} selecionado."));
                }
            }
            else
            {
                ui.Section("Verificação").Prop("Problemas", _rows.Count.ToString(CultureInfo.InvariantCulture))
                    .Note(_rows.Count == 0 ? "Nenhum problema nos dispositivos do desenho." : "Selecione um problema para ver o dispositivo.");
            }

            return ui.Element;
        });
    }

    public void Draw(MonitorView view, PanelRow? selected)
    {
        var handles = _rows.Select(r => ((AuditIssue)r.Data!).Handle).ToHashSet();
        var target = (selected?.Data as AuditIssue)?.Handle;
        foreach (var row in _devices.Rows)
        {
            var item = (DeviceItem)row.Data!;
            DevicesCategory.DrawDevice(view, item, item.Handle == target);
            if (handles.Contains(item.Handle))
            {
                view.Marker((item.Placement.Origin.X, item.Placement.Origin.Y), HydroTheme.Brush(244, 196, 64), 10);
            }
        }

        if (_devices.Rows.Count == 0)
        {
            view.Message = "Nenhum dispositivo no desenho.";
        }
    }

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row)
    {
        if (row.Data is AuditIssue { Handle.Length: > 0 } issue)
        {
            yield return new PanelMenuItem("Ir para", $"HY_IR {issue.Handle} ");
            yield return new PanelMenuItem("Editar parâmetros", $"HY_IR {issue.Handle} HY_EDITAR ");
        }

        yield return new PanelMenuItem("Auditar e corrigir", "HY_AUDITAR ");
    }
}
