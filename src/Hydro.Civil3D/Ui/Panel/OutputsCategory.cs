using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Hydro.Civil3D.Civil;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Drawing;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Persistence;
using Hydro.Civil3D.Ui.Common;
using Hydro.Core.Csg;
using Hydro.Core.Drawing;
using SW = System.Windows;
using WC = System.Windows.Controls;
using ProfileProjection = Hydro.Core.Drawing.ProfileProjection;

namespace Hydro.Civil3D.Ui.Panel;

/// <summary>Uma saída na lista: estilo (de dispositivo ou de projeção), rótulo, tabela ou projeção.</summary>
internal sealed record OutputItem(string Kind, string Name, string Handle, OutputRecord? Record, DeviceStyle? DeviceStyle, ProjectionStyle? ProjectionStyle,
    List<ProjectedLine>? Preview);

/// <summary>Categoria Saídas: estilos de dispositivo e de projeção (com o editor da tabela de
/// componentes), rótulos, tabelas e projeções em perfil do desenho. O monitor mostra a projeção
/// selecionada em estaca × elevação.</summary>
internal sealed class OutputsCategory : IPanelCategory
{
    private List<PanelRow> _rows = new();

    public string Id => "SAIDAS";

    public string Icon => "SAIDAS";

    public string Title => "Saídas";

    public string Keyword => "Saidas";

    public bool Ready => true;

    public (int Count, int Warnings) Count => (_rows.Count(r => ((OutputItem)r.Data!).Record is not null), _rows.Count(r => r.Level > 0));

    public IReadOnlyList<PanelCommand> Commands { get; } = new[]
    {
        new PanelCommand("PROJETAR", "Projetar em perfil", "Saídas",
            "Corta o sólido do dispositivo pelo plano do alinhamento da vista e desenha corte, hachura, contorno, linhas ocultas e eixo pelo estilo de projeção.",
            new[] { "Selecione os dispositivos (Enter = todos).", "Selecione a vista de perfil.", "Estilo, lado e distância máxima." }, "HY_PROJETAR "),
        new PanelCommand("LABEL", "Rótulo", "Saídas",
            "MLeader com o modelo de macros ({nome}, {codigo}, {elev_tampa:m:3}...); acompanha o dispositivo.",
            new[] { "Selecione o dispositivo.", "Modelo do texto.", "Posição do texto." }, "HY_ROTULO "),
        new PanelCommand("TABELA", "Tabela", "Saídas", "Tabela nativa dos dispositivos, refeita quando eles mudam.", Array.Empty<string>(), Menu: new[]
        {
            new PanelMenuItem("Identificação e elevações...", "HY_TABELA  Padrao "),
            new PanelMenuItem("Quantitativos...", "HY_TABELA  Quantitativos "),
        }),
        new PanelCommand("ESTILO", "Estilo de dispositivo", "Estilos", "Tabela de componentes casada pelo nome dos sólidos.", Array.Empty<string>(), Menu: new[]
        {
            new PanelMenuItem("Aplicar a dispositivos...", "HY_ESTILO Aplicar "),
            new PanelMenuItem("Criar a partir de um dispositivo...", "HY_ESTILO Criar "),
            new PanelMenuItem("Refazer os dispositivos (depois de editar)", "HY_ESTILO Atualizar "),
        }),
        new PanelCommand("ATUALIZAR", "Atualizar saídas", "Estilos", "Refaz todos os rótulos, tabelas e projeções.", Array.Empty<string>(), "HY_SAIDAS "),
    };

    public string[] Columns => new[] { "Saída", "Tipo", "Dispositivo / uso", "Situação" };

