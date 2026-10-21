using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Ui.Common;
using Modeler.Core.Network;
using WM = System.Windows.Media;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>Categoria Redes: as redes do desenho (dispositivos ligados por tubos), com extensão,
/// trechos e situação (ciclos, dispositivos isolados), e a planta esquemática de todas no monitor:
/// um traço por trecho com a seta do fluxo, uma cor por rede.</summary>
internal sealed class NetworksCategory : IPanelCategory
{
    private static readonly WM.Brush[] Colors =
    {
        HydroTheme.Brush(90, 200, 240), HydroTheme.Brush(110, 210, 110), HydroTheme.Brush(240, 160, 50),
        HydroTheme.Brush(230, 90, 230), HydroTheme.Brush(244, 196, 64), HydroTheme.Brush(200, 205, 210),
    };

    private List<PanelRow> _rows = new();

    public string Id => "REDES";

    public string Icon => "REDES";

    public string Title => "Redes";

    public string Keyword => "Redes";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (_rows.Count, _rows.Count(r => r.Level > 0));

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("REDE", "Lançar rede", "Lançamento",
            "Lançamento em cadeia: dispositivo → tubo da Pipe Network → dispositivo, com declividade e elevação da tampa pela superfície. Cada nó ganha uma estrutura nula.",
            new[] { "Escolha o dispositivo e os parâmetros.", "Nome da rede, diâmetro e declividade.", "Clique os pontos; Desfazer volta um trecho; Enter termina." }, "DM_REDE "),
        new PanelCommand("CONECTAR", "Estruturas na Pipe Network", "Lançamento",
            "Cria a estrutura nula de cada dispositivo e liga a ela as pontas soltas dos tubos conectados: mover o dispositivo passa a mover os tubos.",
            new[] { "Selecione os dispositivos (Enter = todos).", "Nome da rede." }, "DM_ESTRUTURA "),
        new PanelCommand("NUMERAR", "Numerar", "Rede",
            "Numera pelo fluxo e renomeia pelo modelo de nome da definição.", Array.Empty<string>(), Menu: new[]
            {
                new PanelMenuItem("Global (cabeceira → descarga)", "DM_NUMERAR  Global 1 "),
                new PanelMenuItem("Invertido (descarga = 1)", "DM_NUMERAR  Invertido 1 "),
                new PanelMenuItem("Escolher na linha de comando...", "DM_NUMERAR "),
            }),
        new PanelCommand("CONECTOR", "Pontas dos tubos", "Rede",
            "Como a ponta solta de um tubo segue o dispositivo: qual conector e se a elevação é corrigida ou mantida.", Array.Empty<string>(), Menu: new[]
            {
                Mode("Ajustar posição, corrigir elevação", "ajustar_corrigir"),
                Mode("Ajustar posição, manter elevação", "ajustar_manter"),
                Mode("Melhor conector (3D), corrigir elevação", "melhor3d_corrigir"),
                Mode("Melhor conector (planta), corrigir elevação", "melhor2d_corrigir"),
                Mode("Fixo, corrigir elevação", "fixo_corrigir"),
                PanelMenuItem.Line,
                new PanelMenuItem("Aplicar aos dispositivos...", "DM_PONTAS "),
            }),
        new PanelCommand("ATUALIZAR", "Atualizar", "Rede", "Relê tubos e catálogo e refaz os dispositivos.", Array.Empty<string>(), "DM_ATUALIZAR "),
    };

    public string[] Columns => new[] { "Rede", "Dispositivos", "Trechos", "Extensão (m)", "Situação" };

    public double[] Widths => new double[] { 200, 90, 70, 100, 320 };

    public IReadOnlyList<PanelRow> Rows => _rows;

    private List<DeviceNetwork> _networks = new();

    public void Load(IPanelHost host)
    {
        _rows = new List<PanelRow>();
        _networks = new List<DeviceNetwork>();
        var db = host.Document?.Database;
        if (db is null)
        {
            return;
        }

        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        _networks = NetworkModel.Build(tr, db);
        tr.Commit();
        foreach (var network in _networks)
        {
            var cycles = network.Graph.CycleNodes().Count;
            var outlets = network.Graph.Nodes.Count(n => network.Graph.Downstream(n.Id).Count == 0);
            var issues = new List<string>();
            if (cycles > 0)
            {
                issues.Add($"{cycles} dispositivo(s) em ciclo");
            }

            if (network.Members.Count > 1 && outlets > 1)
            {
                issues.Add($"{outlets} descargas");
            }

            var level = cycles > 0 ? 2 : issues.Count > 0 ? 1 : 0;
            _rows.Add(new PanelRow("rede:" + network.Name, "REDES", new[]
            {
                network.Name,
                network.Members.Count.ToString(CultureInfo.InvariantCulture),
                network.Graph.Edges.Count.ToString(CultureInfo.InvariantCulture),
                DevicesCategory.N(network.Length, 2),
                issues.Count == 0 ? "OK" : string.Join("; ", issues),
            }, network, level, issues.Count == 0 ? null : string.Join("; ", issues)));
        }
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        if (row?.Data is not DeviceNetwork network)
        {
            yield return new PanelTab("Propriedades", "Propriedades", () => new PanelUi(host.Theme).Section("Redes")
                .Prop("Redes", _networks.Count.ToString(CultureInfo.InvariantCulture))
                .Prop("Modo das pontas (padrão)", NetworkSync.DefaultMode)
                .Note("Uma rede é o conjunto de dispositivos ligados por tubos (com o mesmo nome de rede, quando lançados pelo DM_REDE).")
                .Action("Lançar rede", () => host.Send("DM_REDE ", "Lançamento: siga a linha de comando."))
                .Element);
            yield break;
        }

        var numbers = network.Graph.Number();
        yield return new PanelTab("Propriedades", "Propriedades", () => new PanelUi(host.Theme).Section("Rede")
            .Prop("Nome", network.Name)
            .Prop("Dispositivos", network.Members.Count.ToString(CultureInfo.InvariantCulture))
            .Prop("Trechos", network.Graph.Edges.Count.ToString(CultureInfo.InvariantCulture))
            .Prop("Extensão", DevicesCategory.N(network.Length, 2) + " m")
            .Prop("Descarga(s)", string.Join(", ", network.Graph.Nodes.Where(n => network.Graph.Downstream(n.Id).Count == 0).Select(n => n.Name)))
            .Action("Numerar (Global)", () => host.Send("DM_NUMERAR  Global 1 ", "Numerando..."))
            .Action("Estruturas", () => host.Send("DM_ESTRUTURA  " + network.Name + "\n", "Estruturas: siga a linha de comando."))
            .Element);
        yield return new PanelTab("Dispositivos", $"Dispositivos ({network.Members.Count})", () => new PanelUi(host.Theme)
            .Table(new[] { "Ordem", "Dispositivo", "Número", "Fundo", "Tampa" }, network.Members
                .OrderBy(m => numbers.GetValueOrDefault(m.Key))
                .Select(m =>
                {
                    var (rim, sump) = NetworkSync.Elevations(m.Value.Entry);
                    return new[] { numbers.GetValueOrDefault(m.Key).ToString(CultureInfo.InvariantCulture), m.Value.Entry.Record.Name,
                        m.Value.Entry.Record.Number.ToString(CultureInfo.InvariantCulture), DevicesCategory.N(sump), DevicesCategory.N(rim) };
                }))
            .Element);
        yield return new PanelTab("Trechos", $"Trechos ({network.Graph.Edges.Count})", () => new PanelUi(host.Theme)
            .Table(new[] { "Tubo", "Montante", "Jusante", "Compr. (m)", "Declividade" }, network.Graph.Edges.Select(e =>
            {
                var (up, down) = e.Direction;
                var drop = Math.Abs(e.FromInvert - e.ToInvert);
                return new[] { e.Id, Name(network, up), Name(network, down), DevicesCategory.N(e.Length, 2),
                    e.Length > 1e-9 ? (drop / e.Length * 100).ToString("0.00", CultureInfo.InvariantCulture) + " %" : "-" };
            }))
            .Element);
    }

    public void Draw(MonitorView view, PanelRow? selected)
    {
        if (_networks.Count == 0)
        {
            view.Message = "Nenhuma rede no desenho.\nUse Lançar rede (DM_REDE) ou ligue tubos a dispositivos (DM_CONECTAR).";
            return;
        }

        for (var i = 0; i < _networks.Count; i++)
        {
            var network = _networks[i];
            var isSelected = ReferenceEquals(selected?.Data, network);
            var brush = isSelected ? HydroTheme.Highlight : Colors[i % Colors.Length];
            var nodes = network.Graph.Nodes.ToDictionary(n => n.Id);
            foreach (var edge in network.Graph.Edges)
            {
                var (up, down) = edge.Direction;
                var a = nodes[up];
                var b = nodes[down];
                view.Polyline(new[] { (a.X, a.Y), (b.X, b.Y) }, brush, isSelected ? 2.4 : 1.6);
                Arrow(view, (a.X, a.Y), (b.X, b.Y), brush);
            }

            foreach (var node in network.Graph.Nodes)
            {
                view.Marker((node.X, node.Y), brush, isSelected ? 8 : 6);
                view.Text((node.X, node.Y), node.Name, isSelected ? HydroTheme.Highlight : HydroTheme.GraphicText, 10);
            }
        }
    }

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row)
    {
        if (row.Data is DeviceNetwork network)
        {
            var first = network.Members.Keys.FirstOrDefault();
            if (first is not null)
            {
                yield return new PanelMenuItem("Ir para o primeiro dispositivo", $"DM_IR {first} ");
            }

            yield return new PanelMenuItem("Numerar (Global)", "DM_NUMERAR  Global 1 ");
        }
    }

    private static PanelMenuItem Mode(string title, string mode) =>
        new(title, Run: () => NetworkSync.DefaultMode = mode, Checked: () => NetworkSync.DefaultMode == mode);

    private static string Name(DeviceNetwork network, string id) =>
        network.Members.TryGetValue(id, out var member) ? member.Entry.Record.Name : id;

    /// <summary>Seta do fluxo no meio do trecho.</summary>
    private static void Arrow(MonitorView view, (double X, double Y) a, (double X, double Y) b, WM.Brush brush)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
        {
            return;
        }

        var (ux, uy) = (dx / length, dy / length);
        var size = Math.Min(length * 0.15, 1.5);
        var tip = (a.X + (dx * 0.55), a.Y + (dy * 0.55));
        var left = (tip.Item1 - (ux * size) - (uy * size * 0.5), tip.Item2 - (uy * size) + (ux * size * 0.5));
        var right = (tip.Item1 - (ux * size) + (uy * size * 0.5), tip.Item2 - (uy * size) - (ux * size * 0.5));
        view.Polyline(new[] { left, tip, right }, brush, 1.6);
    }
}
