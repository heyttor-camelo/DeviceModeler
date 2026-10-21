using System;
using System.Collections.Generic;
using System.Linq;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Ui.Common;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>Categoria Configuração: opções do plugin (pasta do catálogo, atualização automática,
/// tema, log e versão), uma por linha, com as ações na aba Propriedades.</summary>
internal sealed class SettingsCategory : IPanelCategory
{
    private List<PanelRow> _rows = new();

    public string Id => "CONFIG";

    public string Icon => "CONFIG";

    public string Title => "Configuração";

    public string Keyword => "Configuracao";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (-1, 0);

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("PASTA", "Pasta do catálogo", "Geral", "Mostra ou troca a pasta das definições .yaml.", Array.Empty<string>(), "DM_CATALOGO "),
        new PanelCommand("LOG", "Log", "Geral", "Abre o log comum do HYDRO (Ctrl+Shift+L).", Array.Empty<string>(), Run: LogWindow.Show),
    };

    public string[] Columns => new[] { "Opção", "Valor" };

    public double[] Widths => new double[] { 240, 420 };

    public IReadOnlyList<PanelRow> Rows => _rows;

    public void Load(IPanelHost host)
    {
        _rows = new List<PanelRow>
        {
            new("catalogo", "PASTA", new[] { "Pasta do catálogo", DeviceCatalog.Folder }, "catalogo"),
            new("auto", "ATUALIZAR", new[] { "Atualização automática pelos tubos", PipeWatcher.Enabled ? "ligada" : "desligada" }, "auto"),
            new("tema", "PAINEL", new[] { "Tema do painel", host.Theme.IsDark ? "escuro" : "claro" }, "tema"),
            new("log", "LOG", new[] { "Pasta do log", HydroLog.Folder }, "log"),
            new("versao", "INFO", new[] { "Versão", HydroLog.Environment() }, "versao"),
        };
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        yield return new PanelTab("Propriedades", "Propriedades", () =>
        {
            var ui = new PanelUi(host.Theme);
            switch (row?.Data as string)
            {
                case "catalogo":
                    ui.Section("Pasta do catálogo").Prop("Atual", DeviceCatalog.Folder).Prop("Padrão", DeviceCatalog.DefaultFolder)
                        .Action("Trocar...", () => host.Send("DM_CATALOGO Sim ", "Escolha a pasta."))
                        .Action("Voltar ao padrão", () => host.Send("DM_CATALOGO Padrão ", "Pasta padrão."));
                    break;
                case "auto":
                    ui.Section("Atualização automática").Note("Ligada: mover a ponta de um tubo ligado refaz o dispositivo ao fim do comando.")
                        .Action("Ligar", () => host.Send("DM_AUTO Ligar ", "Atualização automática ligada."))
                        .Action("Desligar", () => host.Send("DM_AUTO Desligar ", "Atualização automática desligada."));
                    break;
                case "tema":
                    ui.Section("Tema").Note("Troque em Preferências..., no rodapé do painel.");
                    break;
                case "log":
                    ui.Section("Log").Prop("Pasta", HydroLog.Folder).Action("Abrir o log", LogWindow.Show);
                    break;
                default:
                    ui.Section("HYDRO").Note(HydroLog.Environment()).Note("Dependências embutidas: " + string.Join(", ", EmbeddedAssemblies.Names));
                    break;
            }

            return ui.Element;
        });
    }

    public void Draw(MonitorView view, PanelRow? selected) => view.Message = "Configuração do HYDRO";

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row) => Enumerable.Empty<PanelMenuItem>();
}
