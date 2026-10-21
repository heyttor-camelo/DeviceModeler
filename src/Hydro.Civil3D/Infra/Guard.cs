using System;
using System.Collections.Generic;

namespace Hydro.Civil3D.Infra;

/// <summary>Barreira para código que o AutoCAD chama fora de um comando: overrules, desenho e
/// amostragem de jigs, eventos do banco e do documento, monitor do cursor, menu de contexto e painel.
/// Uma exceção que escapa dali derruba o AutoCAD; aqui ela vai para o <see cref="HydroLog"/> e para.
/// O mesmo erro no mesmo lugar é registrado no máximo a cada 10 s (o desenho roda dezenas de vezes
/// por segundo). Não pega estouro de pilha: o .NET encerra o processo sem passar por catch.</summary>
internal static class Guard
{
    private static readonly Dictionary<string, DateTime> LastLogged = new(StringComparer.Ordinal);

    public static void Run(string area, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(area, ex);
        }
    }

    public static T Run<T>(string area, Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Report(area, ex);
            return fallback;
        }
    }

    /// <summary>Barreira única para os eventos das janelas WPF (cliques, listas, canvas, timers do
    /// painel e das janelas de log e progresso), que passam todos pelo Dispatcher da thread principal.
    /// Só trata exceções que passaram pelo nosso código; as do AutoCAD seguem o caminho dele.</summary>
    public static void WatchDispatcher()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
        {
            if (IsOurs(e.Exception))
            {
                Report("Janelas", e.Exception);
                e.Handled = true;
            }
        };
    }

    private static bool IsOurs(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var stack = e.StackTrace ?? "";
            if (stack.Contains("Hydro.Civil3D.", StringComparison.Ordinal) || stack.Contains("Hydro.Core.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static void Report(string area, Exception ex)
    {
        try
        {
            var key = area + "|" + ex.GetType().FullName + "|" + ex.Message;
            var now = DateTime.UtcNow;
            lock (LastLogged)
            {
                if (LastLogged.TryGetValue(key, out var last) && (now - last).TotalSeconds < 10)
                {
                    return;
                }

                LastLogged[key] = now;
            }

            HydroLog.Error(area, "erro interno (o AutoCAD continua)", ex);
        }
        catch (Exception)
        {
            // o log nunca pode derrubar o AutoCAD
        }
    }
}
