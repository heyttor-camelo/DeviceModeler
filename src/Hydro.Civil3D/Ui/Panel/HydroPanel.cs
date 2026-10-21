using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Ui.Common;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SW = System.Windows;
using WC = System.Windows.Controls;
using WD = System.Windows.Data;
using WI = System.Windows.Input;
using WM = System.Windows.Media;

namespace Hydro.Civil3D.Ui.Panel;

/// <summary>Painel único do HYDRO (C3DHYDRO), no molde do painel do Viário: de cima para baixo,
/// categorias com contagem → barra de comandos só com ícones → monitor gráfico escuro →
/// GridSplitter → lista com marcação → abas do item → barra de status. Janela WPF não modal de
/// instância única; os comandos que pedem clique no desenho continuam na linha de comando e o painel
/// só os envia. Esc nunca fecha a janela (só o X).</summary>
internal sealed class HydroPanel : SW.Window, IPanelHost
{
    private static HydroPanel? _instance;

    private readonly List<IPanelCategory> _categories;
    private IPanelCategory _category;
    private HydroTheme _theme = HydroTheme.Current;
    private WC.WrapPanel _categoryBar = new();
    private WC.StackPanel _commandBar = new();
    private WC.Canvas _canvas = new();
    private MonitorView _monitor = null!;
    private WC.TextBlock _coords = new();
    private WC.ListView _list = new();
    private WC.TextBox _find = new();
    private WC.StackPanel _tabStrip = new();
    private WC.ContentControl _tabContent = new();
    private WC.TextBlock _status = new();
    private string _tab = "Propriedades";
    private string? _selectedId;
    private bool _refreshQueued;

    private HydroPanel()
    {
        _categories = Categories.Create();
        var last = Preferences.Get("categoria");
        _category = _categories.FirstOrDefault(c => c.Id == last) ?? _categories.First(c => c.Id == "DISPOSITIVOS");
        LogWindow.Shortcut(this);
        Title = "HYDRO - drenagem";
        Width = 1100;
        Height = 760;
        MinWidth = 760;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = SW.WindowStartupLocation.CenterScreen;
        Icon = HydroIcons.Image("PAINEL");
        FontFamily = HydroTheme.UiFont;
        FontSize = 12;
        Content = BuildUi();
    }

    public HydroTheme Theme => _theme;

    public Document? Document => AcApp.DocumentManager.MdiActiveDocument;

    SW.Window IPanelHost.Window => this;

