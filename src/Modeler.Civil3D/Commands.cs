using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui;
using Modeler.Civil3D.Ui.Panel;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using Modeler.Core.Export;
using Modeler.Core.Results;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;
using Exception = System.Exception;

[assembly: CommandClass(typeof(Modeler.Civil3D.Commands))]

namespace Modeler.Civil3D;

/// <summary>Comandos DM_* do DeviceModeler.</summary>
public sealed class Commands
{
    public static readonly (string Name, string Description)[] All =
    {
        ("DM_INSERIR", "insere um dispositivo do catálogo (janela de parâmetros + ponto + rotação)"),
        ("DM_EDITAR", "edita os parâmetros de um dispositivo"),
        ("DM_CONECTAR", "liga tubos de Pipe Network a um dispositivo (furos, conectores e elevação de fundo)"),
        ("DM_SUPERFICIE", "ajusta parâmetros com 'alvo: superficie' (elevação da tampa) a uma superfície"),
        ("DM_ATUALIZAR", "relê tubos e catálogo e regenera dispositivos"),
        ("DM_QUANTITATIVOS", "soma os quantitativos e exporta CSV"),
        ("DM_SWMM", "exporta as junções para um arquivo .inp do SWMM"),
        ("DM_INFO", "mostra parâmetros, variáveis, quantitativos e conectores de um dispositivo"),
        ("DM_CATALOGO", "mostra/troca a pasta do catálogo e lista as definições"),
        ("DM_AUTO", "liga/desliga a atualização automática quando tubos ligados mudam"),
        ("DM_REDE", "lança uma rede em cadeia: dispositivo → tubo da Pipe Network → dispositivo"),
        ("DM_NUMERAR", "numera os dispositivos pelo fluxo e renomeia pelo modelo de nome"),
        ("DM_PONTAS", "modo de recálculo das pontas dos tubos soltos (conector e elevação)"),
        ("DM_ESTRUTURA", "cria a estrutura nula da Pipe Network de cada dispositivo e liga os tubos a ela"),
        ("DM_PROJETAR", "projeta dispositivos numa vista de perfil (corte pelo plano do alinhamento, com estilo e hachura)"),
        ("DM_ROTULO", "rótulo do dispositivo com modelo de macros (acompanha o dispositivo)"),
        ("DM_TABELA", "tabela nativa dos dispositivos (colunas por modelos de macros)"),
        ("DM_ESTILO", "aplica, cria ou atualiza estilos de dispositivo (tabela de componentes)"),
        ("DM_SAIDAS", "atualiza todos os rótulos, tabelas e projeções"),
        ("DM_AUDITAR", "audita os dispositivos do desenho e corrige o que tiver correção automática"),
        ("DM_OSNAP", "liga/desliga o osnap de conector"),
        ("DM_IR", "seleciona e enquadra um dispositivo pelo handle (usado pelo painel)"),
        ("C3DHYDRO", "abre o painel HYDRO (C3DHYDRO <categoria> abre direto nela)"),
    };

    [CommandMethod("DeviceModeler", "DM_AUTO", CommandFlags.Modal)]
    public void Auto() => Run(() =>
    {
        var options = new PromptKeywordOptions($"\nAtualização automática pelos tubos está {(DeviceWatcher.Enabled ? "LIGADA" : "DESLIGADA")} [Ligar/Desligar]: ", "Ligar Desligar")
        {
            AllowNone = true,
        };
        var result = Ed.GetKeywords(options);
        if (result.Status == PromptStatus.OK)
        {
            DeviceWatcher.Enabled = result.StringResult == "Ligar";
            Ed.WriteMessage($"\nAtualização automática {(DeviceWatcher.Enabled ? "ligada" : "desligada")}.");
        }
    });

    private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

    private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

