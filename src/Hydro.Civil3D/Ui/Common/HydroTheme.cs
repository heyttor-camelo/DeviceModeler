using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WM = System.Windows.Media;

namespace Hydro.Civil3D.Ui.Common;

/// <summary>Cores das janelas do HYDRO nos temas claro e escuro (as mesmas do painel do Viário).
/// O monitor gráfico é escuro nos dois temas, como no Project Explorer.</summary>
internal sealed record HydroTheme(
    WM.Brush Back, WM.Brush Pane, WM.Brush Header, WM.Brush Line, WM.Brush Text, WM.Brush Muted,
    WM.Brush Input, WM.Brush Accent, WM.Brush Warn, WM.Brush Selected, WM.Brush Error)
{
    public static readonly HydroTheme Light = new(
        Brush(240, 240, 240), Brush(255, 255, 255), Brush(229, 229, 229), Brush(200, 200, 200),
        Brush(20, 20, 20), Brush(105, 105, 105), Brush(255, 255, 240), Brush(0, 95, 170),
        Brush(190, 110, 0), Brush(204, 228, 247), Brush(200, 40, 40));

    public static readonly HydroTheme Dark = new(
        Brush(45, 45, 48), Brush(37, 37, 38), Brush(58, 58, 62), Brush(70, 70, 74),
        Brush(222, 222, 222), Brush(150, 150, 150), Brush(30, 30, 30), Brush(80, 160, 255),
        Brush(230, 165, 20), Brush(38, 79, 120), Brush(240, 90, 90));

    public static readonly WM.Brush GraphicBack = Brush(56, 58, 62);
    public static readonly WM.Brush GraphicGrid = Brush(66, 68, 73);
    public static readonly WM.Brush GraphicGridMajor = Brush(86, 88, 94);
    public static readonly WM.Brush GraphicText = Brush(205, 205, 205);
    public static readonly WM.Brush Highlight = Brush(255, 176, 0);

    public static readonly WM.FontFamily UiFont = new("Tahoma");
    public static readonly WM.FontFamily MonoFont = new("Consolas");

    public static HydroTheme Current => Preferences.Get("tema") == "escuro" ? Dark : Light;

    public bool IsDark => ReferenceEquals(this, Dark);

    public static WM.SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new WM.SolidColorBrush(WM.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static string Hex(WM.Brush brush) =>
        brush is WM.SolidColorBrush s ? $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "#FF808080";
}

/// <summary>Preferências de interface do usuário (tema, última categoria...), em
/// %APPDATA%\HYDRO\painel.txt, no formato <c>chave=valor</c>. São conveniência: falhas de
/// leitura ou gravação são ignoradas.</summary>
internal static class Preferences
{
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HYDRO", "painel.txt");

    public static string? Get(string key) => Read().TryGetValue(key, out var value) ? value : null;

    public static void Set(string key, string value)
    {
        try
        {
            var map = Read();
            map[key] = value;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllLines(Path, map.Select(kv => kv.Key + "=" + kv.Value));
        }
        catch (Exception)
        {
            // preferência é conveniência
        }
    }

    private static Dictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(Path))
            {
                foreach (var line in File.ReadAllLines(Path))
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                    }
                }
            }
        }
        catch (Exception)
        {
            // idem
        }

        return map;
    }
}
