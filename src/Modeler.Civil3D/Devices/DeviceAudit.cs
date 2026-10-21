using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;

namespace Modeler.Civil3D.Devices;

internal enum AuditLevel
{
    Info,
    Aviso,
    Erro,
}

/// <summary>Problema achado pela auditoria. <see cref="Fix"/> diz o que <c>DM_AUDITAR Corrigir</c> faz.</summary>
internal sealed record AuditIssue(string Handle, string Device, AuditLevel Level, string Kind, string Message, string? Fix);

/// <summary>Auditoria dos dispositivos do desenho (técnica 6 do estudo E, como o SAUDIT do SOLIDOS):
/// registro em esquema antigo, YAML ausente ou alterado, instância que não avalia, avisos das
/// validações, bloco compartilhado, nome ou id repetido, conexão para tubo apagado e superfície seguida
/// que não existe mais. <see cref="Repair"/> corrige o que tem correção automática.</summary>
internal static class DeviceAudit
{
    public static List<AuditIssue> Run(Transaction tr, Database db)
    {
        var issues = new List<AuditIssue>();
        var devices = DeviceService.AllDevices(tr, db);
        var records = devices.Select(r => (Reference: r, Record: DeviceRecord.TryRead(r, tr)!)).ToList();
        var names = records.GroupBy(r => r.Record.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).Select(r => r.Reference.Handle.ToString()).ToHashSet();
        var ids = records.GroupBy(r => r.Record.InstanceId).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).Select(r => r.Reference.Handle.ToString()).ToHashSet();
        foreach (var (reference, record) in records)
        {
            var handle = reference.Handle.ToString();
            void Add(AuditLevel level, string kind, string message, string? fix = null) => issues.Add(new AuditIssue(handle, record.Name, level, kind, message, fix));

            if (record.Schema < DeviceRecord.CurrentSchema)
            {
                Add(AuditLevel.Info, "esquema", $"registro no esquema {record.Schema} (atual {DeviceRecord.CurrentSchema})", "migrar o registro");
            }

            if (names.Contains(handle))
            {
                Add(AuditLevel.Aviso, "nome", $"nome '{record.Name}' repetido", "dar um nome livre");
            }

            if (ids.Contains(handle))
            {
                Add(AuditLevel.Aviso, "id", "id de instância repetido (cópia antiga)", "gerar id novo");
            }

            var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
            if (block.GetBlockReferenceIds(true, false).Count > 1)
            {
                Add(AuditLevel.Aviso, "bloco", "bloco anônimo compartilhado com outra referência (cópia antiga)", "separar o bloco");
            }

            var definition = DeviceCatalog.Find(record.DefinitionId, record.SourceFile);
            if (definition is null)
            {
                Add(AuditLevel.Erro, "definição", $"definição '{record.DefinitionId}' não está no catálogo ({DeviceCatalog.Folder})");
                continue;
            }

            if (record.DefinitionHash != definition.Hash)
            {
                Add(AuditLevel.Aviso, "definição", $"definição alterada desde a geração ({record.DefinitionVersion} → {definition.Version})", "regenerar com a definição atual");
            }

            try
            {
                var values = DeviceEditing.Reconcile(definition, record.Values, out var problems);
                foreach (var problem in problems)
                {
                    Add(AuditLevel.Aviso, "parâmetro", problem, "regenerar com a definição atual");
                }

                var instance = definition.Evaluate(values);
                foreach (var warning in instance.Warnings)
                {
                    Add(AuditLevel.Aviso, "validação", warning);
                }

                var conexoes = definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes);
                if (conexoes is not null && instance.Parameters[conexoes.Name] is IReadOnlyList<object> connections)
                {
                    foreach (var c in connections.OfType<IReadOnlyDictionary<string, object>>())
                    {
                        if (c.GetValueOrDefault("ref") is string pipe && pipe.Length > 0 && !CivilLinks.TryGet<Pipe>(tr, db, pipe, out _))
                        {
                            Add(AuditLevel.Aviso, "conexão", $"conexão '{c.GetValueOrDefault("nome")}' aponta para o tubo {pipe}, que não existe mais", "remover a conexão e regenerar");
                        }
                    }
                }
            }
            catch (ModelerException ex)
            {
                Add(AuditLevel.Erro, "avaliação", ex.Message);
            }

