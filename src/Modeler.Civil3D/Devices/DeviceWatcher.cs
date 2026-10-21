using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Modeler.Civil3D.Devices;

/// <summary>Fila de trabalho por desenho, processada em fases no fim de cada comando (técnica 2 do
/// estudo E, como o SOLIDOS): os eventos e os overrules só enfileiram — nada pesado dentro deles.
/// <list type="number">
/// <item>edições por grip (valor final do arrasto);</item>
/// <item>cópias (COPY/ARRAY/MIRROR): bloco próprio, id e nome novos;</item>
/// <item>tubos apagados: a conexão sai do dispositivo;</item>
/// <item>tubos alterados: furos e elevação de fundo acompanham (DM_AUTO);</item>
/// <item>superfícies alteradas: a elevação acompanha pela impressão digital (ComputeFingerPrint);</item>
/// <item>dispositivos apagados: avisa quem depende deles (rótulos, projeções).</item>
/// </list>
/// UNDO/REDO e afins são ignorados (o UNDO já desfaz tudo).</summary>
internal static class DeviceWatcher
{
    private static readonly HashSet<string> SkipCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "U", "UNDO", "REDO", "MREDO", "OOPS", "SAVE", "QSAVE", "SAVEAS", "CLOSE", "AUDIT", "RECOVER", "BEDIT", "REFEDIT",
    };

    private static readonly Dictionary<Database, Pending> Queues = new();
    private static readonly HashSet<Document> Attached = new();
    private static bool _busy;

    /// <summary>Atualização automática pelos tubos (DM_AUTO).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Chamado depois que dispositivos foram refeitos ou apagados (rótulos e projeções se
    /// atualizam por aqui).</summary>
    public static event Action<Transaction, Database, OutputChanges>? DevicesChanged;

    public static void Start()
    {
        foreach (Document document in AcApp.DocumentManager)
        {
            Attach(document);
        }

        AcApp.DocumentManager.DocumentCreated += (_, e) => Attach(e.Document);
        AcApp.DocumentManager.DocumentToBeDestroyed += (_, e) =>
        {
            Queues.Remove(e.Document.Database);
            InstanceCache.Forget(e.Document.Database);
            Attached.Remove(e.Document);
        };
    }

    public static void QueueGrip(ObjectId entity, string parameter, double value)
    {
        if (!entity.IsNull)
        {
            For(entity.Database).Grips[entity] = (parameter, value);
        }
    }

    public static void QueueClone(DBObject clone)
    {
        if (!_busy && clone.Database is not null && !clone.ObjectId.IsNull)
        {
            For(clone.Database).Clones.Add(clone.ObjectId);
        }
    }

    public static void QueueErasedDevice(DBObject device)
    {
        if (!_busy && device.Database is not null)
        {
            For(device.Database).ErasedDevices.Add(device.Handle.ToString());
        }
    }

    private static Pending For(Database db)
    {
        if (!Queues.TryGetValue(db, out var pending))
        {
            Queues[db] = pending = new Pending();
        }

        return pending;
    }

    private static void Attach(Document document)
    {
        if (!Attached.Add(document))
        {
            return;
        }

        document.Database.ObjectModified += OnObjectModified;
        document.Database.ObjectErased += OnObjectErased;
        document.CommandEnded += OnCommandEnded;
        document.CommandCancelled += (_, _) => Clear(document.Database, keepGrips: false);
        document.CommandFailed += (_, _) => Clear(document.Database, keepGrips: false);
    }

    private static void Clear(Database db, bool keepGrips)
    {
        if (Queues.TryGetValue(db, out var pending))
        {
            var grips = pending.Grips;
            Queues.Remove(db);
            if (keepGrips && grips.Count > 0)
            {
                For(db).Grips = grips;
            }
        }
    }

    private static void OnObjectModified(object sender, ObjectEventArgs e)
    {
        if (_busy || e.DBObject.IsUndoing)
        {
            return;
        }

        var db = (Database)sender;
        switch (e.DBObject)
        {
            case Pipe pipe when Enabled:
                For(db).Pipes.Add(pipe.Handle.ToString());
                break;
            case CivilSurface surface:
                For(db).Surfaces.Add(surface.Handle.ToString());
                break;
            case ProfileView view:
                For(db).Views.Add(view.Handle.ToString());
                break;
            case Alignment alignment:
                For(db).Alignments.Add(alignment.Handle.ToString());
                break;
            case Profile profile when !profile.AlignmentId.IsNull:
                For(db).Alignments.Add(profile.AlignmentId.Handle.ToString());
                break;
            case BlockReference reference when !reference.ExtensionDictionary.IsNull:
                // pode ser dispositivo: confere no fim do comando (aqui só enfileira)
                For(db).Devices.Add(reference.ObjectId);
                break;
        }
    }

    private static void OnObjectErased(object sender, ObjectErasedEventArgs e)
    {
        if (!_busy && e.Erased && !e.DBObject.IsUndoing && e.DBObject is Pipe pipe)
        {
            For((Database)sender).ErasedPipes.Add(pipe.Handle.ToString());
        }
    }

    private static void OnCommandEnded(object sender, CommandEventArgs e)
    {
        var document = (Document)sender;
        if (_busy || !Queues.Remove(document.Database, out var pending) || pending.IsEmpty)
        {
            return;
        }

        var command = e.GlobalCommandName.TrimStart('_', '.');
        if (SkipCommands.Contains(command))
        {
            return;
        }

        // os comandos DM_* já cuidam de tubos e superfícies; grips e cópias valem sempre
        if (command.StartsWith("DM_", StringComparison.OrdinalIgnoreCase))
        {
            pending.Pipes.Clear();
            pending.ErasedPipes.Clear();
            pending.Surfaces.Clear();
        }

        _busy = true;
        try
        {
            using var _ = document.LockDocument();
            using var tr = document.Database.TransactionManager.StartTransaction();
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process(tr, document, pending, changed);
            DevicesChanged?.Invoke(tr, document.Database, new OutputChanges(changed, pending.ErasedDevices, pending.Views, pending.Alignments));
            tr.Commit();
        }
        catch (Exception ex)
        {
            HydroLog.Error("Acompanhamento", "falha ao atualizar dispositivos no fim do comando", ex);
            document.Editor.WriteMessage($"\nHYDRO: falha ao atualizar dispositivos: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }

        Ui.Panel.HydroPanel.NotifyChanged();
    }

    private static void Process(Transaction tr, Document document, Pending pending, HashSet<string> changed)
    {
        var db = document.Database;
        var editor = document.Editor;
        void Say(string name, IEnumerable<string> messages)
        {
            foreach (var message in messages)
            {
                editor.WriteMessage($"\n{name}: {message}");
            }
        }

        // 1. grips
        foreach (var (id, (parameter, value)) in pending.Grips)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference)
            {
                continue;
            }

            Guard(editor, reference, () =>
            {
                var (record, definition, instance) = DeviceService.Load(tr, reference);
                var values = DeviceEditing.Edit(definition, instance.Parameters, parameter, value, out var messages);
                messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
                Say(record.Name, messages.Prepend($"{parameter} = {value.ToString("0.###", CultureInfo.InvariantCulture)}"));
                changed.Add(reference.Handle.ToString());
            });
        }

        // 2. cópias
        if (pending.Clones.Count > 0)
        {
            var names = new HashSet<string>(DeviceService.AllDevices(tr, db).Select(r => DeviceRecord.TryRead(r, tr)!.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var id in pending.Clones)
            {
                if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference || DeviceRecord.TryRead(reference, tr) is not { } record)
                {
                    continue;
                }

                Guard(editor, reference, () =>
                {
                    var (_, definition, _) = DeviceService.Load(tr, reference);
                    var original = record.Name;
                    record.InstanceId = Guid.NewGuid().ToString("N");
                    record.Number = 0;
                    record.Name = DeviceService.NextName(tr, db, definition, names);
                    names.Add(record.Name);
                    DeviceService.SeparateBlock(tr, reference, definition, record);
                    HydroLog.Info("Acompanhamento", $"cópia de {original} virou {record.Name} (bloco próprio)");
                    changed.Add(reference.Handle.ToString());
                });
            }
        }

        // 2b. dispositivos alterados (MOVE, ROTATE, edição): a estrutura nula acompanha e as pontas
        // soltas dos tubos vão para o conector; depois os furos se refazem pelos tubos
        foreach (var id in pending.Devices.Where(i => !i.IsErased && !pending.Grips.ContainsKey(i)))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference || !DeviceRecord.IsDevice(reference, tr))
            {
                continue;
            }

            Guard(editor, reference, () =>
            {
                var entry = InstanceCache.Get(tr, reference);
                if (entry is null)
                {
                    return;
                }

                // rótulos, tabelas e projeções dele se refazem (a posição ou os parâmetros podem ter mudado)
                changed.Add(reference.Handle.ToString());
                NetworkSync.SyncStructure(tr, db, reference, entry);
                var messages = new List<string>();
                if (!Enabled || !NetworkSync.AdjustEnds(tr, db, reference, entry, messages))
                {
                    return;
                }

                var values = new Dictionary<string, object>(entry.Instance.Parameters, StringComparer.Ordinal);
                var conexoes = entry.Definition.Parameters.First(p => p.Type == ParameterType.Conexoes);
                var connections = CivilLinks.RefreshConnections(tr, db, (IReadOnlyList<object>)values[conexoes.Name], DeviceService.PlacementOf(reference), messages);
                CivilLinks.ApplyConnections(entry.Definition, values, connections, messages);
                messages.AddRange(DeviceService.Regenerate(tr, reference, entry.Definition, entry.Record, values));
                Say(entry.Record.Name, messages);
                changed.Add(reference.Handle.ToString());
            });
        }

        if (pending.ErasedPipes.Count == 0 && pending.Pipes.Count == 0 && pending.Surfaces.Count == 0)
        {
            return;
        }

        foreach (var reference in DeviceService.AllDevices(tr, db))
        {
            Guard(editor, reference, () =>
            {
                var record = DeviceRecord.TryRead(reference, tr)!;
                var (_, definition, instance) = DeviceService.Load(tr, reference);
                var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
                var messages = new List<string>();
                var refresh = false;
                var conexoes = definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes);
                if (conexoes is not null && values[conexoes.Name] is IReadOnlyList<object> connections && connections.Count > 0)
                {
                    // 3. tubos apagados
                    var kept = connections.Where(c => c is not IReadOnlyDictionary<string, object> r ||
                                                       r.GetValueOrDefault("ref") is not string h || !pending.ErasedPipes.Contains(h)).ToList();
                    if (kept.Count != connections.Count)
                    {
                        messages.Add($"{connections.Count - kept.Count} conexão(ões) removida(s): tubo apagado");
                        connections = kept;
                        refresh = true;
                    }

                    // 4. tubos alterados
                    if (Enabled && connections.OfType<IReadOnlyDictionary<string, object>>().Any(c => c.GetValueOrDefault("ref") is string h && pending.Pipes.Contains(h)))
                    {
                        connections = CivilLinks.RefreshConnections(tr, db, connections, DeviceService.PlacementOf(reference), messages);
                        refresh = true;
                    }

                    if (refresh)
                    {
                        CivilLinks.ApplyConnections(definition, values, connections, messages);
                    }
                }

                // 5. superfície seguida
                if (record.FollowSurface && pending.Surfaces.Contains(record.SurfaceHandle) &&
                    CivilLinks.TryGetSurface(tr, db, record.SurfaceHandle, out var surface))
                {
                    var fingerprint = surface.ComputeFingerPrint().ToString(CultureInfo.InvariantCulture);
                    if (fingerprint != record.SurfaceFingerprint && CivilLinks.ApplySurface(definition, values, surface, DeviceService.PlacementOf(reference), messages))
                    {
                        record.SurfaceFingerprint = fingerprint;
                        messages.Add($"elevação refeita pela superfície {surface.Name}");
                        refresh = true;
                    }
                }

                if (refresh)
                {
                    messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
                    Say(record.Name, messages);
                    changed.Add(reference.Handle.ToString());
                }
            });
        }
    }

    /// <summary>Um dispositivo com problema não impede os outros: a falha vai para o log e para a linha de comando.</summary>
    private static void Guard(Autodesk.AutoCAD.EditorInput.Editor editor, BlockReference reference, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception)
        {
            HydroLog.Warn("Acompanhamento", $"{reference.Handle}: {ex.Message}");
            editor.WriteMessage($"\nHYDRO {reference.Handle}: {ex.Message}");
        }
    }

    private sealed class Pending
    {
        public Dictionary<ObjectId, (string Parameter, double Value)> Grips { get; set; } = new();

        public HashSet<ObjectId> Clones { get; } = new();

        public HashSet<string> Pipes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ErasedPipes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Surfaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ErasedDevices { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Referências de bloco alteradas (MOVE, ROTATE, edição): estrutura e pontas acompanham.</summary>
        public HashSet<ObjectId> Devices { get; } = new();

        /// <summary>Vistas de perfil e alinhamentos (ou perfis) alterados: as projeções se refazem.</summary>
        public HashSet<string> Views { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Alignments { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty => Grips.Count == 0 && Clones.Count == 0 && Pipes.Count == 0 && ErasedPipes.Count == 0 && Surfaces.Count == 0 && ErasedDevices.Count == 0 && Devices.Count == 0 && Views.Count == 0 && Alignments.Count == 0;
    }
}

/// <summary>O que mudou no fim do comando, para as saídas (rótulos, tabelas, projeções): dispositivos
/// refeitos, dispositivos apagados, vistas de perfil e alinhamentos alterados (handles).</summary>
internal sealed record OutputChanges(
    IReadOnlyCollection<string> Changed,
    IReadOnlyCollection<string> Erased,
    IReadOnlyCollection<string> Views,
    IReadOnlyCollection<string> Alignments);