    public double[] Widths => new double[] { 220, 130, 260, 260 };

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
        var devices = DeviceService.AllDevices(tr, db).ToDictionary(r => r.Handle.ToString(), r => DeviceRecord.TryRead(r, tr)!, StringComparer.OrdinalIgnoreCase);
        foreach (var name in StyleStore.DeviceStyleNames(tr, db))
        {
            var style = StyleStore.Device(tr, db, name);
            var uses = devices.Values.Count(r => string.Equals(r.Style.Length == 0 ? DeviceStyle.DefaultName : r.Style, name, StringComparison.OrdinalIgnoreCase));
            _rows.Add(new PanelRow("ed:" + name, "ESTILO", new[] { name, "estilo de dispositivo", $"{uses} dispositivo(s)", $"{style.Components.Count} componente(s)" },
                new OutputItem("estilo", name, "", null, style, null, null)));
        }

        foreach (var name in StyleStore.ProjectionStyleNames(tr, db))
        {
            var style = StyleStore.Projection(tr, db, name);
            _rows.Add(new PanelRow("ep:" + name, "PROJETAR", new[] { name, "estilo de projeção", $"lado {style.Side.ToString().ToLowerInvariant()}, até {style.MaxDistance:0.##} m", $"hachura {style.Hatch.Pattern}" },
                new OutputItem("estilo_projecao", name, "", null, null, style, null)));
        }

        foreach (var (entity, record) in OutputRecord.All(tr, db))
        {
            var device = record.AllDevices ? "todos os dispositivos" : string.Join(", ", record.Devices.Select(h => devices.TryGetValue(h, out var r) ? r.Name : h + " (apagado)"));
            var (kind, icon) = record.Kind switch
            {
                OutputKind.Rotulo => ("rótulo", "LABEL"),
                OutputKind.Tabela => ("tabela", "TABELA"),
                _ => ("projeção", "PROJETAR"),
            };
            var status = record.Kind == OutputKind.Projecao ? (record.Status.Length == 0 ? ViewName(tr, db, record) : "não aparece: " + record.Status) : "";
            _rows.Add(new PanelRow("out:" + entity.Handle, icon, new[] { $"{kind} {entity.Handle}", kind, device, status },
                new OutputItem(kind, kind, entity.Handle.ToString(), record, null, null, record.Kind == OutputKind.Projecao ? Preview(tr, db, record) : null),
                record.Status.Length > 0 ? 1 : 0, record.Status.Length > 0 ? record.Status : null));
        }

