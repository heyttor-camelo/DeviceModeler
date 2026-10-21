using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Modeler.Civil3D.Infra;
using SW = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WT = System.Windows.Threading;

namespace Modeler.Civil3D.Ui.Common;

/// <summary>Janela de progresso numa thread própria: continua desenhando (etapa, barra e tempo)
/// enquanto a operação ocupa a thread do AutoCAD. Cancelar só marca o pedido; a operação lê o pedido
/// entre uma etapa e outra e o que já foi feito fica. Só aparece se a operação passar de meio
/// segundo. Mesmo molde da ProgressoJanela do Civil3D2026Plugin.</summary>
internal sealed class ProgressWindow : IProgressView
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private WT.Dispatcher? _dispatcher;
    private SW.Window? _window;
    private volatile string _step = "";
    private long _fraction = BitConverter.DoubleToInt64Bits(0);
    private volatile bool _cancelled;
    private volatile bool _closed;

    private ProgressWindow()
    {
    }

    public bool Cancelled => _cancelled;

    /// <summary>Liga o progresso (com a janela) até o Dispose. Use com <c>using</c> em volta da operação.</summary>
    public static IDisposable Open(string title, SW.Window? over, bool cancellable = true)
    {
        if (Progress.Active)
        {
            return Progress.Begin(new ProgressWindow());
        }

        SW.Rect? area = null;
        try
        {
            area = ScreenArea(over);
        }
        catch (Exception)
        {
            // sem posição da janela: centraliza na tela
        }

        var window = new ProgressWindow();
        window.Start(title, area, HydroTheme.Current, cancellable);
        return Progress.Begin(window);
    }

    public void Show(string step, double fraction)
    {
        _step = step;
        if (!double.IsNaN(fraction))
        {
            Interlocked.Exchange(ref _fraction, BitConverter.DoubleToInt64Bits(Math.Clamp(fraction, 0, 1)));
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var dispatcher = _dispatcher;
        try
        {
            dispatcher?.BeginInvoke(new Action(() =>
            {
                if (_window is { IsVisible: true } window)
                {
                    window.Close();
                }
                else
                {
                    dispatcher.BeginInvokeShutdown(WT.DispatcherPriority.Background);
                }
            }));
        }
        catch (Exception)
        {
            // a thread da janela já terminou
        }
    }

    private static SW.Rect? ScreenArea(SW.Window? w)
    {
        if (w is null || !w.IsVisible || w.WindowState == SW.WindowState.Minimized)
        {
            return null;
        }

        var source = SW.PresentationSource.FromVisual(w);
        if (source?.CompositionTarget is null)
        {
            return null;
        }

        var m = source.CompositionTarget.TransformFromDevice;
        return new SW.Rect(m.Transform(w.PointToScreen(new SW.Point(0, 0))), m.Transform(w.PointToScreen(new SW.Point(w.ActualWidth, w.ActualHeight))));
    }

    private void Start(string title, SW.Rect? area, HydroTheme t, bool cancellable)
    {
        var thread = new Thread(() =>
        {
            try
            {
                Run(title, area, t, cancellable);
            }
            catch (Exception ex)
            {
                HydroLog.Error("HYDRO", "Janela de progresso", ex);
            }
            finally
            {
                _ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "HYDRO progresso",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _ready.Wait(3000);
    }

    private void Run(string title, SW.Rect? area, HydroTheme t, bool cancellable)
    {
        var dispatcher = WT.Dispatcher.CurrentDispatcher;
        _dispatcher = dispatcher;
        _ready.Set();
        const double width = 460;

        var titleText = new WC.TextBlock { Text = title, FontWeight = SW.FontWeights.Bold, FontSize = 13, Foreground = t.Text, TextTrimming = SW.TextTrimming.CharacterEllipsis };
        var stepText = new WC.TextBlock { Foreground = t.Text, Margin = new SW.Thickness(0, 8, 0, 6), TextTrimming = SW.TextTrimming.CharacterEllipsis };
        var bar = new WC.ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 1000, Height = 14, Foreground = t.Accent, Background = t.Input, BorderBrush = t.Line };
        var timeText = new WC.TextBlock { Foreground = t.Muted, VerticalAlignment = SW.VerticalAlignment.Center };
        var cancel = new WC.Button
        {
            Content = "Cancelar",
            Padding = new SW.Thickness(12, 3, 12, 3),
            Visibility = cancellable ? SW.Visibility.Visible : SW.Visibility.Collapsed,
            ToolTip = "Para depois da etapa atual. O que já foi feito fica no desenho.",
        };
        cancel.Click += (_, _) =>
        {
            _cancelled = true;
            cancel.IsEnabled = false;
            cancel.Content = "Cancelando...";
        };

        var footer = new WC.DockPanel { Margin = new SW.Thickness(0, 10, 0, 0) };
        WC.DockPanel.SetDock(cancel, WC.Dock.Right);
        footer.Children.Add(cancel);
        footer.Children.Add(timeText);
        var content = new WC.StackPanel { Margin = new SW.Thickness(16, 12, 16, 12) };
        content.Children.Add(titleText);
        content.Children.Add(stepText);
        content.Children.Add(bar);
        content.Children.Add(footer);

        var window = new SW.Window
        {
            Title = title,
            WindowStyle = SW.WindowStyle.None,
            ResizeMode = SW.ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Width = width,
            SizeToContent = SW.SizeToContent.Height,
            Background = t.Back,
            FontFamily = HydroTheme.UiFont,
            Content = new WC.Border { BorderBrush = t.Accent, BorderThickness = new SW.Thickness(1), Child = content },
        };
        _window = window;
        HydroStyles.Apply(window.Resources, t);
        if (area is { } r)
        {
            window.WindowStartupLocation = SW.WindowStartupLocation.Manual;
            window.Left = r.Left + ((r.Width - width) / 2);
            window.Top = r.Top + (r.Height * 0.55) - 60;
        }
        else
        {
            window.WindowStartupLocation = SW.WindowStartupLocation.CenterScreen;
        }

        var timer = new WT.DispatcherTimer(WT.DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            if (_closed)
            {
                timer.Stop();
                if (window.IsVisible)
                {
                    window.Close();
                }
                else
                {
                    dispatcher.BeginInvokeShutdown(WT.DispatcherPriority.Background);
                }

                return;
            }

            var fraction = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _fraction));
            stepText.Text = _step;
            bar.IsIndeterminate = fraction <= 0;
            if (fraction > 0)
            {
                bar.Value = fraction * 1000;
            }

            var elapsed = _clock.Elapsed;
            var text = "Tempo: " + Time(elapsed);
            if (fraction > 0.08 && fraction < 0.995 && elapsed.TotalSeconds > 5)
            {
                text += "   faltam ~" + Time(TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - fraction) / fraction));
            }

            timeText.Text = text;
            if (!window.IsVisible && elapsed.TotalMilliseconds > 500)
            {
                window.Show();
            }
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            dispatcher.BeginInvokeShutdown(WT.DispatcherPriority.Background);
        };
        timer.Start();
        WT.Dispatcher.Run();
    }

    private static string Time(TimeSpan t) =>
        ((int)t.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
}
