using System;
using System.Diagnostics;
using System.Linq;
using Modeler.Civil3D.Infra;
using SW = System.Windows;
using WC = System.Windows.Controls;
using WI = System.Windows.Input;

namespace Modeler.Civil3D.Ui.Common;

/// <summary>Janela do log comum (Ctrl+Shift+L em qualquer janela do HYDRO, ou o botão Log do
/// painel): só avisos e erros, copiar, abrir a pasta e limpar. Fecha só pelo X ou pelo botão.</summary>
internal static class LogWindow
{
    private static SW.Window? _window;

    /// <summary>Liga o atalho Ctrl+Shift+L na janela.</summary>
    public static void Shortcut(SW.Window owner)
    {
        owner.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WI.Key.L && WI.Keyboard.Modifiers == (WI.ModifierKeys.Control | WI.ModifierKeys.Shift))
            {
                Show();
                e.Handled = true;
            }
        };
    }

    public static void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var t = HydroTheme.Current;
        var problemsOnly = new WC.CheckBox { Content = "Só avisos e erros", VerticalAlignment = SW.VerticalAlignment.Center, Margin = new SW.Thickness(0, 0, 10, 0) };
        var box = new WC.TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = HydroTheme.MonoFont,
            FontSize = 11.5,
            TextWrapping = SW.TextWrapping.NoWrap,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Auto,
        };

        void Fill()
        {
            var entries = HydroLog.Snapshot().Where(e => problemsOnly.IsChecked != true || e.Level is LogLevel.Aviso or LogLevel.Erro);
            box.Text = string.Join(Environment.NewLine, entries.Select(e => e.Line));
            box.ScrollToEnd();
        }

        problemsOnly.Click += (_, _) => Fill();
        var buttons = new WC.WrapPanel { Margin = new SW.Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Button("Copiar", () => SW.Clipboard.SetText(HydroLog.Text(HydroLog.Snapshot()))));
        buttons.Children.Add(Button("Abrir pasta", () =>
        {
            System.IO.Directory.CreateDirectory(HydroLog.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", HydroLog.Folder) { UseShellExecute = true });
        }));
        buttons.Children.Add(Button("Limpar", () =>
        {
            HydroLog.Clear();
            Fill();
        }));
        buttons.Children.Add(Button("Fechar", () => _window?.Close()));

        var root = new WC.DockPanel { Margin = new SW.Thickness(8) };
        WC.DockPanel.SetDock(problemsOnly, WC.Dock.Top);
        WC.DockPanel.SetDock(buttons, WC.Dock.Bottom);
        root.Children.Add(problemsOnly);
        root.Children.Add(buttons);
        root.Children.Add(box);

        _window = new SW.Window
        {
            Title = "HYDRO - log",
            Width = 900,
            Height = 520,
            Background = t.Back,
            Foreground = t.Text,
            FontFamily = HydroTheme.UiFont,
            FontSize = 12,
            ShowInTaskbar = false,
            WindowStartupLocation = SW.WindowStartupLocation.CenterScreen,
            Content = root,
        };
        HydroStyles.Apply(_window.Resources, t);
        Action<LogEntry> added = _ => _window?.Dispatcher.BeginInvoke(new Action(Fill));
        HydroLog.Added += added;
        _window.Closed += (_, _) =>
        {
            HydroLog.Added -= added;
            _window = null;
        };
        Fill();
        Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_window);
    }

    private static WC.Button Button(string text, Action action)
    {
        var button = new WC.Button { Content = text, MinWidth = 90, Padding = new SW.Thickness(10, 3, 10, 3), Margin = new SW.Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }
}
