using System;
using System.Collections.Generic;
using System.Linq;
using Hydro.Civil3D.Catalog;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Ui.Common;

namespace Hydro.Civil3D.Ui.Panel;

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
        new PanelCommand("PASTA", "Pasta do catálogo", "Geral", "Mostra ou troca a pasta das definições .yaml.", Array.Empty<string>(), "HY_CATALOGO "),
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
            new("auto", "ATUALIZAR", new[] { "Atualização automática pelos tubos", DeviceWatcher.Enabled ? "ligada" : "desligada" }, "auto"),
            new("pontas", "CONECTOR", new[] { "Modo das pontas dos tubos (padrão)", Civil.NetworkSync.DefaultMode }, "pontas"),
            new("osnap", "CONECTOR", new[] { "Osnap de conector", ConnectorSnap.Active ? "ligado" : "desligado" }, "osnap"),
            new("grips", "EDITAR", new[] { "Clique no grip", GripCommands.TypedInput ? "arrastar ou digitar o valor (HY_GRIP)" : "só arrastar (estiramento nativo)" }, "grips"),
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
                        .Action("Trocar...", () => host.Send("HY_CATALOGO Sim ", "Escolha a pasta."))
                        .Action("Voltar ao padrão", () => host.Send("HY_CATALOGO Padrão ", "Pasta padrão."));
                    break;
                case "auto":
                    ui.Section("Atualização automática").Note("Ligada: mover a ponta de um tubo ligado refaz o dispositivo ao fim do comando.")
                        .Action("Ligar", () => host.Send("HY_AUTO Ligar ", "Atualização automática ligada."))
                        .Action("Desligar", () => host.Send("HY_AUTO Desligar ", "Atualização automática desligada."));
                    break;
                case "pontas":
                    ui.Section("Pontas dos tubos").Prop("Padrão", Civil.NetworkSync.DefaultMode)
                        .Note("Troque pelo menu Pontas dos tubos, na categoria Redes, ou por dispositivo com HY_PONTAS.");
                    break;
                case "osnap":
                    ui.Section("Osnap de conector")
                        .Action("Ligar", () => host.Send("HY_OSNAP Ligar ", "Osnap de conector ligado."))
                        .Action("Desligar", () => host.Send("HY_OSNAP Desligar ", "Osnap de conector desligado."));
                    break;
                case "grips":
                    ui.Section("Grips dos dispositivos")
                        .Note("Arrastar ou digitar: clicar o grip abre o HY_GRIP, com a mesma prévia do arrasto; digite o valor (1,50 ou 1.50) e Enter, ou Esc para não mudar nada.")
                        .Note("Só arrastar: o estiramento nativo do AutoCAD, sem valor digitado. Vale na próxima seleção.")
                        .Action("Arrastar ou digitar", () => host.Send("HY_GRIPMODO Digitar ", "Grips: arrastar ou digitar."))
                        .Action("Só arrastar", () => host.Send("HY_GRIPMODO Nativo ", "Grips: estiramento nativo."));
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
