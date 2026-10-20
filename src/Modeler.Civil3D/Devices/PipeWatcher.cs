using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Modeler.Civil3D.Devices;

/// <summary>Faz os dispositivos acompanharem os tubos ligados: <c>ObjectModified</c> só enfileira o
/// handle do tubo (nada pesado dentro do evento); no fim do comando os dispositivos que referenciam
/// esses tubos são relidos e regenerados. Ignora UNDO/REDO e os próprios comandos DM_*.</summary>
internal static class PipeWatcher
{
    private static readonly HashSet<string> SkipCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "U", "UNDO", "REDO", "MREDO", "OOPS", "SAVE", "QSAVE", "SAVEAS", "CLOSE", "AUDIT", "RECOVER", "BEDIT", "REFEDIT",
    };

    private static readonly Dictionary<Database, HashSet<string>> Queue = new();
    private static bool _busy;

    public static bool Enabled { get; set; } = true;

    public static void Start()
    {
        foreach (Document document in AcApp.DocumentManager)
        {
            Attach(document);
        }

        AcApp.DocumentManager.DocumentCreated += (_, e) => Attach(e.Document);
        AcApp.DocumentManager.DocumentToBeDestroyed += (_, e) => Queue.Remove(e.Document.Database);
    }

    private static void Attach(Document document)
    {
        document.Database.ObjectModified += OnObjectModified;
        document.CommandEnded += OnCommandEnded;
        document.CommandCancelled += (_, _) => Queue.Remove(document.Database);
    }

    private static void OnObjectModified(object sender, ObjectEventArgs e)
    {
        if (!Enabled || _busy || e.DBObject is not Pipe pipe || pipe.IsUndoing)
        {
            return;
        }

        var db = (Database)sender;
        if (!Queue.TryGetValue(db, out var handles))
        {
            Queue[db] = handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        handles.Add(pipe.Handle.ToString());
    }

    private static void OnCommandEnded(object sender, CommandEventArgs e)
    {
        var document = (Document)sender;
        if (!Queue.Remove(document.Database, out var handles) || handles.Count == 0 || _busy || !Enabled)
        {
            return;
        }

        var command = e.GlobalCommandName.TrimStart('_', '.');
        if (SkipCommands.Contains(command) || command.StartsWith("DM_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _busy = true;
        try
        {
            using var _ = document.LockDocument();
            using var tr = document.Database.TransactionManager.StartTransaction();
            foreach (var reference in DeviceService.AllDevices(tr, document.Database))
            {
                Refresh(tr, document, reference, handles);
            }

            tr.Commit();
        }
        catch (Exception ex)
        {
            document.Editor.WriteMessage($"\nDeviceModeler: falha ao atualizar dispositivos ligados: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private static void Refresh(Transaction tr, Document document, BlockReference reference, HashSet<string> handles)
    {
        var record = DeviceRecord.TryRead(reference, tr);
        if (record is null)
        {
            return;
        }

        var (_, definition, instance) = DeviceService.Load(tr, reference);
        var parameter = definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes);
        if (parameter is null || instance.Parameters[parameter.Name] is not IReadOnlyList<object> connections ||
            !connections.OfType<IReadOnlyDictionary<string, object>>().Any(c => c.GetValueOrDefault("ref") is string h && handles.Contains(h)))
        {
            return;
        }

        var messages = new List<string>();
        var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
        var refreshed = CivilLinks.RefreshConnections(tr, document.Database, connections, DeviceService.PlacementOf(reference), messages);
        CivilLinks.ApplyConnections(definition, values, refreshed, messages);
        DeviceService.Regenerate(tr, reference, definition, record, values);
        document.Editor.WriteMessage($"\n{record.Name}: atualizado pelos tubos ligados.");
    }
}
