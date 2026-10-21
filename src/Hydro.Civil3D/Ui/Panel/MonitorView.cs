using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hydro.Civil3D.Ui.Common;
using SW = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WS = System.Windows.Shapes;

namespace Hydro.Civil3D.Ui.Panel;

/// <summary>Monitor gráfico do painel: fundo escuro nos dois temas, grade, roda = zoom, arrastar =
/// pan, duplo clique = enquadrar. As categorias montam uma lista de primitivas em coordenadas do
/// mundo (Y para cima) e o monitor enquadra e redesenha sozinho ao mudar tamanho, zoom ou pan.</summary>
internal sealed class MonitorView
{
    private readonly List<Primitive> _items = new();
    private readonly WC.Canvas _canvas;
    private double _zoom = 1;
    private SW.Vector _pan;
    private SW.Point? _dragFrom;
    private double _scale = 1;
    private double _cx;
    private double _cy;

    public MonitorView(WC.Canvas canvas)
    {
        _canvas = canvas;
        _canvas.Background = HydroTheme.GraphicBack;
        _canvas.ClipToBounds = true;
        _canvas.SizeChanged += (_, _) => Render();
        _canvas.MouseWheel += (_, e) =>
        {
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25 : 0.8), 0.05, 500);
            Render();
        };
        _canvas.MouseDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _zoom = 1;
                _pan = default;
                Render();
                return;
            }

            _dragFrom = e.GetPosition(_canvas);
            _canvas.CaptureMouse();
        };
        _canvas.MouseUp += (_, _) =>
        {
            _dragFrom = null;
            _canvas.ReleaseMouseCapture();
        };
        _canvas.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            if (_dragFrom is { } from)
            {
                _pan += p - from;
                _dragFrom = p;
                Render();
            }

            var (x, y) = ToWorld(p);
            Coordinates?.Invoke(_items.Count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"X {x:0.000}  Y {y:0.000}"));
        };
    }

    /// <summary>Texto das coordenadas sob o cursor (barra do monitor).</summary>
    public event Action<string>? Coordinates;

    public bool ShowGrid { get; set; } = true;

    public bool ShowTexts { get; set; } = true;

    /// <summary>Mensagem centralizada (categoria sem conteúdo, desenho vazio...).</summary>
    public string? Message { get; set; }

    /// <summary>Unidade da grade nas coordenadas do mundo; o rótulo do eixo vertical (perfil) usa outra escala.</summary>
    public string Units { get; set; } = "m";

    public void Clear()
    {
        _items.Clear();
        Message = null;
        Units = "m";
    }

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
    }

    public void Polyline(IEnumerable<(double X, double Y)> points, WM.Brush stroke, double thickness = 1, bool closed = false, bool dashed = false, WM.Brush? fill = null) =>
        _items.Add(new Primitive(Kind.Line, points.ToList(), stroke, thickness, closed, dashed, fill, null, 0));

    public void Circle((double X, double Y) center, double radius, WM.Brush stroke, double thickness = 1, WM.Brush? fill = null)
    {
        var points = Enumerable.Range(0, 36).Select(i => (center.X + (radius * Math.Cos(i * Math.PI / 18)), center.Y + (radius * Math.Sin(i * Math.PI / 18))));
        Polyline(points, stroke, thickness, closed: true, fill: fill);
    }

    /// <summary>Marca de tamanho fixo em pixels (conector, nó).</summary>
    public void Marker((double X, double Y) at, WM.Brush brush, double pixels = 7) =>
        _items.Add(new Primitive(Kind.Marker, new List<(double, double)> { at }, brush, 1, false, false, brush, null, pixels));

    public void Text((double X, double Y) at, string text, WM.Brush brush, double size = 11) =>
        _items.Add(new Primitive(Kind.Text, new List<(double, double)> { at }, brush, 1, false, false, null, text, size));

    public void Render()
    {
        _canvas.Children.Clear();
        var width = Math.Max(_canvas.ActualWidth, 1);
        var height = Math.Max(_canvas.ActualHeight, 1);
        var points = _items.Where(i => i.Kind != Kind.Text || ShowTexts).SelectMany(i => i.Points).ToList();
        if (points.Count > 0)
        {
            var (minX, maxX) = (points.Min(p => p.X), points.Max(p => p.X));
            var (minY, maxY) = (points.Min(p => p.Y), points.Max(p => p.Y));
            var spanX = Math.Max(maxX - minX, 1e-6);
            var spanY = Math.Max(maxY - minY, 1e-6);
            var fit = Math.Min((width - 40) / spanX, (height - 40) / spanY);
            _scale = Math.Max(fit, 1e-9) * _zoom;
            _cx = (minX + maxX) / 2;
            _cy = (minY + maxY) / 2;
            if (ShowGrid)
            {
                DrawGrid(width, height);
            }

            foreach (var item in _items)
            {
                Draw(item);
            }
        }

        if (!string.IsNullOrEmpty(Message))
        {
            var text = new WC.TextBlock
            {
                Text = Message,
                Foreground = HydroTheme.GraphicText,
                FontSize = 13,
                TextAlignment = SW.TextAlignment.Center,
                TextWrapping = SW.TextWrapping.Wrap,
                Width = Math.Max(width - 40, 10),
            };
            text.Measure(new SW.Size(width, height));
            WC.Canvas.SetLeft(text, 20);
            WC.Canvas.SetTop(text, Math.Max(0, (height - text.DesiredSize.Height) / 2));
            _canvas.Children.Add(text);
        }
    }

    private SW.Point ToScreen((double X, double Y) p) =>
        new((_canvas.ActualWidth / 2) + ((p.X - _cx) * _scale) + _pan.X, (_canvas.ActualHeight / 2) - ((p.Y - _cy) * _scale) + _pan.Y);

    private (double X, double Y) ToWorld(SW.Point p) =>
        (_cx + ((p.X - _pan.X - (_canvas.ActualWidth / 2)) / _scale), _cy - ((p.Y - _pan.Y - (_canvas.ActualHeight / 2)) / _scale));

    private void DrawGrid(double width, double height)
    {
        // espaçamento "redondo" (1, 2 ou 5 × 10^n) de ao menos 30 px
        var raw = 30 / _scale;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 5, 10 }.Select(m => m * power).First(s => s >= raw);
        var (x0, y1) = ToWorld(new SW.Point(0, 0));
        var (x1, y0) = ToWorld(new SW.Point(width, height));
        var count = 0;
        for (var x = Math.Floor(x0 / step) * step; x <= x1 && count < 400; x += step, count++)
        {
            var major = Math.Abs(Math.IEEERemainder(x, step * 5)) < step / 2;
            var sx = ToScreen((x, 0)).X;
            _canvas.Children.Add(new WS.Line { X1 = sx, X2 = sx, Y1 = 0, Y2 = height, Stroke = major ? HydroTheme.GraphicGridMajor : HydroTheme.GraphicGrid, StrokeThickness = 1 });
        }

        for (var y = Math.Floor(y0 / step) * step; y <= y1 && count < 800; y += step, count++)
        {
            var major = Math.Abs(Math.IEEERemainder(y, step * 5)) < step / 2;
            var sy = ToScreen((0, y)).Y;
            _canvas.Children.Add(new WS.Line { X1 = 0, X2 = width, Y1 = sy, Y2 = sy, Stroke = major ? HydroTheme.GraphicGridMajor : HydroTheme.GraphicGrid, StrokeThickness = 1 });
        }

        var label = new WC.TextBlock
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"grade {step:0.###} {Units}"),
            Foreground = HydroTheme.GraphicText,
            FontSize = 10,
            Opacity = 0.7,
        };
        WC.Canvas.SetLeft(label, 6);
        WC.Canvas.SetTop(label, 4);
        _canvas.Children.Add(label);
    }

    private void Draw(Primitive item)
    {
        switch (item.Kind)
        {
            case Kind.Line:
                var line = new WS.Polyline
                {
                    Points = new WM.PointCollection(item.Points.Select(ToScreen)),
                    Stroke = item.Stroke,
                    StrokeThickness = item.Thickness,
                    StrokeLineJoin = WM.PenLineJoin.Round,
                    Fill = item.Fill,
                };
                if (item.Closed && item.Points.Count > 2)
                {
                    line.Points.Add(ToScreen(item.Points[0]));
                }

                if (item.Dashed)
                {
                    line.StrokeDashArray = new WM.DoubleCollection { 4, 3 };
                }

                _canvas.Children.Add(line);
                break;
            case Kind.Marker:
                var at = ToScreen(item.Points[0]);
                var marker = new WS.Rectangle { Width = item.Size, Height = item.Size, Fill = item.Fill, Stroke = item.Stroke };
                WC.Canvas.SetLeft(marker, at.X - (item.Size / 2));
                WC.Canvas.SetTop(marker, at.Y - (item.Size / 2));
                _canvas.Children.Add(marker);
                break;
            case Kind.Text when ShowTexts:
                var position = ToScreen(item.Points[0]);
                var text = new WC.TextBlock { Text = item.Text, Foreground = item.Stroke, FontSize = item.Size };
                WC.Canvas.SetLeft(text, position.X + 4);
                WC.Canvas.SetTop(text, position.Y - item.Size - 4);
                _canvas.Children.Add(text);
                break;
        }
    }

    private enum Kind
    {
        Line,
        Marker,
        Text,
    }

    private sealed record Primitive(Kind Kind, List<(double X, double Y)> Points, WM.Brush Stroke, double Thickness, bool Closed, bool Dashed, WM.Brush? Fill, string? Text, double Size);
}
