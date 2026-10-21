using System;
using System.Globalization;
using System.Windows.Input;
using Autodesk.Windows;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Ui.Common;
using Hydro.Civil3D.Ui.Panel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WM = System.Windows.Media;

namespace Hydro.Civil3D.Ui;

/// <summary>Aba HYDRO na Ribbon: um botão grande por categoria do painel (atalhos
/// <c>C3DHYDRO &lt;categoria&gt;</c>), sem placa atrás do desenho e com as cores pelo tema do AutoCAD.
/// A Ribbon é só uma camada: o clique executa o mesmo comando da linha de comando.</summary>
internal static class HydroRibbon
{
    private const string TabId = "HYDRO_RIBBON";
    private static bool _idleHooked;

    public static void Initialize()
    {
        if (!Ensure())
        {
            Hook();
        }
    }

    public static void Terminate() => Unhook();

    private static bool Ensure()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon is null)
        {
            return false;
        }

        foreach (RibbonTab existing in ribbon.Tabs)
        {
            if (existing.Id == TabId)
            {
                return true;
            }
        }

        try
        {
            ribbon.Tabs.Add(Build());
        }
        catch (Exception ex)
        {
            HydroLog.Error("Ribbon", "falha ao montar a aba HYDRO", ex);
        }

        return true;
    }

    private static RibbonTab Build()
    {
        var tab = new RibbonTab { Id = TabId, Title = "HYDRO" };
        var handler = new Handler();
        var dark = IsDark();
        WM.Color? white = dark ? null : WM.Color.FromRgb(43, 48, 54);

        var source = new RibbonPanelSource { Title = "Drenagem" };
        source.Items.Add(Button("C3DHYDRO ", "Painel", "PAINEL", "Abre o painel HYDRO na última categoria usada.", handler, white));
        foreach (var category in Categories.Create())
        {
            source.Items.Add(Button($"C3DHYDRO {category.Keyword} ", category.Title, category.Icon,
                category.Ready ? $"Abre o painel HYDRO em {category.Title}." : $"{category.Title}: em breve.", handler, white));
        }

        tab.Panels.Add(new RibbonPanel { Source = source });

        var tools = new RibbonPanelSource { Title = "Dispositivos" };
        tools.Items.Add(Button("HY_INSERIR ", "Inserir", "INSERIR", "Insere um dispositivo do catálogo.", handler, white));
        tools.Items.Add(Button("HY_EDITAR ", "Editar", "EDITAR", "Edita os parâmetros de um dispositivo.", handler, white));
        tools.Items.Add(Button("HY_CONECTAR ", "Conectar", "CONECTAR", "Liga tubos da Pipe Network a um dispositivo.", handler, white));
        tab.Panels.Add(new RibbonPanel { Source = tools });
        return tab;
    }

    private static RibbonButton Button(string command, string text, string icon, string description, ICommand handler, WM.Color? white) => new()
    {
        Id = "HYDRO_" + command.Trim().Replace(' ', '_'),
        Text = text,
        ShowText = true,
        ShowImage = true,
        Size = RibbonItemSize.Large,
        Orientation = System.Windows.Controls.Orientation.Vertical,
        CommandParameter = command,
        CommandHandler = handler,
        Image = HydroIcons.Bitmap(icon, 16, white),
        LargeImage = HydroIcons.Bitmap(icon, 32, white),
        ToolTip = new RibbonToolTip { Title = text, Content = description, Command = command.Trim() },
    };

    /// <summary>Tema escuro do AutoCAD (COLORTHEME = 0).</summary>
    private static bool IsDark()
    {
        try
        {
            return Convert.ToInt32(AcApp.GetSystemVariable("COLORTHEME"), CultureInfo.InvariantCulture) == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void Hook()
    {
        if (!_idleHooked)
        {
            AcApp.Idle += OnIdle;
            _idleHooked = true;
        }
    }

    private static void Unhook()
    {
        if (_idleHooked)
        {
            AcApp.Idle -= OnIdle;
            _idleHooked = false;
        }
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        // o Idle dispara sem parar: com erro, desiste (sem a aba, os comandos continuam valendo)
        if (Guard.Run("Ribbon", Ensure, true))
        {
            Unhook();
        }
    }

    private sealed class Handler : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Guard.Run("Ribbon", () =>
        {
            var command = parameter switch
            {
                RibbonCommandItem item => item.CommandParameter?.ToString(),
                string text => text,
                _ => null,
            };
            var document = AcApp.DocumentManager.MdiActiveDocument;
            if (!string.IsNullOrWhiteSpace(command) && document is not null)
            {
                document.SendStringToExecute("\x03\x03" + command, true, false, true);
            }
        });
    }
}
