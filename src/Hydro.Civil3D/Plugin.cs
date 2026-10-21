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
        // cada parte se registra sozinha: uma falha numa não impede as outras nem derruba a carga
        Guard.Run("Carga: janelas", Guard.WatchDispatcher);
        Guard.Run("Carga: grips", DeviceGripOverrule.Register);
        Guard.Run("Carga: acompanhamento", DeviceWatcher.Start);
        Guard.Run("Carga: cópias", DeviceObjectOverrule.Register);
        Guard.Run("Carga: osnap", ConnectorSnap.Register);
        Guard.Run("Carga: dica e menu", DeviceInteraction.Start);
        Guard.Run("Carga: saídas", Drawing.OutputsUpdater.Start);
        Guard.Run("Carga: grips de perfil", Drawing.ProjectionGripOverrule.Register);
        Guard.Run("Carga: Ribbon", HydroRibbon.Initialize);
        foreach (Document document in AcApp.DocumentManager)
        {
            WatchCommands(document);
        }

        AcApp.DocumentManager.DocumentCreated += (_, e) => Guard.Run("Painel", () => WatchCommands(e.Document));
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
        Guard.Run("Encerramento", DeviceGripOverrule.Unregister);
        Guard.Run("Encerramento", DeviceObjectOverrule.Unregister);
        Guard.Run("Encerramento", DeviceInteraction.Stop);
        Guard.Run("Encerramento", Drawing.ProjectionGripOverrule.Unregister);
        Guard.Run("Encerramento", HydroRibbon.Terminate);
        HydroLog.Stop();
    }

    /// <summary>O painel aberto se atualiza ao fim de cada comando (o pedido é agrupado e barato
    /// quando nada mudou: as instâncias ficam em cache).</summary>
    private static void WatchCommands(Document document)
    {
        document.CommandEnded += (_, _) => Guard.Run("Painel", HydroPanel.NotifyChanged);
    }
}
