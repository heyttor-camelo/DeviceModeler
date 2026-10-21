using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using Modeler.Core.Persistence;
using Modeler.Core.Results;

namespace Modeler.Civil3D.Devices;

/// <summary>Instâncias já avaliadas por desenho e handle, com a chave hash da definição + JSON dos
/// parâmetros: painel, dica do cursor, osnap e grips leem daqui sem reavaliar o que não mudou.</summary>
internal static class InstanceCache
{
    private static readonly Dictionary<(Database Db, string Handle), Entry> Entries = new();

    public sealed record Entry(string Key, DeviceRecord Record, DeviceDefinition Definition, DeviceInstance Instance);

    /// <summary>Avalia (ou devolve do cache) o dispositivo. Nulo se não for dispositivo ou a definição
    /// não avaliar (o motivo vai para <paramref name="error"/>).</summary>
    public static Entry? Get(Transaction tr, BlockReference reference, out string? error)
    {
        error = null;
        var record = DeviceRecord.TryRead(reference, tr);
        if (record is null)
        {
            return null;
        }

        try
        {
            var definition = DeviceCatalog.Find(record.DefinitionId, record.SourceFile);
            if (definition is null)
            {
                error = $"definição '{record.DefinitionId}' não encontrada no catálogo";
                return null;
            }

            var key = definition.Hash + "|" + ParameterCodec.Serialize(record.Values);
            var id = (reference.Database, reference.Handle.ToString());
            if (Entries.TryGetValue(id, out var cached) && cached.Key == key)
            {
                return cached with { Record = record };
            }

            var instance = definition.Evaluate(DeviceEditing.Reconcile(definition, record.Values, out _));
            var entry = new Entry(key, record, definition, instance);
            Entries[id] = entry;
            return entry;
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
    }

    public static Entry? Get(Transaction tr, BlockReference reference) => Get(tr, reference, out _);

    public static void Forget(Database db)
    {
        foreach (var key in new List<(Database, string)>(Entries.Keys))
        {
            if (key.Item1 == db)
            {
                Entries.Remove(key);
            }
        }
    }
}
