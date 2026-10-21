using System;
using System.Collections.Generic;
using System.Linq;
using Hydro.Civil3D.Ui.Common;
using SW = System.Windows;
using WC = System.Windows.Controls;

namespace Hydro.Civil3D.Ui.Panel;

/// <summary>Peças das abas do painel: propriedades em duas colunas com títulos de grupo, botões de
/// ação e tabelas simples (mesmo visual da aba Propriedades do Viário).</summary>
internal sealed class PanelUi
{
    private readonly HydroTheme _t;
    private readonly WC.StackPanel _stack = new() { Margin = new SW.Thickness(8, 6, 8, 8) };
    private WC.WrapPanel? _actions;

    public PanelUi(HydroTheme theme)
    {
        _t = theme;
    }

    public SW.UIElement Element => new WC.ScrollViewer { Content = _stack, VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto };

    public PanelUi Section(string title)
    {
        _stack.Children.Add(new WC.TextBlock
        {
            Text = title,
            FontWeight = SW.FontWeights.Bold,
            Foreground = _t.Accent,
            Margin = new SW.Thickness(0, _stack.Children.Count == 0 ? 0 : 10, 0, 3),
        });
        return this;
    }

    public PanelUi Prop(string label, string value, string? tooltip = null, bool warn = false)
    {
        var row = Row(label);
        var text = new WC.TextBlock
        {
            Text = value,
            Foreground = warn ? _t.Warn : _t.Text,
            TextWrapping = SW.TextWrapping.Wrap,
            FontFamily = LooksNumeric(value) ? HydroTheme.MonoFont : HydroTheme.UiFont,
            ToolTip = tooltip,
        };
        WC.Grid.SetColumn(text, 1);
        row.Children.Add(text);
        _stack.Children.Add(row);
        return this;
    }

    public PanelUi Note(string text, bool warn = false)
    {
        _stack.Children.Add(new WC.TextBlock
        {
            Text = text,
            Foreground = warn ? _t.Warn : _t.Muted,
            TextWrapping = SW.TextWrapping.Wrap,
            Margin = new SW.Thickness(0, 2, 0, 2),
        });
        return this;
    }

    public PanelUi Action(string text, Action action, string? tooltip = null)
    {
        if (_actions is null)
        {
            _actions = new WC.WrapPanel { Margin = new SW.Thickness(0, 10, 0, 0) };
            _stack.Children.Add(_actions);
        }

        var button = new WC.Button { Content = text, Padding = new SW.Thickness(10, 3, 10, 3), Margin = new SW.Thickness(0, 0, 6, 6), ToolTip = tooltip };
        button.Click += (_, _) => action();
        _actions.Children.Add(button);
        return this;
    }

    /// <summary>Tabela somente leitura (números à direita, em fonte mono).</summary>
    public PanelUi Table(string[] columns, IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        var grid = new WC.Grid { Margin = new SW.Thickness(0, 2, 0, 4) };
        for (var c = 0; c < columns.Length; c++)
        {
            grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = c == 0 ? new SW.GridLength(1.4, SW.GridUnitType.Star) : new SW.GridLength(1, SW.GridUnitType.Star) });
        }

        for (var r = 0; r <= list.Count; r++)
        {
            grid.RowDefinitions.Add(new WC.RowDefinition { Height = SW.GridLength.Auto });
            var cells = r == 0 ? columns : list[r - 1];
            for (var c = 0; c < columns.Length; c++)
            {
                var value = c < cells.Length ? cells[c] : "";
                var numeric = r > 0 && c > 0 && LooksNumeric(value);
                var cell = new WC.TextBlock
                {
                    Text = value,
                    Foreground = r == 0 ? _t.Muted : _t.Text,
                    FontWeight = r == 0 ? SW.FontWeights.Bold : SW.FontWeights.Normal,
                    FontFamily = numeric ? HydroTheme.MonoFont : HydroTheme.UiFont,
                    TextAlignment = numeric ? SW.TextAlignment.Right : SW.TextAlignment.Left,
                    Margin = new SW.Thickness(0, 1, 8, 1),
                    TextTrimming = SW.TextTrimming.CharacterEllipsis,
                    ToolTip = value.Length > 24 ? value : null,
                };
                WC.Grid.SetRow(cell, r);
                WC.Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        _stack.Children.Add(grid);
        if (list.Count == 0)
        {
            Note("(nenhum)");
        }

        return this;
    }

    public PanelUi Add(SW.UIElement element)
    {
        _stack.Children.Add(element);
        return this;
    }

    internal static bool LooksNumeric(string text) =>
        text.Length > 0 && text.TrimStart('-', '+').Length > 0 && char.IsDigit(text.TrimStart('-', '+')[0]) &&
        text.All(ch => char.IsDigit(ch) || ch is '.' or ',' or '-' or '+' or ' ' or '%' or '°' or '³' or '²' || char.IsLetter(ch));

    private WC.Grid Row(string label)
    {
        var row = new WC.Grid { Margin = new SW.Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(170) });
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new SW.GridLength(1, SW.GridUnitType.Star) });
        row.Children.Add(new WC.TextBlock { Text = label, Foreground = _t.Muted, TextTrimming = SW.TextTrimming.CharacterEllipsis, ToolTip = label });
        return row;
    }
}
