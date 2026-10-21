using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Drawing;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui.Common;
using Modeler.Civil3D.Ui.Panel;
using Modeler.Core.Drawing;
using Modeler.Core.Errors;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

[assembly: CommandClass(typeof(Modeler.Civil3D.OutputCommands))]

namespace Modeler.Civil3D;

/// <summary>Desenho e saídas (F4): projeção em vista de perfil (DM_PROJETAR), rótulo (DM_ROTULO),
/// tabela nativa (DM_TABELA), estilo de dispositivo (DM_ESTILO) e atualização de tudo (DM_SAIDAS).</summary>
public sealed class OutputCommands
{
    private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

    private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

    /// <summary>Projeta dispositivos numa vista de perfil, cortando o sólido pelo plano do alinhamento,
    /// com o estilo de projeção (componentes, hachura), o lado e a distância máxima.</summary>
    [CommandMethod("DeviceModeler", "DM_PROJETAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Project() => Run(() =>
    {
        var devices = PickDevices("\nDispositivos a projetar <Enter = todos>: ");
        var viewOptions = new PromptEntityOptions("\nSelecione a vista de perfil (ProfileView): ");
        viewOptions.SetRejectMessage("\nSelecione uma vista de perfil do Civil 3D.");
        viewOptions.AddAllowedClass(typeof(ProfileView), false);
        var viewResult = Ed.GetEntity(viewOptions);
        if (viewResult.Status != PromptStatus.OK)
        {
            return;
        }

        string styleName;
        using (var probe = Db.TransactionManager.StartOpenCloseTransaction())
        {
            var names = StyleStore.ProjectionStyleNames(probe, Db);
            probe.Commit();
            var styleText = Ed.GetString(new PromptStringOptions($"\nEstilo de projeção ({string.Join(", ", names)}) <{ProjectionStyle.DefaultName}>: ") { AllowSpaces = true });
            if (styleText.Status is not (PromptStatus.OK or PromptStatus.None))
            {
                return;
            }

            styleName = string.IsNullOrWhiteSpace(styleText.StringResult) ? ProjectionStyle.DefaultName : styleText.StringResult.Trim();
        }

        var side = Ed.GetKeywords(new PromptKeywordOptions("\nLado [Ambos/Esquerdo/Direito] <do estilo>: ", "Ambos Esquerdo Direito") { AllowNone = true });
        if (side.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var distance = Ed.GetDouble(new PromptDoubleOptions("\nDistância máxima ao alinhamento (m) <do estilo>: ") { AllowNone = true, AllowNegative = false });
        if (distance.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        var view = (ProfileView)tr.GetObject(viewResult.ObjectId, OpenMode.ForRead);
        var existing = OutputRecord.All(tr, Db).Where(o => o.Record.Kind == OutputKind.Projecao && o.Record.ProfileView == view.Handle.ToString())
            .Select(o => o.Record.Devices.FirstOrDefault()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var id in devices)
        {
            var device = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var name = DeviceRecord.TryRead(device, tr)!.Name;
            if (existing.Contains(device.Handle.ToString()))
            {
                Ed.WriteMessage($"\n{name}: já tem projeção nesta vista (DM_SAIDAS atualiza).");
                continue;
            }

            var record = new OutputRecord
            {
                Kind = OutputKind.Projecao,
                Devices = { device.Handle.ToString() },
                ProfileView = view.Handle.ToString(),
                Style = styleName,
                Side = side.Status == PromptStatus.OK ? side.StringResult : null,
                MaxDistance = distance.Status == PromptStatus.OK ? distance.Value : null,
            };
            Projections.Create(tr, Db, device, view, record);
            created++;
            Ed.WriteMessage($"\n{name}: {(record.Status.Length == 0 ? "projetado" : "não aparece: " + record.Status)}");
        }

        tr.Commit();
        Ed.WriteMessage($"\n{created} projeção(ões) em {view.Name}.");
        HydroPanel.Reveal("SAIDAS");
    });

    /// <summary>Rótulo com o modelo de macros (padrão: a dica da definição), num MLeader que acompanha
    /// o dispositivo.</summary>
    [CommandMethod("DeviceModeler", "DM_ROTULO", CommandFlags.Modal)]
    public void Label() => Run(() =>
    {
        var options = new PromptEntityOptions("\nSelecione o dispositivo: ");
        options.SetRejectMessage("\nSelecione um dispositivo.");
        options.AddAllowedClass(typeof(BlockReference), false);
        var picked = Ed.GetEntity(options);
        if (picked.Status != PromptStatus.OK)
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        var device = (BlockReference)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
        var entry = InstanceCache.Get(tr, device, out var error);
        if (entry is null)
        {
            Ed.WriteMessage($"\n{(DeviceRecord.IsDevice(device, tr) ? error : "não é um dispositivo do HYDRO")}");
            return;
        }

        var fallback = Preferences.Get("modelo_rotulo") ?? Labels.DefaultTemplate(entry).Replace("\n", "\\n");
        var template = Ed.GetString(new PromptStringOptions($"\nModelo do texto (\\n quebra a linha) <{fallback}>: ") { AllowSpaces = true });
        if (template.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var text = (string.IsNullOrWhiteSpace(template.StringResult) ? fallback : template.StringResult).Replace("\\n", "\n");
        if (!string.IsNullOrWhiteSpace(template.StringResult))
        {
            Preferences.Set("modelo_rotulo", template.StringResult);
        }

        var point = Ed.GetPoint(new PromptPointOptions("\nPosição do texto: ") { BasePoint = device.Position, UseBasePoint = true, UseDashedLine = true });
        if (point.Status != PromptStatus.OK)
        {
            return;
        }

        var height = double.TryParse(Preferences.Get("altura_rotulo"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : 0.25;
        Labels.Create(tr, Db, device, entry, text, point.Value, height);
        tr.Commit();
    });

    /// <summary>Tabela nativa dos dispositivos (todos ou a seleção), com colunas prontas
    /// (Padrão = identificação e elevações; Quantitativos = os quantitativos das definições).</summary>
    [CommandMethod("DeviceModeler", "DM_TABELA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Table() => Run(() =>
    {
        var implied = Ed.SelectImplied();
        var selection = implied.Status == PromptStatus.OK
            ? implied
            : Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nDispositivos da tabela <Enter = todos, e os que vierem depois>: " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") }));
        if (selection.Status is not (PromptStatus.OK or PromptStatus.Error or PromptStatus.None))
        {
            return;
        }

        var preset = Ed.GetKeywords(new PromptKeywordOptions("\nColunas [Padrao/Quantitativos] <Padrao>: ", "Padrao Quantitativos") { AllowNone = true });
        if (preset.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var point = Ed.GetPoint("\nPonto de inserção da tabela: ");
        if (point.Status != PromptStatus.OK)
        {
            return;
        }

        using var tr = Db.TransactionManager.StartTransaction();
        var all = selection.Status != PromptStatus.OK;
        var references = all
            ? DeviceService.AllDevices(tr, Db)
            : selection.Value.GetObjectIds().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Where(r => DeviceRecord.IsDevice(r, tr)).ToList();
        var entries = references.Select(r => InstanceCache.Get(tr, r)).OfType<InstanceCache.Entry>().ToList();
        var record = new OutputRecord
        {
            Kind = OutputKind.Tabela,
            AllDevices = all,
            Devices = all ? new List<string>() : references.Select(r => r.Handle.ToString()).ToList(),
            Columns = Tables.Preset(preset.StringResult == "Quantitativos" ? "Quantitativos" : "Padrao", entries),
            SortColumn = 1,
            Title = preset.StringResult == "Quantitativos" ? "Quantitativos dos dispositivos" : "Dispositivos",
        };
        Tables.Create(tr, Db, point.Value, record);
        tr.Commit();
        Ed.WriteMessage($"\nTabela com {references.Count} dispositivo(s){(all ? " (acompanha os novos)" : "")}.");
    });

    /// <summary>Estilo de dispositivo: Aplicar um estilo aos dispositivos, Criar um a partir de um
    /// dispositivo (uma linha por sólido) ou Atualizar os dispositivos depois de editar um estilo.</summary>
    [CommandMethod("DeviceModeler", "DM_ESTILO", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Style() => Run(() =>
    {
        var action = Ed.GetKeywords(new PromptKeywordOptions("\nEstilo de dispositivo [Aplicar/Criar/Atualizar] <Aplicar>: ", "Aplicar Criar Atualizar") { AllowNone = true });
        if (action.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        var devices = PickDevices("\nDispositivos <Enter = todos>: ");
        using var tr = Db.TransactionManager.StartTransaction();
        switch (action.StringResult)
        {
            case "Criar":
            {
                var nameResult = Ed.GetString(new PromptStringOptions("\nNome do estilo novo: ") { AllowSpaces = true });
                if (nameResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(nameResult.StringResult) || devices.Count == 0)
                {
                    return;
                }

                var device = (BlockReference)tr.GetObject(devices[0], OpenMode.ForRead);
                var entry = InstanceCache.Get(tr, device);
                if (entry is null)
                {
                    return;
                }

                var style = new DeviceStyle { Name = nameResult.StringResult.Trim(), Description = $"criado de {entry.Record.Name} ({entry.Definition.Id})" };
                style.Components.AddRange(entry.Instance.Solids.Select(s => new StyleComponent { Name = s.Name, Layer = DeviceService.LayerName(s.Name), Material = s.Material }));
                style.Components.Add(new StyleComponent { Name = "*", Description = "sólidos sem linha própria" });
                StyleStore.Save(tr, Db, style);
                Ed.WriteMessage($"\nEstilo '{style.Name}' criado com {style.Components.Count} componente(s). Edite no painel (Saídas) e use Aplicar.");
                break;
            }

            case "Atualizar":
                foreach (var id in devices)
                {
                    Regenerate(tr, id, null);
                }

                break;
            default:
            {
                var names = StyleStore.DeviceStyleNames(tr, Db);
                var nameResult = Ed.GetString(new PromptStringOptions($"\nEstilo ({string.Join(", ", names)}) <{DeviceStyle.DefaultName}>: ") { AllowSpaces = true });
                if (nameResult.Status is not (PromptStatus.OK or PromptStatus.None))
                {
                    return;
                }

                var name = string.IsNullOrWhiteSpace(nameResult.StringResult) ? DeviceStyle.DefaultName : nameResult.StringResult.Trim();
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    Ed.WriteMessage($"\nEstilo '{name}' não existe.");
                    return;
                }

                foreach (var id in devices)
                {
                    Regenerate(tr, id, name == DeviceStyle.DefaultName ? "" : name);
                }

                break;
            }
        }

        tr.Commit();
        HydroPanel.NotifyChanged();
    });

    /// <summary>Atualiza todas as saídas do desenho (rótulos, tabelas e projeções).</summary>
    [CommandMethod("DeviceModeler", "DM_SAIDAS", CommandFlags.Modal)]
    public void Outputs() => Run(() =>
    {
        using var tr = Db.TransactionManager.StartTransaction();
        var count = OutputsUpdater.UpdateAll(tr, Db);
        tr.Commit();
        Ed.WriteMessage($"\n{count} saída(s) atualizada(s).");
        HydroPanel.NotifyChanged();
    });

    private static void Regenerate(Transaction tr, ObjectId id, string? style)
    {
        var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var (record, definition, _) = DeviceService.Load(tr, reference);
        if (style is not null)
        {
            record.Style = style;
        }

        foreach (var message in DeviceService.Regenerate(tr, reference, definition, record, null, forceRebuild: true))
        {
            Ed.WriteMessage($"\n{record.Name}: {message}");
        }
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ModelerException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception or ArgumentException)
        {
            HydroLog.Warn("Saídas", ex.Message);
            Ed.WriteMessage($"\nHYDRO: {ex.Message}");
        }
    }

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