        tr.Commit();
    }

    public IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row)
    {
        switch ((row?.Data as OutputItem)?.Kind)
        {
            case "estilo":
                var deviceStyle = ((OutputItem)row!.Data!).DeviceStyle!;
                yield return new PanelTab("Propriedades", "Componentes", () => DeviceStyleEditor(host, deviceStyle));
                break;
            case "estilo_projecao":
                var projectionStyle = ((OutputItem)row!.Data!).ProjectionStyle!;
                yield return new PanelTab("Propriedades", "Componentes", () => ProjectionStyleEditor(host, projectionStyle));
                break;
            case null:
                yield return new PanelTab("Propriedades", "Propriedades", () => new PanelUi(host.Theme).Section("Saídas")
                    .Note("Estilos ficam no desenho (dicionário HYDRO_ESTILOS). Rótulos, tabelas e projeções se refazem quando o dispositivo muda.")
                    .Note("Campos dos modelos: parâmetros, variáveis, quantitativos, codigo, nome, numero, rede, x, y, rotacao, elev_tampa, elev_fundo e altura, com formato {campo:unidade:casas}.")
                    .Element);
                break;
            default:
                var item = (OutputItem)row!.Data!;
                yield return new PanelTab("Propriedades", "Propriedades", () =>
                {
                    var ui = new PanelUi(host.Theme).Section(item.Kind).Prop("Handle", item.Handle);
                    var record = item.Record!;
                    switch (record.Kind)
                    {
                        case OutputKind.Rotulo:
                            ui.Prop("Modelo", record.Template.Replace("\n", " ⏎ "));
                            break;
                        case OutputKind.Tabela:
                            ui.Prop("Seleção", record.AllDevices ? "todos (e os novos)" : $"{record.Devices.Count} dispositivo(s)")
                                .Table(new[] { "Coluna", "Modelo" }, record.Columns.Select(c => new[] { c.Title, c.Template }));
                            break;
                        case OutputKind.Projecao:
                            ui.Prop("Estilo", record.Style).Prop("Lado", record.Side ?? "do estilo")
                                .Prop("Distância máxima", record.MaxDistance is { } d ? d.ToString("0.##", CultureInfo.InvariantCulture) + " m" : "do estilo")
                                .Prop("Situação", record.Status.Length == 0 ? "visível" : record.Status, warn: record.Status.Length > 0);
                            break;
                    }

                    return ui.Action("Ir para", () => host.Send($"HY_IR {item.Handle} ", "Saída selecionada."))
                        .Action("Atualizar saídas", () => host.Send("HY_SAIDAS ", "Atualizando..."))
                        .Element;
                });
                break;
        }
    }

    public void Draw(MonitorView view, PanelRow? selected)
    {
        if (selected?.Data is OutputItem { Preview: { Count: > 0 } preview })
        {
            view.Units = "m (estaca × elevação)";
            foreach (var line in preview)
            {
                view.Polyline(line.Points.Select(p => (p.Station, p.Elevation)), line.Removed ? HydroTheme.Brush(90, 200, 240) : HydroTheme.Brush(200, 205, 210), 1,
                    closed: line.Closed, dashed: line.Removed);
            }

            return;
        }

        view.Units = "m";
        view.Message = selected is null
            ? "Selecione uma projeção para vê-la em estaca × elevação, ou um estilo para editar os componentes."
            : "Sem prévia para esta saída.";
    }

    public IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row)
    {
        if (row.Data is OutputItem { Handle.Length: > 0 } item)
        {
            yield return new PanelMenuItem("Ir para", $"HY_IR {item.Handle} ");
        }

        yield return new PanelMenuItem("Atualizar saídas", "HY_SAIDAS ");
    }

    private static string ViewName(Transaction tr, Database db, OutputRecord record) =>
        CivilLinks.TryGet<ProfileView>(tr, db, record.ProfileView, out var view) ? $"em {view.Name}" : "vista apagada";

    /// <summary>Prévia (estaca × elevação, sem exagero) do dispositivo da projeção.</summary>
    private static List<ProjectedLine>? Preview(Transaction tr, Database db, OutputRecord record)
    {
        if (record.Devices.Count == 0 || !CivilLinks.TryGet<BlockReference>(tr, db, record.Devices[0], out var device) ||
            !CivilLinks.TryGet<ProfileView>(tr, db, record.ProfileView, out var view) || InstanceCache.Get(tr, device) is not { } entry ||
            Projections.FrameAt((Alignment)tr.GetObject(view.AlignmentId, OpenMode.ForRead), device.Position) is not { } at)
        {
            return null;
        }

        var affine = DeviceService.PlacementOf(device).ToAffine();
        return ProfileProjection.Lines(entry.Instance.Solids.SelectMany(s => Wireframe.Of(s.Node, affine)), at.Frame);
    }

    // ------------------------------------------------------------------ editores de estilo

    private static SW.UIElement DeviceStyleEditor(IPanelHost host, DeviceStyle style)
    {
        var ui = new PanelUi(host.Theme).Section($"Estilo de dispositivo: {style.Name}");
        if (style.Description is not null)
        {
            ui.Note(style.Description);
        }

        if (style.Name == DeviceStyle.DefaultName && style.Components.Count == 0)
        {
            ui.Note("O estilo Padrão vazio usa a camada HY-<NOME> por sólido e a cor pelo material. Acrescente linhas para mudar.");
        }

        var rows = style.Components.Select(c => new ComponentRow(c)).ToList();
        var grid = ComponentGrid(rows, allowNames: true);
        ui.Add(grid);
        return ui.Action("Acrescentar linha", () =>
            {
                // guarda o que já foi digitado nas outras linhas antes de remontar a aba
                style.Components = rows.Select(r => r.ToComponent()).ToList();
                style.Components.Add(new StyleComponent { Name = "novo" });
                host.RebuildTabs();
            })
            .Action("Salvar", () => SaveDevice(host, style, rows, apply: false))
            .Action("Salvar e refazer os dispositivos", () => SaveDevice(host, style, rows, apply: true))
            .Action("Apagar estilo", () => Delete(host, style.Name, projection: false))
            .Element;
    }

    private static SW.UIElement ProjectionStyleEditor(IPanelHost host, ProjectionStyle style)
    {
        var ui = new PanelUi(host.Theme).Section($"Estilo de projeção: {style.Name}");
        var rows = ProjectionStyle.ComponentNames.Select(n => new ComponentRow(style.Component(n))).ToList();
        ui.Add(ComponentGrid(rows, allowNames: false));
        ui.Section("Hachura da face cortada");
        var pattern = Field(ui, "Padrão", style.Hatch.Pattern);
        var scale = Field(ui, "Escala", style.Hatch.Scale.ToString("0.###", CultureInfo.InvariantCulture));
        var angle = Field(ui, "Ângulo (°)", style.Hatch.Angle.ToString("0.##", CultureInfo.InvariantCulture));
        ui.Section("Visibilidade");
        var side = new WC.ComboBox { ItemsSource = Enum.GetNames(typeof(ProjectionSide)), SelectedItem = style.Side.ToString(), Width = 140, HorizontalAlignment = SW.HorizontalAlignment.Left };
        ui.Add(Labeled("Lado", side));
        var distance = Field(ui, "Distância máxima (m)", style.MaxDistance.ToString("0.##", CultureInfo.InvariantCulture));
        return ui.Action("Salvar", () =>
            {
                var copy = new ProjectionStyle
                {
                    Name = style.Name,
                    Description = style.Description,
                    Components = rows.Select(r => r.ToComponent()).ToList(),
                    Hatch = new HatchSettings { Pattern = pattern.Text.Trim(), Scale = Number(scale.Text, style.Hatch.Scale), Angle = Number(angle.Text, style.Hatch.Angle) },
                    Side = Enum.TryParse<ProjectionSide>(side.SelectedItem as string, out var s) ? s : style.Side,
                    MaxDistance = Number(distance.Text, style.MaxDistance),
                };
                Write(host, tr => StyleStore.Save(tr, host.Document!.Database, copy), $"estilo de projeção '{copy.Name}' salvo; use Atualizar saídas para refazer as projeções");
            })
            .Action("Duplicar", () =>
            {
                var copy = ProjectionStyle.FromJson(style.ToJson());
                copy.Name = style.Name + " (cópia)";
                Write(host, tr => StyleStore.Save(tr, host.Document!.Database, copy), $"estilo '{copy.Name}' criado");
            })
            .Action("Apagar estilo", () => Delete(host, style.Name, projection: true))
            .Element;
    }

    private static void SaveDevice(IPanelHost host, DeviceStyle style, List<ComponentRow> rows, bool apply)
    {
        var copy = new DeviceStyle { Name = style.Name, Description = style.Description, Components = rows.Select(r => r.ToComponent()).Where(c => c.Name.Length > 0).ToList() };
        Write(host, tr => StyleStore.Save(tr, host.Document!.Database, copy), $"estilo '{copy.Name}' salvo");
        if (apply)
        {
            host.Send("HY_ESTILO Atualizar  ", "Refazendo os dispositivos...");
        }
    }

    private static void Delete(IPanelHost host, string name, bool projection)
    {
        if (name == DeviceStyle.DefaultName)
        {
            host.Say("o estilo Padrão não pode ser apagado (volta ao padrão se ficar vazio)");
            return;
        }

        Write(host, tr => StyleStore.Delete(tr, host.Document!.Database, projection, name), $"estilo '{name}' apagado");
    }

    /// <summary>Grava no desenho a partir do painel (janela não modal: trava o documento).</summary>
    private static void Write(IPanelHost host, Action<Transaction> action, string message)
    {
        var document = host.Document;
        if (document is null)
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var tr = document.Database.TransactionManager.StartTransaction())
            {
                action(tr);
                tr.Commit();
            }

            HydroLog.Info("Saídas", message);
            host.Say(message);
            host.Refresh();
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        {
            HydroLog.Error("Saídas", "falha ao gravar o estilo", ex);
            host.Say("falha ao gravar: " + ex.Message);
        }
    }

    private static SW.UIElement ComponentGrid(List<ComponentRow> rows, bool allowNames)
    {
        var grid = new WC.Grid { Margin = new SW.Thickness(0, 4, 0, 4) };
        var headers = new[] { "Componente", "Visível", "Camada", "Cor (ACI)", "Material", "Tipo de linha" };
        foreach (var width in new[] { 1.2, 0.5, 1.4, 0.6, 1.0, 1.0 })
        {
            grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(width, SW.GridUnitType.Star) });
        }

        grid.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
        for (var c = 0; c < headers.Length; c++)
        {
            var header = new WC.TextBlock { Text = headers[c], FontWeight = SW.FontWeights.Bold, Margin = new SW.Thickness(0, 0, 4, 2) };
            WC.Grid.SetColumn(header, c);
            grid.Children.Add(header);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
            var row = rows[r];
            var cells = new SW.FrameworkElement[]
            {
                allowNames ? Box(row.Name, v => row.Name = v) : new WC.TextBlock { Text = row.Name, VerticalAlignment = SW.VerticalAlignment.Center, ToolTip = row.Description },
                Check(row.Visible, v => row.Visible = v),
                Box(row.Layer, v => row.Layer = v),
                Box(row.Color, v => row.Color = v),
                Box(row.Material, v => row.Material = v),
                Box(row.Linetype, v => row.Linetype = v),
            };
            for (var c = 0; c < cells.Length; c++)
            {
                cells[c].Margin = new SW.Thickness(0, 1, 4, 1);
                WC.Grid.SetRow(cells[c], r + 1);
                WC.Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
        }

        return grid;
    }

    private static WC.TextBox Box(string value, Action<string> set)
    {
        var box = new WC.TextBox { Text = value };
        box.TextChanged += (_, _) => set(box.Text);
        return box;
    }

    private static WC.CheckBox Check(bool value, Action<bool> set)
    {
        var box = new WC.CheckBox { IsChecked = value, VerticalAlignment = SW.VerticalAlignment.Center };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private static WC.TextBox Field(PanelUi ui, string label, string value)
    {
        var box = new WC.TextBox { Text = value, Width = 140, HorizontalAlignment = SW.HorizontalAlignment.Left };
        ui.Add(Labeled(label, box));
        return box;
    }

    private static SW.UIElement Labeled(string label, SW.FrameworkElement control)
    {
        var row = new WC.DockPanel { Margin = new SW.Thickness(0, 1, 0, 1) };
        var text = new WC.TextBlock { Text = label, Width = 170, VerticalAlignment = SW.VerticalAlignment.Center };
        WC.DockPanel.SetDock(text, WC.Dock.Left);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static double Number(string text, double fallback) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>Linha editável da tabela de componentes.</summary>
    private sealed class ComponentRow
    {
        public ComponentRow(StyleComponent component)
        {
            Name = component.Name;
            Visible = component.Visible;
            Layer = component.Layer ?? "";
            Color = component.Color?.ToString(CultureInfo.InvariantCulture) ?? "";
            Material = component.Material ?? "";
            Linetype = component.Linetype ?? "";
            Description = component.Description;
            Transparency = component.Transparency;
        }

        public string Name { get; set; }

        public bool Visible { get; set; }

        public string Layer { get; set; }

        public string Color { get; set; }

        public string Material { get; set; }

        public string Linetype { get; set; }

        public string? Description { get; }

        public int? Transparency { get; }

        public StyleComponent ToComponent() => new()
        {
            Name = Name.Trim(),
            Visible = Visible,
            Layer = Empty(Layer),
            Color = short.TryParse(Color, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci) && aci is >= 1 and <= 256 ? aci : null,
            Material = Empty(Material),
            Linetype = Empty(Linetype),
            Description = Description,
            Transparency = Transparency,
        };

        private static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
