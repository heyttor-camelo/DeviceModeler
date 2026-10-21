using System;
using System.Collections.Generic;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Hydro.Civil3D.Ui.Common;

/// <summary>Ícones vetoriais do HYDRO, na grade 32×32 do padrão do Viário: pictogramas desenhados em
/// código, sem arquivos externos e sem letras no lugar do desenho. Cores com significado, sempre as
/// mesmas: branco = estrutura/geometria; ciano = água, tubo, superfície de referência; verde =
/// elemento construído; amarelo = ação/destaque; vermelho = remover/erro; magenta = conector.</summary>
internal static class HydroIcons
{
    private static readonly WM.Color Plate = WM.Color.FromRgb(28, 43, 60);
    private static readonly WM.Color Water = WM.Color.FromRgb(90, 200, 240);
    private static readonly WM.Color Built = WM.Color.FromRgb(110, 210, 110);
    private static readonly WM.Color Yellow = WM.Color.FromRgb(244, 196, 64);
    private static readonly WM.Color Magenta = WM.Color.FromRgb(230, 90, 230);
    private static readonly WM.Color Orange = WM.Color.FromRgb(240, 160, 50);
    private static readonly WM.Color White = WM.Color.FromRgb(235, 240, 245);
    private static readonly WM.Color Red = WM.Color.FromRgb(240, 70, 70);
    private static readonly WM.Color WaterFill = WM.Color.FromArgb(70, 90, 200, 240);

    private sealed record Part(string Data, WM.Color? Stroke, double Thickness, WM.Color? Fill = null, bool Dashed = false);

