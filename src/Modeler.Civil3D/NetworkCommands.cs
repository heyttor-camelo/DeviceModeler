using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Catalog;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui;
using Modeler.Civil3D.Ui.Panel;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Editing;
using Modeler.Core.Errors;
using Modeler.Core.Network;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;
using Exception = System.Exception;

[assembly: CommandClass(typeof(Modeler.Civil3D.NetworkCommands))]

namespace Modeler.Civil3D;

/// <summary>Rede e lançamento (F3): lançamento em cadeia dispositivo → tubo → dispositivo (DM_REDE),
/// numeração pelo fluxo (DM_NUMERAR), modo das pontas (DM_PONTAS) e estruturas nulas da Pipe Network
/// (DM_ESTRUTURA).</summary>
public sealed class NetworkCommands
{
    private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

    private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

    private static string _lastNetwork = "Rede HYDRO";
    private static double _lastDiameter = 0.40;
    private static double _lastSlope = 0.005;

    /// <summary>Lança uma rede em cadeia: escolhe o dispositivo e os parâmetros uma vez; cada clique
    /// insere um dispositivo e o tubo da Pipe Network desde o anterior, com a declividade pedida, e
    /// liga as conexões (furos) dos dois lados. Desfazer volta um trecho; Enter termina.</summary>
    [CommandMethod("DeviceModeler", "DM_REDE", CommandFlags.Modal)]
    public void Chain() => Run(() =>
    {
        var definitions = DeviceCatalog.LoadAll(out var errors);
        if (definitions.Count == 0)
        {
            Ed.WriteMessage($"\nNenhuma definição válida em {DeviceCatalog.Folder}.");
            return;
        }

        using var form = new ParameterForm(definitions, null, d => d.Name ?? d.Id, "HYDRO — rede: dispositivo dos nós", errors);
        if (AcApp.ShowModalDialog(form) != DialogResult.OK || form.Instance is null || form.Definition is null)
        {
            return;
        }

        var definition = form.Definition;
        var baseValues = new Dictionary<string, object>(form.Instance.Parameters, StringComparer.Ordinal);
        var name = Ed.GetString(new PromptStringOptions($"\nNome da rede de tubos <{_lastNetwork}>: ") { AllowSpaces = true });
        if (name.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var networkName = string.IsNullOrWhiteSpace(name.StringResult) ? _lastNetwork : name.StringResult.Trim();
        var diameter = Ed.GetDouble(new PromptDoubleOptions($"\nDiâmetro interno do tubo (m) <{_lastDiameter:0.###}>: ") { AllowNone = true, AllowNegative = false, AllowZero = false });
        if (diameter.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var slope = Ed.GetDouble(new PromptDoubleOptions($"\nDeclividade dos tubos (m/m) <{_lastSlope:0.####}>: ") { AllowNone = true, AllowNegative = false });
        if (slope.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        _lastNetwork = networkName;
        _lastDiameter = diameter.Status == PromptStatus.OK ? diameter.Value : _lastDiameter;
        _lastSlope = slope.Status == PromptStatus.OK ? slope.Value : _lastSlope;
        var surfaceId = PickSurface();

        var steps = new Stack<Step>();
        Point3d? previous = null;
        var wire = form.Instance.Solids.SelectMany(s => Wireframe.Of(s.Node)).ToList();
        while (true)
        {
            var jig = new ChainJig(wire, previous, previous is null ? "\nPrimeiro dispositivo [Sair]: " : "\nPróximo dispositivo [Desfazer/Sair] <Sair>: ");
            var status = jig.Run(Ed);
            if (status == PromptStatus.Keyword && jig.Keyword == "Desfazer")
            {
                if (steps.Count > 0)
                {
                    UndoStep(steps.Pop());
                    previous = steps.Count > 0 ? steps.Peek().Position : null;
                }

                continue;
            }

            if (status != PromptStatus.OK || jig.Keyword is not null)
            {
                break;
            }

            var step = AddStep(definition, baseValues, networkName, surfaceId, steps.Count > 0 ? steps.Peek() : null, jig.Position, jig.Rotation);
            if (step is not null)
            {
                steps.Push(step);
                previous = step.Position;
            }
        }

        Ed.WriteMessage($"\nRede '{networkName}': {steps.Count} dispositivo(s), {steps.Count(s => !s.Pipe.IsNull)} tubo(s).");
        if (steps.Count > 1)
        {
            var number = Ed.GetKeywords(new PromptKeywordOptions("\nNumerar a rede agora? [Sim/Não] <Sim>: ", "Sim Não") { AllowNone = true });
            if (number.StringResult != "Não")
            {
                NumberDevices(steps.Select(s => s.Device).ToList(), NumberingMode.Global, 1);
            }
        }

        HydroPanel.Reveal("REDES");
    });

    /// <summary>Numera pelo fluxo (Global: das cabeceiras para a descarga, de modo que o fluxo vá do
    /// menor para o maior número; Invertido: a descarga é 1) e renomeia pelo <c>modelos.nome</c>.</summary>
    [CommandMethod("DeviceModeler", "DM_NUMERAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Numbering() => Run(() =>
    {
        var devices = PickDevices("\nDispositivos a numerar <Enter = todos>: ");
        var mode = Ed.GetKeywords(new PromptKeywordOptions("\nModo [Global/Invertido] <Global>: ", "Global Invertido") { AllowNone = true });
        if (mode.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var start = Ed.GetInteger(new PromptIntegerOptions("\nNúmero inicial <1>: ") { AllowNone = true, DefaultValue = 1, UseDefaultValue = true, LowerLimit = 0 });
        if (start.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        NumberDevices(devices, mode.StringResult == "Invertido" ? NumberingMode.Invertido : NumberingMode.Global, start.Status == PromptStatus.OK ? start.Value : 1);
    });

    /// <summary>Modo de recálculo das pontas dos tubos soltos (sem estrutura) ligados aos dispositivos:
    /// qual conector (Fixo, Ajustar = o mais próximo, Melhor3d/Melhor2d = o que deixa o tubo mais curto)
    /// e a elevação (Corrigir = a do conector, Manter = a do tubo). Aplica já.</summary>
    [CommandMethod("DeviceModeler", "DM_PONTAS", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Ends() => Run(() =>
    {
        var devices = PickDevices("\nDispositivos <Enter = todos>: ");
        var (currentChoice, currentElevation) = ConnectorMatch.ParseMode(NetworkSync.DefaultMode);
        var choice = Ed.GetKeywords(new PromptKeywordOptions($"\nConector [Fixo/Ajustar/Melhor3d/Melhor2d] <{currentChoice}>: ", "Fixo Ajustar Melhor3d Melhor2d") { AllowNone = true });
        if (choice.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var elevation = Ed.GetKeywords(new PromptKeywordOptions($"\nElevação da ponta [Corrigir/Manter] <{currentElevation}>: ", "Corrigir Manter") { AllowNone = true });
        if (elevation.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var mode = ConnectorMatch.FormatMode(
            choice.Status == PromptStatus.OK ? ConnectorMatch.ParseMode(choice.StringResult).Choice : currentChoice,
            elevation.Status == PromptStatus.OK ? (elevation.StringResult == "Manter" ? EndElevation.Manter : EndElevation.Corrigir) : currentElevation);
        if (devices.Count == 0)
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        foreach (var id in devices)
        {
            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
            var record = DeviceRecord.TryRead(reference, tr)!;
            record.EndMode = mode;
            record.Write(reference, tr);
            var entry = InstanceCache.Get(tr, reference);
            var messages = new List<string>();
            if (entry is not null && NetworkSync.AdjustEnds(tr, Db, reference, entry, messages))
            {
                RefreshFromPipes(tr, reference, messages);
            }

            Report(record.Name, messages.Prepend($"modo das pontas: {mode}"));
        }

        tr.Commit();
    });

    /// <summary>Cria a estrutura nula da Pipe Network para cada dispositivo que ainda não tem e liga a
    /// ela as pontas dos tubos conectados que estão soltas. Daí em diante, mover o dispositivo move a
    /// estrutura e os tubos.</summary>
    [CommandMethod("DeviceModeler", "DM_ESTRUTURA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Structures() => Run(() =>
    {
        var devices = PickDevices("\nDispositivos <Enter = todos>: ");
        var name = Ed.GetString(new PromptStringOptions($"\nRede de tubos para as estruturas <{_lastNetwork}>: ") { AllowSpaces = true });
        if (name.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var networkName = string.IsNullOrWhiteSpace(name.StringResult) ? _lastNetwork : name.StringResult.Trim();
        using var tr = Db.TransactionManager.StartTransaction();
        var network = PipeNetworkLink.GetOrCreate(tr, Db, networkName, ObjectId.Null);
        foreach (var id in devices)
        {
            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
            var entry = InstanceCache.Get(tr, reference);
            if (entry is null)
            {
                continue;
            }

            var record = entry.Record;
            var messages = new List<string>();
            if (!CivilLinks.TryGet<Structure>(tr, Db, record.StructureHandle, out _))
            {
                var (rim, sump) = NetworkSync.Elevations(entry);
                var structureId = PipeNetworkLink.AddNullStructure(tr, network, reference.Position, reference.Rotation, rim, sump, messages);
                if (!structureId.IsNull)
                {
                    record.StructureHandle = tr.GetObject(structureId, OpenMode.ForRead).Handle.ToString();
                    record.Network = networkName;
                    record.Write(reference, tr);
                    messages.Add($"estrutura nula criada na rede '{networkName}'");
                    AttachLooseEnds(tr, reference, entry, structureId, messages);
                }
            }

            Report(record.Name, messages);
        }

        tr.Commit();
    });

    // ------------------------------------------------------------------ lançamento em cadeia

    /// <summary>Um passo do lançamento: o dispositivo, a estrutura e o tubo que chega nele.</summary>
    private sealed record Step(ObjectId Device, ObjectId Structure, ObjectId Pipe, ObjectId Previous, Point3d Position, double Bottom);

    private static Step? AddStep(DeviceDefinition definition, Dictionary<string, object> baseValues, string networkName, ObjectId surfaceId,
        Step? previous, Point3d position, double rotation)
    {
        using var tr = Db.TransactionManager.StartTransaction();
        var messages = new List<string>();
        try
        {
            var values = new Dictionary<string, object>(baseValues, StringComparer.Ordinal);
            var top = definition.Parameters.FirstOrDefault(p => p.Target == TargetKind.Superficie && p.IsNumeric);
            var bottom = definition.Parameters.FirstOrDefault(p => p.Target == TargetKind.Tubo && p.IsNumeric);
            var network = PipeNetworkLink.GetOrCreate(tr, Db, networkName, ObjectId.Null);
            var sizes = PipeNetworkLink.PipeSizes(tr, network);
            var size = sizes.OrderBy(s => Math.Abs(s.InnerDiameter - _lastDiameter)).FirstOrDefault();

            // elevações: a tampa pela superfície (se houver); o fundo pela declividade desde o anterior
            if (previous is not null && bottom is not null)
            {
                var length = Math.Sqrt(Math.Pow(position.X - previous.Position.X, 2) + Math.Pow(position.Y - previous.Position.Y, 2));
                values = DeviceEditing.Edit(definition, values, bottom.Name, previous.Bottom - (_lastSlope * length), out var notes);
                messages.AddRange(notes);
            }

            if (!surfaceId.IsNull && top is not null && tr.GetObject(surfaceId, OpenMode.ForRead) is CivilSurface surface)
            {
                try
                {
                    values = DeviceEditing.Edit(definition, values, top.Name, surface.FindElevationAtXY(position.X, position.Y), out var notes);
                    messages.AddRange(notes);
                }
                catch (Exception)
                {
                    messages.Add("ponto fora da superfície: elevação da tampa do padrão");
                }
            }

            var instance = definition.Evaluate(values);
            var blockId = DeviceService.CreateBlock(tr, Db, instance);
            var used = new HashSet<string>(DeviceService.AllDevices(tr, Db).Select(r => DeviceRecord.TryRead(r, tr)!.Name), StringComparer.OrdinalIgnoreCase);
            var record = new DeviceRecord
            {
                DefinitionId = definition.Id,
                DefinitionVersion = definition.Version,
                DefinitionHash = definition.Hash,
                SourceFile = definition.SourceFile ?? "",
                Name = DeviceService.NextName(definition, used),
                Values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal),
                Network = networkName,
                FollowSurface = !surfaceId.IsNull,
            };
            if (!surfaceId.IsNull && tr.GetObject(surfaceId, OpenMode.ForRead) is CivilSurface s)
            {
                record.SurfaceHandle = s.Handle.ToString();
                record.SurfaceFingerprint = s.ComputeFingerPrint().ToString(CultureInfo.InvariantCulture);
            }

            var reference = DeviceService.InsertReference(tr, Db, blockId, position, rotation, record);
            var entry = InstanceCache.Get(tr, reference)!;
            var (rim, sump) = NetworkSync.Elevations(entry);
            var structureId = PipeNetworkLink.AddNullStructure(tr, network, position, rotation, rim, sump, messages);
            if (!structureId.IsNull)
            {
                record.StructureHandle = tr.GetObject(structureId, OpenMode.ForRead).Handle.ToString();
                record.Write(reference, tr);
            }

            var pipeId = ObjectId.Null;
            if (previous is not null)
            {
                if (size is null)
                {
                    messages.Add($"a lista de peças da rede '{networkName}' não tem tubos: trecho não criado");
                }
                else
                {
                    var start = new Point3d(previous.Position.X, previous.Position.Y, previous.Bottom + (size.InnerDiameter / 2));
                    var end = new Point3d(position.X, position.Y, sump + (size.InnerDiameter / 2));
                    var pipe = PipeNetworkLink.AddPipe(tr, network, size, start, end, Structure(tr, previous.Device), structureId);
                    pipeId = pipe.ObjectId;
                    AddConnection(tr, (BlockReference)tr.GetObject(previous.Device, OpenMode.ForRead), pipe, messages);
                    AddConnection(tr, reference, pipe, messages);
                    messages.Add($"tubo {pipe.Name} ({size.Description}), {pipe.Length2D:0.00} m");
                }
            }

            var bottomNow = bottom is not null && DeviceRecord.TryRead(reference, tr) is { } final && final.Values.GetValueOrDefault(bottom.Name) is double b ? b : sump;
            tr.Commit();
            Report(record.Name, messages);
            return new Step(reference.ObjectId, structureId, pipeId, previous?.Device ?? ObjectId.Null, position, bottomNow);
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception or Autodesk.Civil.CivilException or ArgumentException)
        {
            tr.Abort();
            Ed.WriteMessage($"\nHYDRO: dispositivo não inserido: {ex.Message}");
            HydroLog.Warn("Rede", ex.Message);
            return null;
        }
    }

    private static void UndoStep(Step step)
    {
        using var tr = Db.TransactionManager.StartTransaction();
        foreach (var id in new[] { step.Pipe, step.Structure, step.Device })
        {
            if (!id.IsNull && !id.IsErased)
            {
                tr.GetObject(id, OpenMode.ForWrite).Erase();
            }
        }

        if (!step.Previous.IsNull && !step.Pipe.IsNull && tr.GetObject(step.Previous, OpenMode.ForRead) is BlockReference previous)
        {
            var (record, definition, instance) = DeviceService.Load(tr, previous);
            var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
            if (definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is { } conexoes && values[conexoes.Name] is IReadOnlyList<object> list)
            {
                var handle = step.Pipe.Handle.ToString();
                values[conexoes.Name] = list.Where(c => c is not IReadOnlyDictionary<string, object> r || r.GetValueOrDefault("ref") as string != handle).ToList();
                DeviceService.Regenerate(tr, previous, definition, record, values);
            }
        }

        tr.Commit();
        Ed.WriteMessage("\nÚltimo trecho desfeito.");
    }

    private static ObjectId Structure(Transaction tr, ObjectId device) =>
        tr.GetObject(device, OpenMode.ForRead) is BlockReference reference && DeviceRecord.TryRead(reference, tr) is { } record &&
        CivilLinks.TryGet<Structure>(tr, Db, record.StructureHandle, out var structure)
            ? structure.ObjectId
            : ObjectId.Null;

    /// <summary>Acrescenta o tubo às conexões do dispositivo (furo e conector) e regenera.</summary>
    private static void AddConnection(Transaction tr, BlockReference reference, Pipe pipe, List<string> messages)
    {
        var (record, definition, instance) = DeviceService.Load(tr, reference);
        if (definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is not { } conexoes)
        {
            return;
        }

        var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
        var existing = values[conexoes.Name] as IReadOnlyList<object> ?? Array.Empty<object>();
        var fresh = CivilLinks.FromPipe(pipe, DeviceService.PlacementOf(reference));
        CivilLinks.ApplyConnections(definition, values, existing.Append(fresh).ToList(), messages);
        messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
    }

    /// <summary>Liga à estrutura as pontas soltas dos tubos conectados ao dispositivo (a ponta mais perto dele).</summary>
    private static void AttachLooseEnds(Transaction tr, BlockReference reference, InstanceCache.Entry entry, ObjectId structureId, List<string> messages)
    {
        foreach (var c in NetworkModel.Connections(entry))
        {
            if (c.GetValueOrDefault("ref") is not string handle || !CivilLinks.TryGet<Pipe>(tr, Db, handle, out var pipe))
            {
                continue;
            }

            var startNear = pipe.StartPoint.DistanceTo(reference.Position) <= pipe.EndPoint.DistanceTo(reference.Position);
            var current = startNear ? pipe.StartStructureId : pipe.EndStructureId;
            if (!current.IsNull)
            {
                continue;
            }

            pipe.Writable();
            pipe.ConnectToStructure(startNear ? ConnectorPositionType.Start : ConnectorPositionType.End, structureId, true);
            messages.Add($"tubo {pipe.Name} ligado à estrutura");
        }
    }

    private static void RefreshFromPipes(Transaction tr, BlockReference reference, List<string> messages)
    {
        var (record, definition, instance) = DeviceService.Load(tr, reference);
        if (definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes) is not { } conexoes ||
            instance.Parameters[conexoes.Name] is not IReadOnlyList<object> list || list.Count == 0)
        {
            return;
        }

        var values = new Dictionary<string, object>(instance.Parameters, StringComparer.Ordinal);
        var refreshed = CivilLinks.RefreshConnections(tr, Db, list, DeviceService.PlacementOf(reference), messages);
        CivilLinks.ApplyConnections(definition, values, refreshed, messages);
        messages.AddRange(DeviceService.Regenerate(tr, reference, definition, record, values));
    }

    // ------------------------------------------------------------------ numeração

    private static void NumberDevices(List<ObjectId> devices, NumberingMode mode, int start)
    {
        using var tr = Db.TransactionManager.StartTransaction();
        var references = devices.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().ToList();
        foreach (var network in NetworkModel.Build(tr, Db, references))
        {
            if (network.Graph.CycleNodes().Count > 0)
            {
                Ed.WriteMessage($"\nRede {network.Name}: há trechos em ciclo (elevações de fundo iguais ou invertidas); a numeração segue a ordem possível.");
            }

            Ed.WriteMessage($"\nRede {network.Name}:");
            foreach (var line in NetworkModel.Number(tr, Db, network, mode, start))
            {
                Ed.WriteMessage($"\n  {line}");
            }
        }

        tr.Commit();
        HydroPanel.NotifyChanged();
    }

    // ------------------------------------------------------------------ apoio

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception or ArgumentException or Autodesk.Civil.CivilException)
        {
            HydroLog.Warn("Rede", ex.Message);
            Ed.WriteMessage($"\nHYDRO: {ex.Message}");
        }
    }

    private static void Report(string name, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Ed.WriteMessage($"\n{name}: {message}");
        }
    }

    private static ObjectId PickSurface()
    {
        var options = new PromptEntityOptions("\nSuperfície para a elevação das tampas <Enter = sem>: ") { AllowNone = true };
        options.SetRejectMessage("\nSelecione uma superfície do Civil 3D.");
        options.AddAllowedClass(typeof(CivilSurface), false);
        var result = Ed.GetEntity(options);
        return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
    }

    /// <summary>Seleção de dispositivos; Enter = todos do espaço modelo.</summary>
    private static List<ObjectId> PickDevices(string prompt)
    {
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
        var implied = Ed.SelectImplied();
        var result = implied.Status == PromptStatus.OK ? implied : Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = prompt }, filter);
        using var tr = Db.TransactionManager.StartOpenCloseTransaction();
        var ids = result.Status == PromptStatus.OK
            ? result.Value.GetObjectIds().Where(id => tr.GetObject(id, OpenMode.ForRead) is BlockReference r && DeviceRecord.IsDevice(r, tr)).ToList()
            : result.Status is PromptStatus.Error or PromptStatus.None
                ? DeviceService.AllDevices(tr, Db).Select(r => r.ObjectId).ToList()
                : new List<ObjectId>();
        tr.Commit();
        return ids;
    }
}
