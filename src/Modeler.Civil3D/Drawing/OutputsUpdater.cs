using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Table = Autodesk.AutoCAD.DatabaseServices.Table;

namespace Modeler.Civil3D.Drawing;

/// <summary>Mantém as saídas em dia (última fase da fila do <see cref="DeviceWatcher"/>): rótulos,
/// tabelas e projeções de dispositivos refeitos são atualizados; os de dispositivos apagados saem;
/// projeções de vistas ou alinhamentos alterados se refazem.</summary>
internal static class OutputsUpdater
{
    public static void Start() => DeviceWatcher.DevicesChanged += OnChanged;

    /// <summary>Atualiza todas as saídas do desenho (DM_SAIDAS).</summary>
    public static int UpdateAll(Transaction tr, Database db)
    {
        var count = 0;
        var outputs = OutputRecord.All(tr, db);
        using var progress = Ui.Common.ProgressWindow.Open("HYDRO — atualizar saídas", null);
        for (var i = 0; i < outputs.Count && !Progress.Cancelled; i++)
        {
            var (entity, record) = outputs[i];
            Progress.Step(record.Kind + " " + entity.Handle, i, outputs.Count);
            if (Update(tr, db, entity, record))
            {
                count++;
            }
        }

        return count;
    }

    private static void OnChanged(Transaction tr, Database db, OutputChanges changes)
    {
        if (changes.Changed.Count == 0 && changes.Erased.Count == 0 && changes.Views.Count == 0 && changes.Alignments.Count == 0)
        {
            return;
        }

        foreach (var (entity, record) in OutputRecord.All(tr, db))
        {
            try
            {
                var erased = !record.AllDevices && record.Devices.Count > 0 && record.Devices.All(changes.Erased.Contains);
                if (erased && record.Kind != OutputKind.Tabela)
                {
                    entity.UpgradeOpen();
                    entity.Erase();
                    continue;
                }

                var touched = record.Devices.Any(h => changes.Changed.Contains(h) || changes.Erased.Contains(h)) ||
                              (record.Kind == OutputKind.Tabela && record.AllDevices && (changes.Changed.Count > 0 || changes.Erased.Count > 0)) ||
                              (record.Kind == OutputKind.Projecao && ViewTouched(tr, db, record, changes));
                if (touched)
                {
                    Update(tr, db, entity, record);
                }
            }
            catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException)
            {
                HydroLog.Warn("Saídas", $"{entity.Handle}: {ex.Message}");
            }
        }
    }

    private static bool ViewTouched(Transaction tr, Database db, OutputRecord record, OutputChanges changes)
    {
        if (changes.Views.Contains(record.ProfileView))
        {
            return true;
        }

        return changes.Alignments.Count > 0 && CivilLinks.TryGet<ProfileView>(tr, db, record.ProfileView, out var view) &&
               changes.Alignments.Contains(view.AlignmentId.Handle.ToString());
    }

    /// <summary>Refaz uma saída; apaga rótulo ou projeção cujo dispositivo (ou vista) sumiu.</summary>
    private static bool Update(Transaction tr, Database db, Autodesk.AutoCAD.DatabaseServices.Entity entity, OutputRecord record)
    {
        var alive = entity switch
        {
            MLeader leader when record.Kind == OutputKind.Rotulo => Labels.Update(tr, db, leader, record),
            Table table when record.Kind == OutputKind.Tabela => Fill(tr, db, table, record),
            BlockReference projection when record.Kind == OutputKind.Projecao => Projections.Update(tr, db, projection, record),
            _ => true,
        };
        if (!alive)
        {
            entity.UpgradeOpen();
            entity.Erase();
        }

        return alive;
    }

    private static bool Fill(Transaction tr, Database db, Table table, OutputRecord record)
    {
        // lista sem nenhum dispositivo vivo: a tabela fica (vazia), o usuário decide apagar
        record.Devices = record.Devices.Where(h => CivilLinks.TryGet<BlockReference>(tr, db, h, out _)).ToList();
        Tables.Fill(tr, db, table, record);
        record.Write(table, tr);
        return true;
    }
}