    [CommandMethod("DeviceModeler", "DM_INSERIR", CommandFlags.Modal)]
    public void Insert() => Run(() =>
    {
        var definitions = DeviceCatalog.LoadAll(out var errors);
        foreach (var error in errors)
        {
            Ed.WriteMessage($"\nCatálogo: {error}");
        }

        if (definitions.Count == 0)
        {
            Ed.WriteMessage($"\nNenhuma definição válida em {DeviceCatalog.Folder}. Use DM_CATALOGO.");
            return;
        }

        using var form = new ParameterForm(definitions, null, NameFor, "DeviceModeler — inserir dispositivo", errors);
        if (AcApp.ShowModalDialog(form) != DialogResult.OK || form.Instance is null || form.Definition is null)
        {
            return;
        }

        var definition = form.Definition;
        ObjectId referenceId;
        using (var tr = Db.TransactionManager.StartTransaction())
        {
            var blockId = DeviceService.CreateBlock(tr, Db, form.Instance);
            using var preview = new BlockReference(Point3d.Origin, blockId);
            var jig = new PlacementJig(preview);
            if (!jig.Run(Ed))
            {
                tr.Abort();
                return;
            }

            var record = new DeviceRecord
            {
                DefinitionId = definition.Id,
                DefinitionVersion = definition.Version,
                DefinitionHash = definition.Hash,
                SourceFile = definition.SourceFile ?? "",
                Name = form.InstanceName,
                Values = new Dictionary<string, object>(form.Instance.Parameters, StringComparer.Ordinal),
            };
            referenceId = DeviceService.InsertReference(tr, Db, blockId, jig.Position, jig.Rotation, record).ObjectId;
            tr.Commit();
        }

        Ed.WriteMessage($"\n{form.InstanceName} ({definition.Id}) inserido.");
        HydroPanel.Reveal("DISPOSITIVOS", referenceId.Handle.ToString());
        if (definition.Parameters.Any(p => p.Target == TargetKind.Superficie))
        {
            var surfaceId = PickSurface(optional: true);
            if (!surfaceId.IsNull)
            {
                ApplySurface(new[] { referenceId }, surfaceId);
            }
        }
    });

    [CommandMethod("DeviceModeler", "DM_EDITAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Edit() => Run(() =>
    {
        var id = PickDevice("\nSelecione o dispositivo a editar: ");
        if (id.IsNull)
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var (record, definition, instance) = DeviceService.Load(tr, reference);
        using var form = new ParameterForm(new[] { definition }, instance.Parameters, _ => record.Name, $"DeviceModeler — {record.Name}");
        if (AcApp.ShowModalDialog(form) != DialogResult.OK || form.Instance is null)
        {
            return;
        }

        record.Name = form.InstanceName;
        Report(record.Name, DeviceService.Regenerate(tr, reference, definition, record, form.Values));
        tr.Commit();
        HydroPanel.Reveal("DISPOSITIVOS", id.Handle.ToString());
    });

    [CommandMethod("DeviceModeler", "DM_CONECTAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Connect() => Run(() =>
    {
        var id = PickDevice("\nSelecione o dispositivo: ");
        if (id.IsNull)
        {
            return;
        }

        var pipes = Ed.GetSelection(
            new PromptSelectionOptions { MessageForAdding = "\nSelecione os tubos ligados ao dispositivo: " },
            new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "AECC_PIPE") }));
        if (pipes.Status != PromptStatus.OK)
        {
            return;
        }

        var mode = new PromptKeywordOptions("\nConexões existentes [Substituir/Adicionar] <Substituir>: ") { AllowNone = true };
        mode.Keywords.Add("Substituir");
        mode.Keywords.Add("Adicionar");
        var modeResult = Ed.GetKeywords(mode);
        if (modeResult.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var (record, definition, instance) = DeviceService.Load(tr, reference);
        var fresh = CivilLinks.ConnectionsFromPipes(tr, pipes.Value.GetObjectIds(), DeviceService.PlacementOf(reference));
        var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
        var existing = definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is { } p && values[p.Name] is IReadOnlyList<object> list
            ? list
            : Array.Empty<object>();
        var freshRefs = fresh.Select(c => c.Reference).ToHashSet();
        var connections = (modeResult.StringResult == "Adicionar"
                ? existing.Where(e => e is not IReadOnlyDictionary<string, object> r || !freshRefs.Contains(r.GetValueOrDefault("ref") as string))
                : Enumerable.Empty<object>())
            .Concat(fresh)
            .ToList();

        var messages = new List<string>();
        CivilLinks.ApplyConnections(definition, values, connections, messages);
        messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
        Report(record.Name, messages);
        Ed.WriteMessage($"\n{record.Name}: {connections.Count} conexão(ões).");
        tr.Commit();
    });

