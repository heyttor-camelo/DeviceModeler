using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Hydro.Civil3D.Catalog;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Ui;
using Hydro.Civil3D.Ui.Panel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Hydro.Civil3D.Plugin))]

namespace Hydro.Civil3D;

/// <summary>Ponto de entrada: log, overrule de grips, acompanhamento dos tubos, Ribbon e painel.
/// As dependências (Core, YamlDotNet) já foram registradas pelo <see cref="EmbeddedAssemblies"/>.</summary>
public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        HydroLog.Start();
        DeviceGripOverrule.Register();
        DeviceWatcher.Start();
        DeviceObjectOverrule.Register();
        ConnectorSnap.Register();
        DeviceInteraction.Start();
        Drawing.OutputsUpdater.Start();
        Drawing.ProjectionGripOverrule.Register();
        HydroRibbon.Initialize();
        foreach (Document document in AcApp.DocumentManager)
        {
            WatchCommands(document);
        }

        AcApp.DocumentManager.DocumentCreated += (_, e) => WatchCommands(e.Document);
        var editor = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null)
        {
            return;
        }

        var version = typeof(Plugin).Assembly.GetName().Version;
        editor.WriteMessage($"\nHYDRO {version} carregado. Catálogo: {DeviceCatalog.Folder}");
        editor.WriteMessage("\nPainel: C3DHYDRO. Comandos: " + string.Join(", ", Commands.All.Select(c => c.Name)));
    }

    public void Terminate()
    {
        DeviceGripOverrule.Unregister();
        DeviceObjectOverrule.Unregister();
        DeviceInteraction.Stop();
        Drawing.ProjectionGripOverrule.Unregister();
        HydroRibbon.Terminate();
        HydroLog.Stop();
    }

    /// <summary>O painel aberto se atualiza ao fim de cada comando (o pedido é agrupado e barato
    /// quando nada mudou: as instâncias ficam em cache).</summary>
    private static void WatchCommands(Document document)
    {
        document.CommandEnded += (_, _) => HydroPanel.NotifyChanged();
    }
}
