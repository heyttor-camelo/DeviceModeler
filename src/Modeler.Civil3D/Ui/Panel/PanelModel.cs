using System;
using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.AutoCAD.ApplicationServices;
using Modeler.Civil3D.Ui.Common;
using SW = System.Windows;

namespace Modeler.Civil3D.Ui.Panel;

/// <summary>Botão da barra de comandos: ícone, comando enviado à linha de comando (ou ação local),
/// título, grupo (separado por │), descrição e passos do tooltip. Com <see cref="Menu"/>, o botão
/// abre um menu suspenso com essas opções.</summary>
internal sealed record PanelCommand(
    string Icon,
    string Title,
    string Group,
    string Description,
    string[] Steps,
    string? Send = null,
    Action? Run = null,
    IReadOnlyList<PanelMenuItem>? Menu = null)
{
    /// <summary>Nome do comando para o tooltip (primeira palavra do texto enviado).</summary>
    public string CommandName => Send is null ? "" : Send.Trim().Split(' ')[0];
}

/// <summary>Opção de menu suspenso: comando enviado ou ação local; <see cref="Checked"/> mostra a marca.</summary>
internal sealed record PanelMenuItem(string Title, string? Send = null, Action? Run = null, Func<bool>? Checked = null, bool Separator = false)
{
    public static readonly PanelMenuItem Line = new("", Separator: true);
}

/// <summary>Aba do item selecionado.</summary>
internal sealed record PanelTab(string Key, string Title, Func<SW.UIElement> Build);

/// <summary>Linha da lista. <see cref="Level"/>: 0 normal, 1 aviso, 2 erro (coluna de aviso).</summary>
internal sealed class PanelRow : INotifyPropertyChanged
{
    private bool _checked;

    public PanelRow(string id, string icon, string[] cells, object? data, int level = 0, string? hint = null)
    {
        Id = id;
        Icon = icon;
        Cells = cells;
        Data = data;
        Level = level;
        Hint = hint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Icon { get; }

    public string[] Cells { get; }

    public object? Data { get; }

    public int Level { get; }

    /// <summary>Texto do aviso (tooltip da coluna de aviso).</summary>
    public string? Hint { get; }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
            }
        }
    }

    public string this[int index] => index < Cells.Length ? Cells[index] : "";
}

/// <summary>Serviços do painel para as categorias.</summary>
internal interface IPanelHost
{
    HydroTheme Theme { get; }

    Document? Document { get; }

    SW.Window Window { get; }

    /// <summary>Manda um comando à linha de comando (cancela o que estiver em curso) e dá foco ao desenho.</summary>
    void Send(string command, string message);

    /// <summary>Mensagem na barra de status.</summary>
    void Say(string text);

    /// <summary>Relê a categoria atual e redesenha.</summary>
    void Refresh();

    /// <summary>Redesenha as abas (depois de mudar algo local).</summary>
    void RebuildTabs();
}

/// <summary>Uma categoria do painel: o que a lista mostra, o que o monitor mostra e as abas do item.
/// Categorias sem conteúdo ainda ficam com <see cref="Ready"/> falso e mostram "em breve".</summary>
internal interface IPanelCategory
{
    string Id { get; }

    string Icon { get; }

    string Title { get; }

    /// <summary>Atalho por linha de comando: <c>C3DHYDRO &lt;palavra&gt;</c>.</summary>
    string Keyword { get; }

    bool Ready { get; }

    /// <summary>Quantidade de itens (-1 = não mostrar) e de itens com aviso.</summary>
    (int Count, int Warnings) Count { get; }

    IReadOnlyList<PanelCommand> Commands { get; }

    string[] Columns { get; }

    double[] Widths { get; }

    IReadOnlyList<PanelRow> Rows { get; }

    /// <summary>Lê o desenho (ou o catálogo). Chamado na thread do AutoCAD, com o documento ativo.</summary>
    void Load(IPanelHost host);

    IEnumerable<PanelTab> Tabs(IPanelHost host, PanelRow? row);

    /// <summary>Desenha no monitor (planta, perfil ou prévia) a categoria e o item selecionado.</summary>
    void Draw(MonitorView view, PanelRow? selected);

    /// <summary>Ações do botão direito na lista.</summary>
    IEnumerable<PanelMenuItem> RowMenu(IPanelHost host, PanelRow row);
}