    [CommandMethod("DeviceModeler", "DM_SUPERFICIE", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Surface() => Run(() =>
    {
        var devices = PickDevices("\nSelecione os dispositivos <Enter = todos>: ");
        var surfaceId = PickSurface(optional: false);
        if (surfaceId.IsNull)
        {
            return;
        }

        var follow = Ed.GetKeywords(new PromptKeywordOptions("\nAcompanhar a superfície quando ela mudar? [Sim/Não] <Sim>: ", "Sim Não") { AllowNone = true });
        if (follow.Status is PromptStatus.OK or PromptStatus.None)
        {
            ApplySurface(devices, surfaceId, follow.StringResult != "Não");
        }
    });

    [CommandMethod("DeviceModeler", "DM_ATUALIZAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Update() => Run(() =>
    {
        var devices = PickDevices("\nSelecione os dispositivos <Enter = todos>: ");
        using var tr = Db.TransactionManager.StartTransaction();
        var updated = 0;
        var index = 0;
        using var progress = Ui.Common.ProgressWindow.Open("HYDRO — atualizar dispositivos", null);
        foreach (var id in devices)
        {
            if (Infra.Progress.Cancelled)
            {
                Ed.WriteMessage("\nCancelado: os dispositivos já atualizados ficam.");
                break;
            }

            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var record = DeviceRecord.TryRead(reference, tr)!;
            Infra.Progress.Step(record.Name, index++, devices.Count);
            try
            {
                var (_, definition, instance) = DeviceService.Load(tr, reference);
                var messages = new List<string>();
                if (record.DefinitionHash != definition.Hash)
                {
                    messages.Add($"definição alterada ({record.DefinitionVersion} → {definition.Version})");
                }

                var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
                if (definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is { } c && values[c.Name] is IReadOnlyList<object> list && list.Count > 0)
                {
                    var refreshed = CivilLinks.RefreshConnections(tr, Db, list, DeviceService.PlacementOf(reference), messages);
                    CivilLinks.ApplyConnections(definition, values, refreshed, messages);
                }

                messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
                Report(record.Name, messages);
                updated++;
            }
            catch (Exception ex) when (ex is ModelerException or InvalidOperationException)
            {
                Ed.WriteMessage($"\n{record.Name}: {ex.Message}");
            }
        }

        tr.Commit();
        Ed.WriteMessage($"\n{updated} de {devices.Count} dispositivo(s) regenerado(s).");
    });

    [CommandMethod("DeviceModeler", "DM_QUANTITATIVOS", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Quantities() => Run(() =>
    {
        var placed = LoadPlaced(PickDevices("\nSelecione os dispositivos <Enter = todos>: "));
        if (placed.Count == 0)
        {
            Ed.WriteMessage("\nNenhum dispositivo.");
            return;
        }

        var text = new StringBuilder($"\nQuantitativos de {placed.Count} dispositivo(s):");
        foreach (var total in Reports.Totals(placed))
        {
            text.Append($"\n  {total.Name,-20} {total.Value,14:0.000} {total.Unit}");
        }

        Ed.WriteMessage(text.ToString());
        var save = Ed.GetKeywords(new PromptKeywordOptions("\nSalvar CSV detalhado? [Sim/Não] <Não>: ", "Sim Não") { AllowNone = true });
        if (save.StringResult != "Sim")
        {
            return;
        }

        var path = AskSavePath("CSV (separado por ;)|*.csv", "quantitativos.csv");
        if (path is not null)
        {
            File.WriteAllText(path, Reports.QuantitiesCsv(placed), new UTF8Encoding(true));
            Ed.WriteMessage($"\nSalvo em {path}");
        }
    });

    [CommandMethod("DeviceModeler", "DM_SWMM", CommandFlags.Modal)]
    public void Swmm() => Run(() =>
    {
        List<ObjectId> all;
        using (var tr = Db.TransactionManager.StartOpenCloseTransaction())
        {
            all = DeviceService.AllDevices(tr, Db).Select(r => r.ObjectId).ToList();
            tr.Commit();
        }

        var placed = LoadPlaced(all).Where(p => p.Instance.Hydraulics.OfType<SwmmJunction>().Any()).ToList();
        if (placed.Count == 0)
        {
            Ed.WriteMessage("\nNenhum dispositivo com nó SWMM.");
            return;
        }

        var path = AskSavePath("SWMM (*.inp)|*.inp", "dispositivos.inp");
        if (path is null)
        {
            return;
        }

        File.WriteAllText(path, Reports.SwmmJunctions(placed));
        Ed.WriteMessage($"\n{placed.Count} junção(ões) exportada(s) para {path}");
    });

    [CommandMethod("DeviceModeler", "DM_INFO", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Info() => Run(() =>
    {
        var id = PickDevice("\nSelecione o dispositivo: ");
        if (id.IsNull)
        {
            return;
        }

        using var tr = Db.TransactionManager.StartOpenCloseTransaction();
        var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var (record, definition, instance) = DeviceService.Load(tr, reference);
        var placement = DeviceService.PlacementOf(reference);
        var sb = new StringBuilder();
        sb.Append($"\n{record.Name}: {definition.Name ?? definition.Id} ({definition.Id} {definition.Version})");
        sb.Append($"\n  definição: {definition.SourceFile}");
        if (record.DefinitionHash != definition.Hash)
        {
            sb.Append($"\n  ATENÇÃO: a definição mudou desde a geração ({record.DefinitionVersion}); use DM_ATUALIZAR");
        }

        sb.Append($"\n  posição: {reference.Position.X:0.###}, {reference.Position.Y:0.###}  rotação: {placement.RotationDegrees:0.##}°");
        sb.Append("\n  Parâmetros:");
        foreach (var (name, value) in instance.Parameters)
        {
            sb.Append($"\n    {name,-16} = {Modeler.Core.Expressions.Values.Format(value)}");
        }

        sb.Append("\n  Variáveis:");
        foreach (var (name, value) in instance.Variables)
        {
            sb.Append($"\n    {name,-16} = {Modeler.Core.Expressions.Values.Format(value)}");
        }

        sb.Append("\n  Quantitativos:");
        foreach (var q in instance.Quantities)
        {
            sb.Append($"\n    {q.Name,-16} = {q.Value:0.0000} {q.Unit}");
        }

        foreach (var c in instance.Connectors)
        {
            var world = placement.ToWorld(c.Position);
            sb.Append($"\n  Conector {c.Name}: ({world.X:0.###}; {world.Y:0.###}; {world.Z:0.###}) Ø {c.Diameter:0.###}");
        }

        foreach (var j in instance.Hydraulics.OfType<SwmmJunction>())
        {
            sb.Append($"\n  SWMM junção: fundo {j.InvertElevation:0.###}, prof. máx. {j.MaxDepth:0.###}");
        }

        foreach (var w in instance.Warnings)
        {
            sb.Append($"\n  AVISO: {w}");
        }

        Ed.WriteMessage(sb.ToString());
        tr.Commit();
        HydroPanel.Reveal("DISPOSITIVOS", id.Handle.ToString());
    });

    [CommandMethod("DeviceModeler", "DM_CATALOGO", CommandFlags.Modal)]
    public void Catalog() => Run(() =>
    {
        Ed.WriteMessage($"\nPasta do catálogo: {DeviceCatalog.Folder}");
        var change = Ed.GetKeywords(new PromptKeywordOptions("\nTrocar a pasta? [Sim/Não/Padrão] <Não>: ", "Sim Não Padrão") { AllowNone = true });
        if (change.StringResult == "Sim")
        {
            using var dialog = new FolderBrowserDialog { Description = "Pasta com as definições .yaml", SelectedPath = DeviceCatalog.Folder };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                DeviceCatalog.Folder = dialog.SelectedPath;
            }
        }
        else if (change.StringResult == "Padrão")
        {
            DeviceCatalog.Folder = DeviceCatalog.DefaultFolder;
        }

        var definitions = DeviceCatalog.LoadAll(out var errors);
        Ed.WriteMessage($"\nPasta do catálogo: {DeviceCatalog.Folder}\n{definitions.Count} definição(ões):");
        foreach (var d in definitions)
        {
            Ed.WriteMessage($"\n  {d.Id,-24} {d.Version,-8} {d.Category.ToString().ToLowerInvariant(),-8} {d.Name}");
        }

        foreach (var e in errors)
        {
            Ed.WriteMessage($"\n  ERRO: {e}");
        }

        HydroPanel.Reveal("CATALOGO");
    });

    // ------------------------------------------------------------------ apoio

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException or AcException or IOException or ArgumentException or Autodesk.Civil.CivilException)
        {
            Infra.HydroLog.Warn("Comandos", ex.Message);
            Ed.WriteMessage($"\nDeviceModeler: {ex.Message}");
        }
    }

    private static string NameFor(DeviceDefinition definition)
    {
        using var tr = Db.TransactionManager.StartOpenCloseTransaction();
        var name = DeviceService.NextName(tr, Db, definition);
        tr.Commit();
        return name;
    }

    private static void Report(string name, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Ed.WriteMessage($"\n{name}: {message}");
        }
    }

    private static ObjectId PickDevice(string prompt)
    {
        var implied = Ed.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value.Count == 1 && IsDevice(implied.Value[0].ObjectId))
        {
            Ed.SetImpliedSelection(Array.Empty<ObjectId>());
            return implied.Value[0].ObjectId;
        }

        while (true)
        {
            var options = new PromptEntityOptions(prompt);
            options.SetRejectMessage("\nSelecione um dispositivo (bloco do DeviceModeler).");
            options.AddAllowedClass(typeof(BlockReference), false);
            var result = Ed.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                return ObjectId.Null;
            }

            if (IsDevice(result.ObjectId))
            {
                return result.ObjectId;
            }

            Ed.WriteMessage("\nEste bloco não é um dispositivo do DeviceModeler.");
        }
    }

    /// <summary>Seleção de dispositivos; Enter = todos do espaço modelo.</summary>
    private static List<ObjectId> PickDevices(string prompt)
    {
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
        var implied = Ed.SelectImplied();
        var result = implied.Status == PromptStatus.OK
            ? implied
            : Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = prompt }, filter);

