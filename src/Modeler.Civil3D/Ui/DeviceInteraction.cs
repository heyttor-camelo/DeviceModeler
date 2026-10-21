using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui.Panel;
using Modeler.Core.Errors;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Modeler.Civil3D.Ui;

/// <summary>Dica ao passar o cursor e menu de contexto dos dispositivos (técnica 12 do estudo E).
/// A dica usa o modelo <c>modelos.dica</c> da definição (ou um resumo padrão) e mostra os avisos; o
/// menu do botão direito traz Editar, Conectar, Atualizar, Informações e Mostrar no painel, e só
/// aparece quando a seleção tem dispositivos. Tudo dispara comandos DM_* com SendStringToExecute.</summary>
internal static class DeviceInteraction
{
    private static readonly HashSet<Document> Attached = new();
    private static ContextMenuExtension? _menu;

    public static void Start()
    {
        foreach (Document document in AcApp.DocumentManager)
        {
            Attach(document);
        }

        AcApp.DocumentManager.DocumentCreated += (_, e) => Attach(e.Document);
        AcApp.DocumentManager.DocumentToBeDestroyed += (_, e) => Attached.Remove(e.Document);
        AddMenu();
    }

    public static void Stop()
    {
        if (_menu is not null)
        {
            try
            {
                AcApp.RemoveObjectContextMenuExtension(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _menu);
            }
            catch (Exception)
            {
                // encerramento do AutoCAD
            }

            _menu = null;
        }
    }

    private static void Attach(Document document)
    {
        if (Attached.Add(document))
        {
            document.Editor.PointMonitor += OnPointMonitor;
        }
    }

    private static void OnPointMonitor(object sender, PointMonitorEventArgs e)
    {
        try
        {
            var document = AcApp.DocumentManager.MdiActiveDocument;
            if (document is null || !string.IsNullOrEmpty(document.CommandInProgress) || (e.Context.History & PointHistoryBits.FromKeyboard) != 0)
            {
                return;
            }

            var paths = e.Context.GetPickedEntities();
            if (paths is null || paths.Length == 0)
            {
                return;
            }

            var ids = paths[0].GetObjectIds();
            if (ids.Length == 0)
            {
                return;
            }

            using var tr = document.Database.TransactionManager.StartOpenCloseTransaction();
            if (tr.GetObject(ids[0], OpenMode.ForRead) is BlockReference reference)
            {
                var text = Tooltip(tr, reference);
                if (text is not null)
                {
                    e.AppendToolTipText(text);
                }
            }

            tr.Commit();
        }
        catch (Exception ex)
        {
            HydroLog.Warn("Dica", ex.Message);
        }
    }

    /// <summary>Texto da dica: o modelo da definição (com <c>nome</c> e <c>numero</c> da instância) ou
    /// nome, dispositivo e código; depois os avisos.</summary>
    private static string? Tooltip(Transaction tr, BlockReference reference)
    {
        var entry = InstanceCache.Get(tr, reference, out var error);
        if (entry is null)
        {
            return DeviceRecord.TryRead(reference, tr) is { } record ? $"{record.Name} ({record.DefinitionId})\nERRO: {error}" : null;
        }

        var sb = new StringBuilder();
        var extra = new Dictionary<string, object> { ["nome"] = entry.Record.Name, ["numero"] = (double)entry.Record.Number };
        try
        {
            sb.Append(entry.Definition.Templates.Tooltip is { } template
                ? entry.Record.Name + "\n" + entry.Instance.Render(template, extra)
                : $"{entry.Record.Name}: {entry.Definition.Name ?? entry.Definition.Id}{(entry.Instance.Code is { } code ? $"\nCódigo: {code}" : "")}");
        }
        catch (ModelerException ex)
        {
            sb.Append($"{entry.Record.Name}\n(dica com erro: {ex.Reason})");
        }

        if (entry.Record.DefinitionHash != entry.Definition.Hash)
        {
            sb.Append("\nATENÇÃO: definição alterada (DM_ATUALIZAR)");
        }

        foreach (var warning in entry.Instance.Warnings)
        {
            sb.Append("\nAVISO: ").Append(warning);
        }

        return sb.ToString();
    }

    private static void AddMenu()
    {
        try
        {
            _menu = new ContextMenuExtension { Title = "HYDRO" };
            var items = new (string Text, string Command)[]
            {
                ("Editar parâmetros", "DM_EDITAR "),
                ("Conectar tubos", "DM_CONECTAR "),
                ("Atualizar", "DM_ATUALIZAR "),
                ("Informações", "DM_INFO "),
                ("Mostrar no painel", ""),
            };
            foreach (var (text, command) in items)
            {
                var item = new MenuItem(text);
                var send = command;
                item.Click += (_, _) => Run(send);
                _menu.MenuItems.Add(item);
            }

            _menu.Popup += (_, _) =>
            {
                var hasDevice = SelectedDevices().Count > 0;
                foreach (MenuItem item in _menu.MenuItems)
                {
                    item.Visible = hasDevice;
                }
            };
            AcApp.AddObjectContextMenuExtension(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _menu);
        }
        catch (Exception ex)
        {
            HydroLog.Error("Menu", "não foi possível acrescentar o menu de contexto", ex);
        }
    }

    private static void Run(string command)
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        if (command.Length > 0)
        {
            // a seleção atual continua implícita para o comando (UsePickSet)
            document.SendStringToExecute(command, true, false, false);
            return;
        }

        var devices = SelectedDevices();
        HydroPanel.ShowPanel("DISPOSITIVOS", devices.FirstOrDefault());
    }

    private static List<string> SelectedDevices()
    {
        var result = new List<string>();
        var document = AcApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return result;
        }

        var selection = document.Editor.SelectImplied();
        if (selection.Status != PromptStatus.OK)
        {
            return result;
        }

        using var tr = document.Database.TransactionManager.StartOpenCloseTransaction();
        foreach (var id in selection.Value.GetObjectIds())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference reference && DeviceRecord.IsDevice(reference, tr))
            {
                result.Add(reference.Handle.ToString());
            }
        }

        tr.Commit();
        return result;
    }
}
