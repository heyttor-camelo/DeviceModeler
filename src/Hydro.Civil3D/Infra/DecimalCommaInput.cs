using System;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Hydro.Civil3D.Infra;

/// <summary>Enquanto existir, a vírgula digitada vira ponto antes de chegar à linha de comando (ou à
/// entrada dinâmica). Numa pergunta de ponto o AutoCAD lê "1,50" como o ponto (1; 50); com isto
/// ela chega como "1.50", um número. Usado só durante a pergunta do <c>HY_GRIP</c>, onde coordenadas
/// digitadas não têm uso (o ponto vem do mouse e do osnap). O AutoCAD devolve ao Windows a mensagem
/// alterada no <c>PreTranslateMessage</c>.</summary>
internal sealed class DecimalCommaInput : IDisposable
{
    private const int WmChar = 0x0102;

    private bool _disposed;

    public DecimalCommaInput()
    {
        AcApp.PreTranslateMessage += OnPreTranslateMessage;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            AcApp.PreTranslateMessage -= OnPreTranslateMessage;
        }
    }

    private static void OnPreTranslateMessage(object? sender, Autodesk.AutoCAD.ApplicationServices.PreTranslateMessageEventArgs e)
    {
        var message = e.Message;
        if (message.message == WmChar && (long)message.wParam == ',')
        {
            message.wParam = (IntPtr)'.';
            e.Message = message;
        }
    }
}