    /// <summary>Abre (ou traz para a frente) o painel, opcionalmente numa categoria ou num item.</summary>
    public static void ShowPanel(string? category = null, string? selectId = null)
    {
        if (_instance is null)
        {
            _instance = new HydroPanel();
            _instance.Closed += (_, _) =>
            {
                AcApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
                _instance = null;
            };
            AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
            AcApp.ShowModelessWindow(_instance);
        }
        else
        {
            _instance.Show();
            _instance.Activate();
        }

        if (category is not null && _instance._categories.FirstOrDefault(c => c.Id.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                                                                             c.Keyword.Equals(category, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            _instance._category = found;
            _instance._tab = "Propriedades";
        }

        if (selectId is not null)
        {
            _instance._selectedId = selectId;
        }

        _instance.Refresh();
    }

    /// <summary>Se o painel estiver aberto, leva-o à categoria e ao item (os comandos HY_* chamam isto
    /// ao terminar: HY_INFO mostra o dispositivo, HY_CATALOGO o catálogo...). Fechado, não abre.</summary>
    public static void Reveal(string category, string? selectId = null)
    {
        var panel = _instance;
        if (panel is null)
        {
            return;
        }

        if (panel._categories.FirstOrDefault(c => c.Id == category) is { } found && found != panel._category)
        {
            panel._category = found;
            panel._tab = "Propriedades";
        }

        panel._selectedId = selectId ?? panel._selectedId;
        NotifyChanged();
    }

    /// <summary>Palavras-chave das categorias (para o C3DHYDRO na linha de comando).</summary>
    public static IEnumerable<string> Keywords => Categories.Create().Select(c => c.Keyword);

    /// <summary>Pede um refresh se o painel estiver aberto (agrupa vários pedidos seguidos).</summary>
    public static void NotifyChanged()
    {
        var panel = _instance;
        if (panel is null || panel._refreshQueued)
        {
            return;
        }

        panel._refreshQueued = true;
        panel.Dispatcher.BeginInvoke(new Action(() =>
        {
            panel._refreshQueued = false;
            if (_instance == panel)
            {
                panel.Refresh();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public void Send(string command, string message)
    {
        var document = Document;
        if (document is null)
        {
            return;
        }

        Say(message);
        document.SendStringToExecute("\x03\x03" + command, true, false, false);
        try
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
        }
        catch (Exception)
        {
            // o comando roda mesmo sem foco
        }
    }

    public void Say(string text) => _status.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + "  " + text;

    public void Refresh()
    {
        // no meio de um comando (jig, prompt) o painel não lê o desenho: tenta de novo quando o
        // AutoCAD ficar ocioso
        if (!string.IsNullOrEmpty(Document?.CommandInProgress))
        {
            RefreshWhenIdle();
            return;
        }

        try
        {
            // só leitura: sem LockDocument (as gravações do painel travam o documento por conta própria)
            {
                foreach (var category in _categories.Where(c => c.Ready))
                {
                    try
                    {
                        category.Load(this);
                    }
                    catch (Exception ex)
                    {
                        HydroLog.Error("Painel", $"falha ao ler a categoria {category.Title}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HydroLog.Error("Painel", "falha ao atualizar o painel", ex);
        }

        FillCategories();
        FillCommands();
        FillList();
    }

    private bool _idleHooked;

    private void RefreshWhenIdle()
    {
        if (_idleHooked)
        {
            return;
        }

        _idleHooked = true;
        void OnIdle(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(Document?.CommandInProgress))
            {
                return;
            }

            AcApp.Idle -= OnIdle;
            _idleHooked = false;
            if (_instance == this)
            {
                Refresh();
            }
        }

        AcApp.Idle += OnIdle;
    }

    public void RebuildTabs() => BuildTabs();

    private static void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e) => NotifyChanged();

    // ------------------------------------------------------------------ montagem

    private SW.UIElement BuildUi()
    {
        Background = _theme.Back;
        Foreground = _theme.Text;
        HydroStyles.Apply(Resources, _theme);

        _categoryBar = new WC.WrapPanel { Margin = new SW.Thickness(4, 4, 4, 0) };
        _commandBar = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        _canvas = new WC.Canvas();
        _monitor = new MonitorView(_canvas);
        _coords = new WC.TextBlock { Foreground = _theme.Muted, VerticalAlignment = SW.VerticalAlignment.Center, Margin = new SW.Thickness(8, 0, 8, 0), FontFamily = HydroTheme.MonoFont };
        _monitor.Coordinates += text => _coords.Text = text;
        _list = new WC.ListView { BorderBrush = _theme.Line, Background = _theme.Pane, Foreground = _theme.Text, SelectionMode = WC.SelectionMode.Extended };
        _find = new WC.TextBox { Width = 160, VerticalContentAlignment = SW.VerticalAlignment.Center };
        _tabStrip = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        _tabContent = new WC.ContentControl();
        _status = new WC.TextBlock { Foreground = _theme.Muted, VerticalAlignment = SW.VerticalAlignment.Center, Margin = new SW.Thickness(6, 0, 6, 0) };

        var root = new WC.Grid();
        root.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new SW.GridLength(0.85, SW.GridUnitType.Star), MinHeight = 120 });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new SW.GridLength(5) });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new SW.GridLength(1, SW.GridUnitType.Star), MinHeight = 160 });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });

        root.Children.Add(_categoryBar);

        var commandRow = new WC.DockPanel { Margin = new SW.Thickness(4, 2, 4, 2) };
        var findPanel = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        findPanel.Children.Add(new WC.TextBlock { Text = "Localizar:", Foreground = _theme.Text, VerticalAlignment = SW.VerticalAlignment.Center, Margin = new SW.Thickness(8, 0, 4, 0) });
        findPanel.Children.Add(_find);
        _find.TextChanged += (_, _) => FillList();
        WC.DockPanel.SetDock(findPanel, WC.Dock.Right);
        commandRow.Children.Add(findPanel);
        commandRow.Children.Add(_commandBar);
        WC.Grid.SetRow(commandRow, 1);
        root.Children.Add(commandRow);

        var monitor = new WC.Border { BorderBrush = _theme.Line, BorderThickness = new SW.Thickness(1), Child = _canvas, Margin = new SW.Thickness(4, 0, 4, 0) };
        WC.Grid.SetRow(monitor, 2);
        root.Children.Add(monitor);

        var monitorBar = new WC.DockPanel { Margin = new SW.Thickness(4, 0, 4, 0), Background = _theme.Header, Height = 24 };
        var toggles = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        toggles.Children.Add(Toggle("Grade", () => _monitor.ShowGrid, v => _monitor.ShowGrid = v));
        toggles.Children.Add(Toggle("Textos", () => _monitor.ShowTexts, v => _monitor.ShowTexts = v));
        WC.DockPanel.SetDock(toggles, WC.Dock.Left);
        monitorBar.Children.Add(toggles);
        WC.DockPanel.SetDock(_coords, WC.Dock.Right);
        monitorBar.Children.Add(_coords);
        monitorBar.Children.Add(new WC.TextBlock
        {
            Text = "roda = zoom · arrastar = mover · duplo clique = enquadrar",
            Foreground = _theme.Muted,
            VerticalAlignment = SW.VerticalAlignment.Center,
            Margin = new SW.Thickness(8, 0, 8, 0),
        });
        WC.Grid.SetRow(monitorBar, 3);
        root.Children.Add(monitorBar);

        var split = new WC.GridSplitter { Height = 5, HorizontalAlignment = SW.HorizontalAlignment.Stretch, Background = _theme.Back };
        WC.Grid.SetRow(split, 4);
        root.Children.Add(split);

        var bottom = new WC.Grid { Margin = new SW.Thickness(4, 0, 4, 0) };
        bottom.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(1, SW.GridUnitType.Star), MinWidth = 260 });
        bottom.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(5) });
        bottom.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(1.15, SW.GridUnitType.Star), MinWidth = 300 });
        _list.SelectionChanged += (_, _) => OnRowSelected();
        _list.MouseDoubleClick += (_, _) => DoubleClick();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WI.Key.Space && _list.SelectedItems.Count > 0)
            {
                var selected = _list.SelectedItems.OfType<PanelRow>().ToList();
                var mark = selected.Any(r => !r.Checked);
                selected.ForEach(r => r.Checked = mark);
                e.Handled = true;
            }
        };
        _list.MouseRightButtonUp += (_, e) =>
        {
            if (_list.SelectedItem is PanelRow row)
            {
                OpenMenu(_category.RowMenu(this, row).ToList(), null);
                e.Handled = true;
            }
        };
        bottom.Children.Add(_list);
        var splitV = new WC.GridSplitter { Width = 5, HorizontalAlignment = SW.HorizontalAlignment.Stretch, Background = _theme.Back };
        WC.Grid.SetColumn(splitV, 1);
        bottom.Children.Add(splitV);

        var right = new WC.DockPanel();
        var strip = new WC.Border { Child = _tabStrip, BorderBrush = _theme.Line, BorderThickness = new SW.Thickness(0, 0, 0, 1) };
        WC.DockPanel.SetDock(strip, WC.Dock.Top);
        right.Children.Add(strip);
        right.Children.Add(new WC.Border { BorderBrush = _theme.Line, BorderThickness = new SW.Thickness(1, 0, 1, 1), Background = _theme.Pane, Child = _tabContent });
        WC.Grid.SetColumn(right, 2);
        bottom.Children.Add(right);
        WC.Grid.SetRow(bottom, 5);
        root.Children.Add(bottom);

        var footer = new WC.DockPanel { Margin = new SW.Thickness(4) };
        var leftButtons = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        leftButtons.Children.Add(FooterButton("Preferências...", ShowPreferences));
        leftButtons.Children.Add(FooterButton("Atualizar", Refresh));
        WC.DockPanel.SetDock(leftButtons, WC.Dock.Left);
        footer.Children.Add(leftButtons);
        var rightButtons = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        rightButtons.Children.Add(FooterButton("Log", LogWindow.Show));
        WC.DockPanel.SetDock(rightButtons, WC.Dock.Right);
        footer.Children.Add(rightButtons);
        footer.Children.Add(_status);
        WC.Grid.SetRow(footer, 6);
        root.Children.Add(footer);
        return root;
    }

    private WC.CheckBox Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var box = new WC.CheckBox { Content = text, IsChecked = get(), VerticalAlignment = SW.VerticalAlignment.Center, Margin = new SW.Thickness(8, 0, 4, 0) };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            _monitor.Render();
        };
        return box;
    }

    private static WC.Button FooterButton(string text, Action action)
    {
        var button = new WC.Button { Content = text, MinWidth = 96, Padding = new SW.Thickness(10, 3, 10, 3), Margin = new SW.Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void ShowPreferences()
    {
        OpenMenu(new List<PanelMenuItem>
        {
            new("Tema claro", Run: () => SetTheme(false), Checked: () => !_theme.IsDark),
            new("Tema escuro", Run: () => SetTheme(true), Checked: () => _theme.IsDark),
            PanelMenuItem.Line,
            new("Abrir o log (Ctrl+Shift+L)", Run: LogWindow.Show),
        }, null);
    }

    private void SetTheme(bool dark)
    {
        if (_theme.IsDark == dark)
        {
            return;
        }

        Preferences.Set("tema", dark ? "escuro" : "claro");
        _theme = dark ? HydroTheme.Dark : HydroTheme.Light;
        Content = BuildUi();
        Refresh();
    }

    // ------------------------------------------------------------------ conteúdo

    private void FillCategories()
    {
        _categoryBar.Children.Clear();
        foreach (var category in _categories)
        {
            var (count, warnings) = category.Ready ? category.Count : (-1, 0);
            var content = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
            content.Children.Add(new WC.Image { Source = HydroIcons.Image(category.Icon), Width = 18, Height = 18 });
            if (count >= 0)
            {
                content.Children.Add(new WC.TextBlock
                {
                    Text = warnings > 0 ? $" ({count} ⚠{warnings})" : $" ({count})",
                    Foreground = warnings > 0 ? _theme.Warn : _theme.Text,
                    VerticalAlignment = SW.VerticalAlignment.Center,
                });
            }

            var selected = category == _category;
            var button = new WC.Button
            {
                Content = content,
                ToolTip = category.Ready ? category.Title : category.Title + " (em breve)",
                Padding = new SW.Thickness(5, 2, 7, 2),
                Margin = new SW.Thickness(0, 0, 2, 0),
                Background = selected ? _theme.Selected : WM.Brushes.Transparent,
                BorderBrush = selected ? _theme.Accent : WM.Brushes.Transparent,
                BorderThickness = new SW.Thickness(0, 0, 0, 2),
                Opacity = category.Ready ? 1 : 0.6,
            };
            var target = category;
            button.Click += (_, _) => SelectCategory(target);
            _categoryBar.Children.Add(button);
        }
    }

    private void SelectCategory(IPanelCategory category)
    {
        _category = category;
        _selectedId = null;
        _tab = "Propriedades";
        _monitor.ResetView();
        Preferences.Set("categoria", category.Id);
        FillCategories();
        FillCommands();
        FillList();
    }

    private void FillCommands()
    {
        _commandBar.Children.Clear();
        var first = true;
        foreach (var group in _category.Commands.GroupBy(c => c.Group))
        {
            if (!first)
            {
                _commandBar.Children.Add(new WC.Border { Width = 1, Background = _theme.Line, Margin = new SW.Thickness(5, 3, 5, 3) });
            }

            first = false;
            foreach (var command in group)
            {
                var image = new WC.Image { Source = HydroIcons.Image(command.Icon), Width = 20, Height = 20 };
                var button = new WC.Button
                {
                    Content = command.Menu is null
                        ? image
                        : new WC.StackPanel
                        {
                            Orientation = WC.Orientation.Horizontal,
                            Children = { image, new WC.TextBlock { Text = "▾", Foreground = _theme.Muted, FontSize = 9, VerticalAlignment = SW.VerticalAlignment.Bottom } },
                        },
                    Padding = new SW.Thickness(2),
                    Background = WM.Brushes.Transparent,
                    BorderBrush = WM.Brushes.Transparent,
                    ToolTip = Tip(command),
                };
                WC.ToolTipService.SetInitialShowDelay(button, 250);
                WC.ToolTipService.SetShowDuration(button, 30000);
                var c = command;
                button.Click += (_, _) => Execute(c, button);
                _commandBar.Children.Add(button);
            }
        }

        if (!_category.Ready)
        {
            _commandBar.Children.Add(new WC.TextBlock
            {
                Text = _category.Title + ": em breve",
                Foreground = _theme.Muted,
                VerticalAlignment = SW.VerticalAlignment.Center,
                Margin = new SW.Thickness(6, 0, 0, 0),
            });
        }
    }

    private void Execute(PanelCommand command, SW.FrameworkElement anchor)
    {
        if (command.Menu is { } menu)
        {
            OpenMenu(menu, anchor);
            return;
        }

        Run(command.Title, command.Send, command.Run);
    }

    private void Run(string title, string? send, Action? run)
    {
        try
        {
            if (run is not null)
            {
                run();
            }
            else if (send is not null)
            {
                Send(send, title + ": siga a linha de comando.");
            }
        }
        catch (Exception ex)
        {
            HydroLog.Error("Painel", title, ex);
            Say($"{title}: {ex.Message}");
        }
    }

    private void OpenMenu(IReadOnlyList<PanelMenuItem> items, SW.FrameworkElement? anchor)
    {
        if (items.Count == 0)
        {
            return;
        }

        var menu = new WC.ContextMenu();
        if (anchor is not null)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = WC.Primitives.PlacementMode.Bottom;
        }

        foreach (var item in items)
        {
            if (item.Separator)
            {
                menu.Items.Add(new WC.Separator());
                continue;
            }

            var entry = new WC.MenuItem { Header = item.Title, IsCheckable = item.Checked is not null, IsChecked = item.Checked?.Invoke() == true };
            var i = item;
            entry.Click += (_, _) => Run(i.Title, i.Send, i.Run);
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    private static WC.ToolTip Tip(PanelCommand command)
    {
        var panel = new WC.StackPanel { MaxWidth = 380 };
        panel.Children.Add(new WC.TextBlock { Text = command.Title, FontWeight = SW.FontWeights.Bold });
        var row = new WC.DockPanel { Margin = new SW.Thickness(0, 4, 0, 0) };
        var image = new WC.Image { Source = HydroIcons.Image(command.Icon), Width = 96, Height = 96, Margin = new SW.Thickness(0, 0, 8, 0), VerticalAlignment = SW.VerticalAlignment.Top };
        WC.DockPanel.SetDock(image, WC.Dock.Left);
        row.Children.Add(image);
        var text = command.Description;
        if (command.Steps.Length > 0)
        {
            text += "\n\n" + string.Join("\n", command.Steps.Select((s, i) => $"{i + 1}. {s}"));
        }

        if (command.CommandName.Length > 0)
        {
            text += $"\n\nComando: {command.CommandName}";
        }

        row.Children.Add(new WC.TextBlock { Text = text, TextWrapping = SW.TextWrapping.Wrap });
        panel.Children.Add(row);
        return new WC.ToolTip { Content = panel };
    }

    private void FillList()
    {
        var filter = _find.Text.Trim();
        var rows = _category.Ready ? _category.Rows : Array.Empty<PanelRow>();
        var visible = filter.Length == 0
            ? rows.ToList()
            : rows.Where(r => r.Cells.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();

        _list.View = BuildGrid(_category.Columns, _category.Widths, visible);
        _list.ItemsSource = visible;
        var selected = visible.FirstOrDefault(r => r.Id == _selectedId);
        if (selected is not null)
        {
            _list.SelectedItem = selected;
            _list.ScrollIntoView(selected);
        }

        var total = _category.Ready ? $"{_category.Title}: {rows.Count} item(ns)" : $"{_category.Title}: em breve";
        _status.Text = filter.Length > 0 ? $"{total}, {visible.Count} no filtro" : total;
        OnRowSelected();
    }

    private WC.GridView BuildGrid(string[] columns, double[] widths, List<PanelRow> rows)
    {
        var grid = new WC.GridView();
        var check = new SW.FrameworkElementFactory(typeof(WC.CheckBox));
        check.SetBinding(WC.Primitives.ToggleButton.IsCheckedProperty, new WD.Binding(nameof(PanelRow.Checked)) { Mode = WD.BindingMode.TwoWay });
        check.SetValue(SW.FrameworkElement.ToolTipProperty, "Marcar para agir em lote (espaço marca a seleção)");
        grid.Columns.Add(new WC.GridViewColumn { Width = 28, CellTemplate = new SW.DataTemplate { VisualTree = check } });

        var warn = new SW.FrameworkElementFactory(typeof(WC.TextBlock));
        warn.SetBinding(WC.TextBlock.TextProperty, new WD.Binding(nameof(PanelRow.Level)) { Converter = new LevelText() });
        warn.SetBinding(WC.TextBlock.ForegroundProperty, new WD.Binding(nameof(PanelRow.Level)) { Converter = new LevelBrush(_theme) });
        warn.SetBinding(SW.FrameworkElement.ToolTipProperty, new WD.Binding(nameof(PanelRow.Hint)));
        grid.Columns.Add(new WC.GridViewColumn { Width = 24, CellTemplate = new SW.DataTemplate { VisualTree = warn } });

        for (var c = 0; c < columns.Length; c++)
        {
            var index = c;
            var numeric = rows.Count > 0 && rows.Take(50).All(r => r[index].Length == 0 || PanelUi.LooksNumeric(r[index]));
            var cell = new SW.FrameworkElementFactory(typeof(WC.StackPanel));
            cell.SetValue(WC.StackPanel.OrientationProperty, WC.Orientation.Horizontal);
            if (c == 0)
            {
                var icon = new SW.FrameworkElementFactory(typeof(WC.Image));
                icon.SetBinding(WC.Image.SourceProperty, new WD.Binding(nameof(PanelRow.Icon)) { Converter = new IconImage() });
                icon.SetValue(SW.FrameworkElement.WidthProperty, 16.0);
                icon.SetValue(SW.FrameworkElement.HeightProperty, 16.0);
                icon.SetValue(SW.FrameworkElement.MarginProperty, new SW.Thickness(0, 0, 4, 0));
                cell.AppendChild(icon);
            }

            var text = new SW.FrameworkElementFactory(typeof(WC.TextBlock));
            text.SetBinding(WC.TextBlock.TextProperty, new WD.Binding($"[{index}]"));
            if (numeric && c > 0)
            {
                text.SetValue(WC.TextBlock.FontFamilyProperty, HydroTheme.MonoFont);
            }

            cell.AppendChild(text);
            grid.Columns.Add(new WC.GridViewColumn
            {
                Header = columns[c],
                Width = c < widths.Length ? widths[c] : 100,
                CellTemplate = new SW.DataTemplate { VisualTree = cell },
            });
        }

        return grid;
    }

    private PanelRow? Current => _list.SelectedItem as PanelRow;

    private void OnRowSelected()
    {
        _selectedId = Current?.Id ?? _selectedId;
        BuildTabs();
        _monitor.Clear();
        if (!_category.Ready)
        {
            _monitor.Message = $"{_category.Title}\n\nEsta categoria ainda não tem conteúdo (em breve).";
        }
        else
        {
            try
            {
                _category.Draw(_monitor, Current);
            }
            catch (Exception ex)
            {
                HydroLog.Error("Painel", "falha ao desenhar o monitor", ex);
                _monitor.Message = "Falha ao desenhar: " + ex.Message;
            }
        }

        _monitor.Render();
    }

    private void BuildTabs()
    {
        var tabs = new List<PanelTab>();
        if (_category.Ready)
        {
            try
            {
                tabs.AddRange(_category.Tabs(this, Current));
            }
            catch (Exception ex)
            {
                HydroLog.Error("Painel", "falha ao montar as abas", ex);
                tabs.Add(new PanelTab("Propriedades", "Propriedades", () => new PanelUi(_theme).Note("Falha: " + ex.Message, warn: true).Element));
            }
        }

        if (tabs.Count == 0)
        {
            tabs.Add(new PanelTab("Propriedades", "Propriedades", () => new PanelUi(_theme)
                .Note(_category.Ready ? "Selecione um item da lista." : "Em breve: esta categoria entra numa próxima fase do HYDRO.")
                .Element));
        }

        if (tabs.All(t => t.Key != _tab))
        {
            _tab = tabs[0].Key;
        }

        _tabStrip.Children.Clear();
        foreach (var tab in tabs)
        {
            var active = tab.Key == _tab;
            var button = new WC.Button
            {
                Content = tab.Title,
                Padding = new SW.Thickness(10, 3, 10, 3),
                Background = active ? _theme.Pane : _theme.Back,
                Foreground = active ? _theme.Text : _theme.Muted,
                BorderBrush = active ? _theme.Accent : _theme.Line,
                BorderThickness = new SW.Thickness(0, active ? 2 : 0, 0, 0),
                FontWeight = active ? SW.FontWeights.Bold : SW.FontWeights.Normal,
            };
            var key = tab.Key;
            button.Click += (_, _) =>
            {
                _tab = key;
                BuildTabs();
            };
            _tabStrip.Children.Add(button);
        }

        try
        {
            _tabContent.Content = tabs.First(t => t.Key == _tab).Build();
        }
        catch (Exception ex)
        {
            HydroLog.Error("Painel", $"falha ao montar a aba {_tab}", ex);
            _tabContent.Content = new PanelUi(_theme).Note("Falha: " + ex.Message, warn: true).Element;
        }
    }

    private void DoubleClick()
    {
        if (Current is { } row && _category.RowMenu(this, row).FirstOrDefault(m => !m.Separator) is { } first)
        {
            Run(first.Title, first.Send, first.Run);
        }
    }

    private sealed class LevelText : WD.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int level ? level switch { 2 => "✖", 1 => "⚠", _ => "" } : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class LevelBrush : WD.IValueConverter
    {
        private readonly HydroTheme _theme;

        public LevelBrush(HydroTheme theme)
        {
            _theme = theme;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is 2 ? _theme.Error : _theme.Warn;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class IconImage : WD.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => HydroIcons.Image(value as string ?? "PAINEL");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
