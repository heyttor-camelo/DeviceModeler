using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Results;
using CorePlacement = Modeler.Core.Geometry.Placement;

namespace Modeler.Civil3D.Devices;

/// <summary>Cria e regenera instâncias no desenho.
/// Hospedeiro: um BlockReference de um bloco anônimo exclusivo (um Solid3d por sólido de saída,
/// em camadas DM-&lt;NOME&gt;), com o <see cref="DeviceRecord"/> no dicionário de extensão.
/// Mover/girar a referência mantém o sistema local; a edição reconstrói o conteúdo do bloco.</summary>
internal static class DeviceService
{
    public static CorePlacement PlacementOf(BlockReference reference) =>
        new(Solid3dBuilder.ToVec3(reference.Position), reference.Rotation * 180 / Math.PI);

    /// <summary>Cria um bloco anônimo com os sólidos da instância e devolve o id do bloco.</summary>
    public static ObjectId CreateBlock(Transaction tr, Database db, DeviceInstance instance)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var block = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
        var id = table.Add(block);
        tr.AddNewlyCreatedDBObject(block, true);
        FillBlock(tr, db, block, instance);
        return id;
    }

    /// <summary>Insere a referência (já com o registro) no espaço atual.</summary>
    public static BlockReference InsertReference(Transaction tr, Database db, ObjectId blockId, Point3d position, double rotation, DeviceRecord record)
    {
        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        var reference = new BlockReference(new Point3d(position.X, position.Y, 0), blockId) { Rotation = rotation };
        reference.SetDatabaseDefaults(db);
        space.AppendEntity(reference);
        tr.AddNewlyCreatedDBObject(reference, true);
        record.Write(reference, tr);
        return reference;
    }

    /// <summary>Reavalia a instância e, se for preciso, reconstrói a geometria (técnica 2 do estudo E:
    /// regeneração pelo impacto do parâmetro). Corrige deslocamento vertical (soma às elevações),
    /// escala e normal; se o bloco for compartilhado por cópias, a referência ganha um bloco próprio.
    /// Se só mudaram parâmetros que não chegam aos sólidos (folga de escavação, lastro...), o bloco
    /// fica como está e só o registro é regravado.</summary>
    /// <returns>Mensagens sobre ajustes feitos (valores descartados, avisos da definição).</returns>
    public static List<string> Regenerate(Transaction tr, BlockReference reference, DeviceDefinition definition, DeviceRecord record,
        IReadOnlyDictionary<string, object>? newValues = null)
    {
        var messages = new List<string>();
        var before = DeviceEditing.Reconcile(definition, record.Values, out _);
        var values = DeviceEditing.Reconcile(definition, newValues ?? record.Values, out var problems);
        messages.AddRange(problems);

        var shifted = Math.Abs(reference.Position.Z) > 1e-9;
        if (shifted)
        {
            values = DeviceEditing.ShiftElevations(definition, values, reference.Position.Z);
            messages.Add($"movido {reference.Position.Z:0.###} m na vertical: elevações ajustadas");
        }

        var instance = definition.Evaluate(values);
        messages.AddRange(instance.Warnings);

        reference.UpgradeOpen();
        if (shifted)
        {
            reference.Position = new Point3d(reference.Position.X, reference.Position.Y, 0);
        }

        var rescaled = reference.ScaleFactors != new Scale3d(1);
        if (rescaled)
        {
            reference.ScaleFactors = new Scale3d(1);
            messages.Add("escala do bloco restaurada para 1 (o dispositivo é dimensionado pelos parâmetros)");
        }

        if (!reference.Normal.IsEqualTo(Vector3d.ZAxis))
        {
            reference.Normal = Vector3d.ZAxis;
        }

        var db = reference.Database;
        var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
        var shared = block.GetBlockReferenceIds(true, false).Count > 1;
        var rebuild = shared || shifted || record.DefinitionHash != definition.Hash || definition.ChangesGeometry(before, instance.Parameters);
        if (shared)
        {
            // cópia (COPY/ARRAY) compartilhando o bloco anônimo: separa antes de editar
            reference.BlockTableRecord = CreateBlock(tr, db, instance);
        }
        else if (rebuild)
        {
            block.UpgradeOpen();
            foreach (ObjectId id in block)
            {
                tr.GetObject(id, OpenMode.ForWrite).Erase();
            }

            FillBlock(tr, db, block, instance);
        }

        record.DefinitionId = definition.Id;
        record.DefinitionVersion = definition.Version;
        record.DefinitionHash = definition.Hash;
        record.SourceFile = definition.SourceFile ?? record.SourceFile;
        record.Values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
        record.Write(reference, tr);
        reference.RecordGraphicsModified(true);
        if (!rebuild)
        {
            HydroLog.Info("Regeneração", $"{record.Name}: só parâmetros sem efeito na geometria mudaram; bloco mantido");
        }

        return messages;
    }

    /// <summary>Dá a uma cópia um bloco anônimo próprio (com a geometria da instância) e regrava o
    /// registro (id e nome novos já definidos pelo chamador).</summary>
    public static void SeparateBlock(Transaction tr, BlockReference reference, DeviceDefinition definition, DeviceRecord record)
    {
        reference.UpgradeOpen();
        var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
        if (block.GetBlockReferenceIds(true, false).Count > 1)
        {
            var values = DeviceEditing.Reconcile(definition, record.Values, out _);
            reference.BlockTableRecord = CreateBlock(tr, reference.Database, definition.Evaluate(values));
        }

        record.Write(reference, tr);
    }

    /// <summary>Lê o registro e avalia a instância (sem alterar o desenho).</summary>
    public static (DeviceRecord Record, DeviceDefinition Definition, DeviceInstance Instance) Load(Transaction tr, BlockReference reference)
    {
        var record = DeviceRecord.TryRead(reference, tr) ?? throw new InvalidOperationException("a entidade não é um dispositivo do DeviceModeler");
        var definition = DeviceCatalog.Find(record.DefinitionId, record.SourceFile)
                         ?? throw new InvalidOperationException($"definição '{record.DefinitionId}' não encontrada no catálogo ({DeviceCatalog.Folder})");
        var values = DeviceEditing.Reconcile(definition, record.Values, out _);
        return (record, definition, definition.Evaluate(values));
    }

    /// <summary>Todas as referências de dispositivo do espaço modelo.</summary>
    public static List<BlockReference> AllDevices(Transaction tr, Database db)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var result = new List<BlockReference>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference))) &&
                tr.GetObject(id, OpenMode.ForRead) is BlockReference reference && DeviceRecord.IsDevice(reference, tr))
            {
                result.Add(reference);
            }
        }

        return result;
    }

    /// <summary>Próximo nome livre: pelo modelo <c>modelos.nome</c> da definição (com <c>numero</c> =
    /// 1, 2, ...) ou, sem modelo, prefixo em maiúsculas + número (PV-01, PV-02...).</summary>
    public static string NextName(Transaction tr, Database db, DeviceDefinition definition, ISet<string>? taken = null)
    {
        var used = taken ?? new HashSet<string>(AllDevices(tr, db).Select(r => DeviceRecord.TryRead(r, tr)!.Name), StringComparer.OrdinalIgnoreCase);
        return NextName(definition, used);
    }

    public static string NextName(DeviceDefinition definition, ISet<string> used)
    {
        var prefix = definition.Id.Split('_')[0].ToUpperInvariant();
        DeviceInstance? sample = null;
        if (definition.Templates.Name is not null)
        {
            try
            {
                sample = definition.Evaluate();
            }
            catch (Modeler.Core.Errors.ModelerException)
            {
                // os valores padrão não avaliam: fica o prefixo
            }
        }

        string? previous = null;
        for (var i = 1; ; i++)
        {
            var name = $"{prefix}-{i:00}";
            if (sample is not null)
            {
                try
                {
                    var rendered = sample.Render(definition.Templates.Name!, new Dictionary<string, object> { ["numero"] = (double)i });
                    // modelo sem {numero}: o número vai no fim
                    name = rendered == previous || (i == 1 && !definition.Templates.Name!.Text.Contains("numero", StringComparison.Ordinal))
                        ? $"{rendered}-{i}"
                        : rendered;
                    previous = rendered;
                }
                catch (Modeler.Core.Errors.ModelerException)
                {
                    sample = null;
                }
            }

            if (!used.Contains(name))
            {
                return name;
            }
        }
    }

    private static void FillBlock(Transaction tr, Database db, BlockTableRecord block, DeviceInstance instance)
    {
        foreach (var output in instance.Solids)
        {
            var solid = Solid3dBuilder.Build(output.Node);
            if (solid is null)
            {
                continue;
            }

            solid.SetDatabaseDefaults(db);
            solid.LayerId = EnsureLayer(tr, db, LayerName(output.Name), ColorFor(output));
            solid.ColorIndex = 256; // ByLayer
            var material = FindMaterial(tr, db, output.Material);
            if (!material.IsNull)
            {
                solid.MaterialId = material;
            }

            block.AppendEntity(solid);
            tr.AddNewlyCreatedDBObject(solid, true);
        }
    }

    internal static string LayerName(string solidName)
    {
        var sb = new StringBuilder("DM-");
        foreach (var ch in solidName.ToUpperInvariant())
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        }

        return sb.ToString();
    }

    private static short ColorFor(NamedSolid solid)
    {
        var key = (solid.Material ?? solid.Name).ToLowerInvariant();
        if (key.Contains("concreto"))
        {
            return 8;
        }

        if (key.Contains("aço") || key.Contains("aco") || key.Contains("ferro"))
        {
            return 1;
        }

        return key.Contains("alvenaria") || key.Contains("tijolo") ? (short)30 : (short)7;
    }

    private static ObjectId EnsureLayer(Transaction tr, Database db, string name, short color)
    {
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
        {
            return layers[name];
        }

        layers.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
        var id = layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }

    private static ObjectId FindMaterial(Transaction tr, Database db, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || db.MaterialDictionaryId.IsNull)
        {
            return ObjectId.Null;
        }

        var materials = (DBDictionary)tr.GetObject(db.MaterialDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in materials)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return ObjectId.Null;
    }
}
