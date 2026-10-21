using System;
using System.Collections.Generic;

namespace Hydro.Civil3D.Infra;

/// <summary>Quem mostra o progresso de uma operação longa (a janela de progresso do painel).</summary>
internal interface IProgressView
{
    /// <summary>Etapa atual e fração do total (0 a 1). Chamado na thread do AutoCAD.</summary>
    void Show(string step, double fraction);

    /// <summary>O usuário pediu para parar (lido só nos pontos seguros da operação).</summary>
    bool Cancelled { get; }

    void Close();
}

/// <summary>Progresso das operações longas. O código chama <see cref="Show"/>, <see cref="Step"/> e
/// <see cref="Range"/> sem saber quem mostra; sem visor ativo nada acontece (linha de comando,
/// atualização automática). <see cref="Range"/> divide o total em etapas e pode ser aninhada.</summary>
internal static class Progress
{
    private static readonly Stack<(double From, double To)> Ranges = new();
    private static IProgressView? _view;
    private static double _from;
    private static double _to = 1;
    private static string _last = "";

    public static bool Active => _view is not null;

    /// <summary>O usuário clicou em Cancelar (a operação decide onde para).</summary>
    public static bool Cancelled => _view?.Cancelled == true;

    /// <summary>Liga o visor até o Dispose. Se já há um ativo (operação dentro de outra), fica o de fora.</summary>
    public static IDisposable Begin(IProgressView view)
    {
        if (_view is not null)
        {
            view.Close();
            return new Disposer(() => { });
        }

        _view = view;
        Ranges.Clear();
        _from = 0;
        _to = 1;
        _last = "";
        return new Disposer(() =>
        {
            var current = _view;
            _view = null;
            Ranges.Clear();
            _from = 0;
            _to = 1;
            try
            {
                current?.Close();
            }
            catch (Exception)
            {
                // fechar a janela nunca derruba a operação
            }
        });
    }

    /// <summary>Etapa com a fração dentro da faixa atual (0 a 1; NaN mantém a posição).</summary>
    public static void Show(string step, double fraction = double.NaN)
    {
        if (_view is null)
        {
            return;
        }

        _last = step;
        var value = double.IsNaN(fraction) ? double.NaN : _from + ((_to - _from) * Math.Clamp(fraction, 0, 1));
        try
        {
            _view.Show(step, value);
        }
        catch (Exception)
        {
            // idem
        }
    }

    /// <summary>Item <paramref name="index"/> (a partir de 0) de <paramref name="count"/> dentro da faixa atual.</summary>
    public static void Step(string step, int index, int count) => Show(step, count > 0 ? (double)index / count : 0);

    /// <summary>Enquanto vivo, 0 a 1 passa a valer <paramref name="from"/> a <paramref name="to"/> da faixa atual.</summary>
    public static IDisposable Range(double from, double to)
    {
        if (_view is null)
        {
            return new Disposer(() => { });
        }

        Ranges.Push((_from, _to));
        var width = _to - _from;
        var start = _from + (width * Math.Clamp(from, 0, 1));
        var end = _from + (width * Math.Clamp(to, 0, 1));
        _from = start;
        _to = Math.Max(start, end);
        return new Disposer(() =>
        {
            if (_view is not null)
            {
                Show(_last, 1);
            }

            if (Ranges.Count > 0)
            {
                (_from, _to) = Ranges.Pop();
            }
        });
    }

    private sealed class Disposer : IDisposable
    {
        private Action? _action;

        public Disposer(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            var action = _action;
            _action = null;
            action?.Invoke();
        }
    }
}
