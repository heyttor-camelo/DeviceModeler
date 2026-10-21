using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Infra;
using Modeler.Civil3D.Persistence;
using Modeler.Civil3D.Ui.Panel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(Modeler.Civil3D.MaintenanceCommands))]

namespace Modeler.Civil3D;

/// <summary>Manutenção do desenho: auditoria (DM_AUDITAR) e osnap de conector (DM_OSNAP).</summary>
public sealed class MaintenanceCommands
{
    /// <summary>Lista os problemas dos dispositivos e, com Corrigir, aplica as correções automáticas
    /// (migrar registro, separar bloco, renomear, remover conexão morta, regenerar).</summary>
    [CommandMethod("DeviceModeler", "DM_AUDITAR", CommandFlags.Modal)]
    public void Audit()
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        var editor = document.Editor;
        using var tr = document.Database.TransactionManager.StartTransaction();
        var issues = DeviceAudit.Run(tr, document.Database);
        var stamp = DrawingVersion.Read(document.Database, tr);
        editor.WriteMessage($"\nHYDRO: desenho gravado por {(stamp is { } s ? $"HYDRO {s.Version} (esquema {s.Schema})" : "versão anterior ao registro de versão")}.");
        if (issues.Count == 0)
        {
            editor.WriteMessage("\nNenhum problema nos dispositivos.");
            tr.Commit();
            return;
        }

        foreach (var issue in issues)
        {
            editor.WriteMessage($"\n  {issue.Level.ToString().ToUpperInvariant(),-5} {issue.Device,-14} {issue.Kind}: {issue.Message}{(issue.Fix is null ? "" : $" → {issue.Fix}")}");
        }

        var fixable = issues.Count(i => i.Fix is not null);
        editor.WriteMessage($"\n{issues.Count} problema(s), {fixable} com correção automática.");
        HydroLog.Info("Auditoria", $"{issues.Count} problema(s), {fixable} corrigível(is)");
        if (fixable > 0)
        {
            var answer = editor.GetKeywords(new PromptKeywordOptions("\nCorrigir agora? [Corrigir/Sair] <Sair>: ", "Corrigir Sair") { AllowNone = true });
            if (answer.Status == PromptStatus.OK && answer.StringResult == "Corrigir")
            {
                foreach (var message in DeviceAudit.Repair(tr, document.Database, issues))
                {
                    editor.WriteMessage($"\n  {message}");
                    HydroLog.Info("Auditoria", message);
                }
            }
        }

        tr.Commit();
        HydroPanel.Reveal("VERIFICACAO");
    }

    [CommandMethod("DeviceModeler", "DM_OSNAP", CommandFlags.Modal)]
    public void Osnap()
    {
        var editor = AcApp.DocumentManager.MdiActiveDocument.Editor;
        var options = new PromptKeywordOptions($"\nOsnap de conector está {(ConnectorSnap.Active ? "LIGADO" : "DESLIGADO")} [Ligar/Desligar]: ", "Ligar Desligar") { AllowNone = true };
        var result = editor.GetKeywords(options);
        if (result.Status == PromptStatus.OK)
        {
            ConnectorSnap.Activate(result.StringResult == "Ligar");
            editor.WriteMessage($"\nOsnap de conector {(ConnectorSnap.Active ? "ligado" : "desligado")}.");
        }
    }
}