            if (record.FollowSurface && !CivilLinks.TryGetSurface(tr, db, record.SurfaceHandle, out _))
            {
                Add(AuditLevel.Aviso, "superfície", $"a superfície seguida ({record.SurfaceHandle}) não existe mais", "deixar de seguir");
            }
        }

        if (DrawingVersion.Read(db, tr) is { } stamp && stamp.Schema > DeviceRecord.CurrentSchema)
        {
            issues.Add(new AuditIssue("", "(desenho)", AuditLevel.Aviso, "versão",
                $"desenho gravado por um HYDRO mais novo ({stamp.Version}, esquema {stamp.Schema})", null));
        }

        return issues;
    }

    /// <summary>Corrige o que tem correção automática; devolve as mensagens do que foi feito.</summary>
    public static List<string> Repair(Transaction tr, Database db, IReadOnlyList<AuditIssue> issues)
    {
        var messages = new List<string>();
        var names = new HashSet<string>(DeviceService.AllDevices(tr, db).Select(r => DeviceRecord.TryRead(r, tr)!.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var group in issues.Where(i => i.Fix is not null && i.Handle.Length > 0).GroupBy(i => i.Handle))
        {
            if (!CivilLinks.TryGet<BlockReference>(tr, db, group.Key, out var reference) || DeviceRecord.TryRead(reference, tr) is not { } record)
            {
                continue;
            }

            try
            {
                var kinds = group.Select(i => i.Kind).ToHashSet();
                var definition = DeviceCatalog.Find(record.DefinitionId, record.SourceFile);
                if (kinds.Contains("nome"))
                {
                    var old = record.Name;
                    record.Name = definition is null ? $"{old}-{Guid.NewGuid().ToString("N")[..4]}" : DeviceService.NextName(definition, names);
                    names.Add(record.Name);
                    messages.Add($"{old}: renomeado para {record.Name}");
                }

                if (kinds.Contains("id"))
                {
                    record.InstanceId = Guid.NewGuid().ToString("N");
                }

                if (kinds.Contains("superfície"))
                {
                    record.FollowSurface = false;
                    messages.Add($"{record.Name}: deixou de seguir a superfície apagada");
                }

                if (definition is null)
                {
                    reference.UpgradeOpen();
                    record.Write(reference, tr);
                    continue;
                }

                var values = DeviceEditing.Reconcile(definition, record.Values, out _);
                if (kinds.Contains("conexão") && definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is { } conexoes &&
                    values.GetValueOrDefault(conexoes.Name) is IReadOnlyList<object> connections)
                {
                    var kept = connections.Where(c => c is not IReadOnlyDictionary<string, object> r || r.GetValueOrDefault("ref") is not string h ||
                                                      h.Length == 0 || CivilLinks.TryGet<Pipe>(tr, db, h, out _)).ToList();
                    CivilLinks.ApplyConnections(definition, values, kept, messages);
                    messages.Add($"{record.Name}: {connections.Count - kept.Count} conexão(ões) para tubo apagado removida(s)");
                }

                // regenerar cobre esquema, bloco compartilhado, definição alterada e parâmetros descartados
                messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values).Select(m => $"{record.Name}: {m}"));
                messages.Add($"{record.Name}: corrigido ({string.Join(", ", kinds)})");
            }
            catch (Exception ex) when (ex is ModelerException or InvalidOperationException)
            {
                messages.Add($"{record.Name}: não corrigido: {ex.Message}");
            }
        }

        return messages;
    }
}