    private static readonly Dictionary<string, Part[]> Parts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAINEL"] = new[]
        {
            new Part("M4,6 H28 V26 H4 Z", White, 1.5),
            new Part("M4,10 H28", White, 1.5),
            new Part("M7,13 H13 V17 H7 Z M7,19 H13 V23 H7 Z", Yellow, 1.2),
            new Part("M16,13 H25 M16,16 H23 M16,19 H25 M16,22 H22", Water, 1.2),
        },
        ["REDES"] = new[]
        {
            new Part("M7,24 L16,10 L26,22 M16,10 L27,6", Water, 2.0),
            new Part("M4,21 H10 V27 H4 Z M13,7 H19 V13 H13 Z M23,19 H29 V25 H23 Z", White, 1.4, Plate),
            new Part("M27,6 a1.6,1.6 0 1,0 0.1,0", Yellow, 1.4),
        },
        ["DISPOSITIVOS"] = new[]
        {
            new Part("M6,6 H26 V26 H6 Z", Built, 1.8),
            new Part("M9,9 H23 V23 H9 Z", Built, 1.0),
            new Part("M16,12 a4,4 0 1,0 0.1,0", White, 1.4),
            new Part("M1,16 H6 M26,16 H31", Water, 2.4),
        },
        ["BACIAS"] = new[]
        {
            new Part("M4,10 L14,4 L27,8 L29,20 L18,28 L6,24 Z", Water, 1.4, WaterFill),
            new Part("M14,8 C16,14 13,18 18,24", Water, 1.0, null, true),
            new Part("M18,24 L23,27", Yellow, 1.8),
            new Part("M21,25 H26 V30 H21 Z", White, 1.2),
        },
        ["SETORES"] = new[]
        {
            new Part("M3,6 H29 V26 H3 Z", Orange, 1.2, null, true),
            new Part("M8,18 L12,14 L16,18 V23 H8 Z M18,16 L22,12 L26,16 V23 H18 Z", White, 1.3),
            new Part("M3,28 H29", Water, 1.8),
        },
        ["VERIFICACAO"] = new[]
        {
            new Part("M14,4 L27,27 H1 Z", Yellow, 1.8),
            new Part("M14,12 V19", Yellow, 2.4),
            new Part("M14,23 V23.5", Yellow, 2.6),
            new Part("M21,18 L24,21 L31,12", Built, 2.2),
        },
        ["SAIDAS"] = new[]
        {
            new Part("M3,5 H23 V27 H3 Z", White, 1.3),
            new Part("M3,11 H23 M3,17 H23 M3,22 H23 M11,5 V27", White, 0.9),
            new Part("M19,20 L29,12 M25,11 H29 V15", Yellow, 1.8),
        },
        ["SIMULACAO"] = new[]
        {
            new Part("M4,4 V28 H29", White, 1.3),
            new Part("M5,26 C10,26 11,8 15,8 C19,8 20,22 28,24", Water, 2.0),
            new Part("M15,8 V27", Yellow, 1.0, null, true),
        },
        ["CATALOGO"] = new[]
        {
            new Part("M5,8 H15 V26 H5 Z", Built, 1.4),
            new Part("M18,4 H28 V26 H18 Z", White, 1.4),
            new Part("M8,13 H12 M8,17 H12 M21,9 H25 M21,13 H25 M21,17 H25", Water, 1.2),
            new Part("M3,28 H30", White, 1.4),
        },
        ["CONFIG"] = new[]
        {
            new Part("M16,10 a6,6 0 1,0 0.1,0", White, 1.8),
            new Part("M16,3 V8 M16,24 V29 M3,16 H8 M24,16 H29 M7,7 L10.5,10.5 M21.5,21.5 L25,25 M25,7 L21.5,10.5 M10.5,21.5 L7,25", White, 2.2),
            new Part("M16,14 a2,2 0 1,0 0.1,0", Yellow, 1.6),
        },
        ["INSERIR"] = new[]
        {
            new Part("M4,8 H20 V24 H4 Z", Built, 1.8),
            new Part("M12,12 a4,4 0 1,0 0.1,0", White, 1.3),
            new Part("M22,22 H30 M26,18 V26", Yellow, 2.2),
        },
        ["EDITAR"] = new[]
        {
            new Part("M4,8 H20 V24 H4 Z", Built, 1.8),
            new Part("M12,12 a4,4 0 1,0 0.1,0", White, 1.3),
            new Part("M18,26 L19,22 L28,13 L31,16 L22,25 Z", Yellow, 1.4),
        },
        ["CONECTAR"] = new[]
        {
            new Part("M16,6 H28 V26 H16 Z", Built, 1.8),
            new Part("M2,12 H16 M2,20 H16", Water, 1.8),
            new Part("M8,16 H14 M11,13 L14,16 L11,19", Yellow, 1.8),
            new Part("M16,16 a1.4,1.4 0 1,0 0.1,0", Magenta, 1.6),
        },
        ["DESCONECTAR"] = new[]
        {
            new Part("M16,6 H28 V26 H16 Z", Built, 1.8),
            new Part("M2,12 H12 M2,20 H12", Water, 1.8),
            new Part("M8,9 L14,15 M14,9 L8,15", Red, 2.2),
        },
        ["SUPERFICIE"] = new[]
        {
            new Part("M2,12 C8,8 12,14 18,10 C22,7 26,9 30,8", Built, 2.0),
            new Part("M11,16 H21 V30 H11 Z", White, 1.4),
            new Part("M16,3 V11 M13,8 L16,11 L19,8", Yellow, 1.8),
        },
        ["ATUALIZAR"] = new[]
        {
            new Part("M24,10 A10,10 0 1,0 26,18", Water, 2.0),
            new Part("M24,4 V10 H18", Yellow, 2.0),
        },
        ["INFO"] = new[]
        {
            new Part("M16,4 a12,12 0 1,0 0.1,0", Water, 1.8),
            new Part("M16,14 V24", Water, 2.6),
            new Part("M16,9 V9.5", Water, 2.8),
        },
        ["QUANTITATIVOS"] = new[]
        {
            new Part("M6,11 L16,6 L26,11 V23 L16,28 L6,23 Z M6,11 L16,16 L26,11 M16,16 V28", White, 1.4),
            new Part("M4,14 V26 M2,14 H6 M2,26 H6", Yellow, 1.4),
            new Part("M16,16 L26,11 V23 L16,28 Z", Built, 0.8, WM.Color.FromArgb(60, 110, 210, 110)),
        },
        ["SWMM"] = new[]
        {
            new Part("M3,24 L11,14 L20,20 L29,8", Water, 2.0),
            new Part("M11,14 a2.5,2.5 0 1,0 0.1,0 M20,20 a2.5,2.5 0 1,0 0.1,0", White, 1.4, Plate),
            new Part("M22,4 H29 V11 M29,4 L22,11", Yellow, 1.8),
        },
        ["PASTA"] = new[]
        {
            new Part("M3,9 H12 L14,12 H29 V26 H3 Z", Yellow, 1.6),
            new Part("M3,15 H29", Yellow, 1.0),
        },
        ["LOG"] = new[]
        {
            new Part("M7,4 H21 L26,9 V28 H7 Z", White, 1.4),
            new Part("M10,13 H22 M10,17 H22 M10,21 H18", Water, 1.2),
            new Part("M21,4 V9 H26", White, 1.2),
        },
        ["ZOOM"] = new[]
        {
            new Part("M13,6 a7,7 0 1,0 0.1,0", Water, 1.8),
            new Part("M18,18 L27,27", Yellow, 2.6),
        },
        ["EMBREVE"] = new[]
        {
            new Part("M16,4 a12,12 0 1,0 0.1,0", White, 1.6),
            new Part("M16,9 V16 L21,19", Yellow, 2.0),
        },
        ["REDE"] = new[]
        {
            new Part("M2,10 H10 V18 H2 Z M22,14 H30 V22 H22 Z", White, 1.4),
            new Part("M10,14 L22,18", Water, 2.4),
            new Part("M12,24 H22 M19,21 L22,24 L19,27", Yellow, 1.8),
        },
        ["NUMERAR"] = new[]
        {
            new Part("M4,22 H10 V28 H4 Z M13,15 H19 V21 H13 Z M22,8 H28 V14 H22 Z", White, 1.3),
            new Part("M10,25 L13,18 M19,18 L22,11", Water, 1.6),
            new Part("M7,13 V18 M15,6 V11 M17,6 V11 M24,1 V5 M26,1 V5 M28,1 V5", Yellow, 1.4),
        },
        ["AUDITAR"] = new[]
        {
            new Part("M4,4 H18 V26 H4 Z", White, 1.3),
            new Part("M7,9 H15 M7,13 H15 M7,17 H12", Water, 1.1),
            new Part("M21,14 a5,5 0 1,0 0.1,0", Yellow, 1.8),
            new Part("M24.5,22.5 L29,27", Yellow, 2.4),
        },
        ["PROJETAR"] = new[]
        {
            new Part("M3,5 V27 H29", White, 1.2),
            new Part("M3,12 C10,10 18,14 29,11", Built, 1.6),
            new Part("M13,13 H19 V25 H13 Z", White, 1.4),
            new Part("M13,17 L17,13 M13,21 L19,15 M14,25 L19,20", Water, 0.9),
            new Part("M3,24 L13,22 M19,21 L29,19", Water, 1.6),
        },
        ["LABEL"] = new[]
        {
            new Part("M4,26 H12 V20 H4 Z", Built, 1.4),
            new Part("M10,20 L18,10 H29", Yellow, 1.4),
            new Part("M18,6 H29 M18,14 H26", White, 1.4),
        },
        ["TABELA"] = new[]
        {
            new Part("M3,6 H29 V26 H3 Z", White, 1.3),
            new Part("M3,11 H29", White, 1.8),
            new Part("M3,16 H29 M3,21 H29 M12,6 V26 M21,6 V26", Water, 0.9),
        },
        ["ESTILO"] = new[]
        {
            new Part("M4,6 H14 V16 H4 Z", Built, 1.2, Built),
            new Part("M18,6 H28 V16 H18 Z", Water, 1.2, Water),
            new Part("M4,20 H14 V30 H4 Z", Orange, 1.2, Orange),
            new Part("M18,20 H28 V30 H18 Z", White, 1.2),
        },
        ["GRIP"] = new[]
        {
            new Part("M6,6 H26 V26 H6 Z", White, 1.2, null, true),
            new Part("M13,13 H19 V19 H13 Z", Water, 1.4, Water),
            new Part("M22,10 L28,16 L22,22 Z", Yellow, 1.4, Yellow),
        },
        ["CONECTOR"] = new[]
        {
            new Part("M6,6 H26 V26 H6 Z", Built, 1.6),
            new Part("M23,13 H29 V19 H23 Z", Magenta, 1.6, Magenta),
            new Part("M10,16 H22", Water, 1.4, null, true),
        },
        ["APAGAR"] = new[]
        {
            new Part("M8,8 H24 L22,28 H10 Z", White, 1.4),
            new Part("M6,8 H26 M13,5 H19", White, 1.4),
            new Part("M12,13 L20,23 M20,13 L12,23", Red, 2.0),
        },
    };

    public static bool Has(string key) => Parts.ContainsKey(key);

    /// <summary>Desenho vetorial (escala livre). Com <paramref name="plate"/>, sobre a placa escura do
    /// painel; sem ela (Ribbon com fundo claro), o branco do desenho vira <paramref name="white"/>.</summary>
    public static WM.DrawingImage Image(string key, bool plate = true, WM.Color? white = null)
    {
        var group = new WM.DrawingGroup();
        group.Children.Add(plate
            ? new WM.GeometryDrawing(new WM.SolidColorBrush(Plate), null, new WM.RectangleGeometry(new System.Windows.Rect(0, 0, 32, 32), 5, 5))
            : new WM.GeometryDrawing(WM.Brushes.Transparent, null, new WM.RectangleGeometry(new System.Windows.Rect(0, 0, 32, 32))));

        if (!Parts.TryGetValue(key, out var parts))
        {
            parts = Parts["PAINEL"];
        }

        foreach (var part in parts)
        {
            WM.Pen? pen = null;
            if (part.Stroke is { } stroke)
            {
                if (white is { } w && stroke == White)
                {
                    stroke = w;
                }

                pen = new WM.Pen(new WM.SolidColorBrush(stroke), part.Thickness)
                {
                    StartLineCap = WM.PenLineCap.Round,
                    EndLineCap = WM.PenLineCap.Round,
                    LineJoin = WM.PenLineJoin.Round,
                };
                if (part.Dashed)
                {
                    pen.DashStyle = new WM.DashStyle(new double[] { 3, 2 }, 0);
                }
            }

            WM.Brush? fill = null;
            if (part.Fill is { } f)
            {
                fill = new WM.SolidColorBrush(white is { } wf && f == White ? wf : !plate && f == Plate ? WM.Colors.Transparent : f);
            }

            group.Children.Add(new WM.GeometryDrawing(fill, pen, WM.Geometry.Parse(part.Data)));
        }

        var image = new WM.DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Bitmap do ícone (Ribbon), sem placa e com o branco no tom pedido.</summary>
    public static WM.ImageSource Bitmap(string key, int size, WM.Color? white = null)
    {
        var visual = new WM.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(Image(key, plate: false, white), new System.Windows.Rect(0, 0, size, size));
        }

        var bitmap = new WMI.RenderTargetBitmap(size, size, 96, 96, WM.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
