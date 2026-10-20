using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Devices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Modeler.Civil3D.Plugin))]

namespace Modeler.Civil3D;

/// <summary>Ponto de entrada: registra o overrule de grips e anuncia os comandos.</summary>
public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        DeviceGripOverrule.Register();
        PipeWatcher.Start();
        var editor = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null)
        {
            return;
        }

        var version = typeof(Plugin).Assembly.GetName().Version;
        editor.WriteMessage($"\nDeviceModeler {version} carregado. Catálogo: {DeviceCatalog.Folder}");
        editor.WriteMessage("\nComandos: " + string.Join(", ", Commands.All.Select(c => c.Name)));
    }

    public void Terminate() => DeviceGripOverrule.Unregister();
}