        if (result.Status == PromptStatus.OK)
        {
            return result.Value.GetObjectIds().Where(IsDevice).ToList();
        }

        if (result.Status is PromptStatus.Error or PromptStatus.None)
        {
            using var tr = Db.TransactionManager.StartOpenCloseTransaction();
            var all = DeviceService.AllDevices(tr, Db).Select(r => r.ObjectId).ToList();
            tr.Commit();
            return all;
        }

        return new List<ObjectId>();
    }

    private static ObjectId PickSurface(bool optional)
    {
        var options = new PromptEntityOptions(optional
            ? "\nSuperfície para a elevação da tampa <Enter = pular>: "
            : "\nSelecione a superfície: ") { AllowNone = optional };
        options.SetRejectMessage("\nSelecione uma superfície do Civil 3D.");
        options.AddAllowedClass(typeof(CivilSurface), false);
        var result = Ed.GetEntity(options);
        return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
    }

    /// <param name="follow">grava a superfície e a impressão digital dela no registro: quando a
    /// superfície mudar, o <see cref="DeviceWatcher"/> refaz a elevação no fim do comando.</param>
    private static void ApplySurface(IEnumerable<ObjectId> devices, ObjectId surfaceId, bool follow = true)
    {
        using var tr = Db.TransactionManager.StartTransaction();
        var surface = (CivilSurface)tr.GetObject(surfaceId, OpenMode.ForRead);
        foreach (var id in devices)
        {
            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var (record, definition, instance) = DeviceService.Load(tr, reference);
            var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
            var messages = new List<string>();
            try
            {
                if (CivilLinks.ApplySurface(definition, values, surface, DeviceService.PlacementOf(reference), messages))
                {
                    record.FollowSurface = follow;
                    record.SurfaceHandle = surface.Handle.ToString();
                    record.SurfaceFingerprint = surface.ComputeFingerPrint().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
                    messages.Add($"ajustado à superfície {surface.Name}{(follow ? " (acompanhando)" : "")}");
                }
            }
            catch (ModelerException ex)
            {
                messages.Add(ex.Message);
            }

            Report(record.Name, messages);
        }

        tr.Commit();
    }

    private static List<PlacedInstance> LoadPlaced(IEnumerable<ObjectId> ids)
    {
        var result = new List<PlacedInstance>();
        using var tr = Db.TransactionManager.StartOpenCloseTransaction();
        foreach (var id in ids)
        {
            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            try
            {
                var (record, _, instance) = DeviceService.Load(tr, reference);
                result.Add(new PlacedInstance(record.Name, Solid3dBuilder.ToVec3(reference.Position), instance));
            }
            catch (Exception ex) when (ex is ModelerException or InvalidOperationException)
            {
                Ed.WriteMessage($"\n{id.Handle}: {ex.Message}");
            }
        }

        tr.Commit();
        return result;
    }

    private static bool IsDevice(ObjectId id)
    {
        using var tr = Db.TransactionManager.StartOpenCloseTransaction();
        var ok = tr.GetObject(id, OpenMode.ForRead) is BlockReference reference && DeviceRecord.IsDevice(reference, tr);
        tr.Commit();
        return ok;
    }

    private static string? AskSavePath(string filter, string fileName)
    {
        using var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, OverwritePrompt = true };
        var folder = Path.GetDirectoryName(Db.Filename);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
