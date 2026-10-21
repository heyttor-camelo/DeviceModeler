using System;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Hydro.Civil3D.Devices;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Persistence;
using Hydro.Civil3D.Ui.Common;
using Hydro.Core.Definition;
using Hydro.Core.Editing;
using Hydro.Core.Results;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

[assembly: CommandClass(typeof(Hydro.Civil3D.GripCommands))]

namespace Hydro.Civil3D;

/// <summary>Edição pelo grip com valor digitado (HY_GRIP) e a escolha entre ela e o estiramento
/// nativo (HY_GRIPMODO).</summary>
public sealed class GripCommands
{
    private const string PreferenceKey = "grip_digitar";

    private static (ObjectId Entity, string Parameter)? _pending;

    /// <summary>Clicar um grip de parâmetro abre o HY_GRIP (arrastar ou digitar); desligado, o grip usa
    /// só o estiramento nativo do AutoCAD. Guardado nas preferências do painel.</summary>
    internal static bool TypedInput { get; private set; } = Preferences.Get(PreferenceKey) != "nao";

    internal static void SetTypedInput(bool on)
    {
        TypedInput = on;
        Preferences.Set(PreferenceKey, on ? "sim" : "nao");
    }

    /// <summary>Chamado pelo grip clicado: guarda o dispositivo e o parâmetro e manda o HY_GRIP para a
    /// linha de comando (o grip não pode abrir uma pergunta ele mesmo).</summary>
    internal static void Start(ObjectId entity, string parameter)
    {
        _pending = (entity, parameter);
        AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("HY_GRIP ", true, false, false);
    }

    /// <summary>Muda um parâmetro pelo grip: arrastar mostra a prévia como o estiramento nativo; digitar
    /// um número (1,50 ou 1.50) e Enter dá ao parâmetro esse valor exato (o valor final, não o
    /// deslocamento), dentro de min/max. Esc ou Enter sem nada não mudam nada. Aberto pelo clique no
    /// grip ou digitado (pede o dispositivo e o parâmetro).</summary>
    [CommandMethod("HYDRO", "HY_GRIP", CommandFlags.Modal)]
    public void Grip()
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        var editor = document.Editor;
        var pending = _pending;
        _pending = null;

        var id = ObjectId.Null;
        string? parameterName = null;
        if (pending is { } p && !p.Entity.IsNull && !p.Entity.IsErased && p.Entity.Database == document.Database)
        {
            (id, parameterName) = p;
        }
        else
        {
            id = PickDevice(editor);
        }

        if (id.IsNull)
        {
            return;
        }

        GripSession session;
        string deviceName;
        using (var tr = document.Database.TransactionManager.StartOpenCloseTransaction())
        {
            var reference = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var entry = InstanceCache.Get(tr, reference, out var error);
            tr.Commit();
            if (entry is null)
            {
                editor.WriteMessage($"\nHY_GRIP: {error ?? "não é um dispositivo do HYDRO"}.");
                return;
            }

            var grips = entry.Instance.Grips.Where(g => g.Kind != GripKind.Perfil).ToList();
            if (grips.Count == 0)
            {
                editor.WriteMessage($"\n{entry.Record.Name}: a definição não tem grips de parâmetro.");
                return;
            }

            parameterName ??= AskParameter(editor, grips);
            var grip = grips.FirstOrDefault(g => g.Parameter == parameterName);
            if (grip is null)
            {
                return;
            }

            session = new GripSession(entry, grip, DeviceService.PlacementOf(reference), reference.BlockTransform);
            deviceName = entry.Record.Name;
        }

        if (session.Parameter is not { } parameter || session.CurrentValue is not { } current)
        {
            editor.WriteMessage($"\n{deviceName}: o parâmetro do grip não é numérico.");
            return;
        }

        while (true)
        {
            session.Reset();
            PromptResult result;
            using (new DecimalCommaInput())
            using (PointerInputOff())
            {
                result = editor.Drag(new GripEditJig(session, DeviceEditing.GripPrompt(parameter, current)));
            }

            double value;
            if (result.Status == PromptStatus.OK)
            {
                if (session.Value is not { } dragged)
                {
                    if (session.Text.Length > 0)
                    {
                        // a definição recusou o valor arrastado: pergunta de novo
                        editor.WriteMessage($"\n{session.Text}");
                        continue;
                    }

                    editor.WriteMessage("\nNada mudou.");
                    return;
                }

                value = dragged;
            }
            else if (result.Status is PromptStatus.Keyword or PromptStatus.Other && !string.IsNullOrWhiteSpace(result.StringResult))
            {
                if (!DeviceEditing.TryParseGripInput(parameter, result.StringResult, out value, out var note))
                {
                    editor.WriteMessage($"\n{note}. Digite um número (ex.: 1,50) ou arraste.");
                    continue;
                }

                if (note.Length > 0)
                {
                    editor.WriteMessage($"\n{note}");
                }

                if (!session.SetValue(value))
                {
                    editor.WriteMessage($"\n{session.Text}");
                    continue;
                }
            }
            else
            {
                // Esc, Enter sem nada ou erro: nada muda
                editor.WriteMessage("\nNada mudou.");
                return;
            }

            if (Math.Abs(value - current) < 1e-9)
            {
                editor.WriteMessage($"\n{deviceName}: {parameter.Name} continua {DeviceEditing.FormatGripValue(parameter, current)}.");
                return;
            }

            // o dispositivo se refaz no fim do comando, pela fila (o mesmo caminho do grip nativo)
            DeviceWatcher.QueueGrip(id, parameter.Name, value);
            editor.WriteMessage($"\n{deviceName}: {parameter.Name} {DeviceEditing.FormatGripValue(parameter, current)} → {DeviceEditing.FormatGripValue(parameter, value)}");
            HydroLog.Info("Grips", $"{deviceName}: {parameter.Name} = {value} (HY_GRIP)");
            return;
        }
    }

    [CommandMethod("HYDRO", "HY_GRIPMODO", CommandFlags.Modal)]
    public void GripMode()
    {
        var editor = AcApp.DocumentManager.MdiActiveDocument.Editor;
        var options = new PromptKeywordOptions(
            $"\nClique no grip: {(TypedInput ? "arrastar ou digitar (HY_GRIP)" : "só arrastar (estiramento nativo)")} [Digitar/Nativo]: ", "Digitar Nativo") { AllowNone = true };
        var result = editor.GetKeywords(options);
        if (result.Status == PromptStatus.OK)
        {
            SetTypedInput(result.StringResult == "Digitar");
            editor.WriteMessage($"\nGrips: {(TypedInput ? "arrastar ou digitar o valor" : "só o estiramento nativo")} (vale na próxima seleção).");
        }
    }

    private static ObjectId PickDevice(Editor editor)
    {
        while (true)
        {
            var options = new PromptEntityOptions("\nSelecione o dispositivo: ");
            options.SetRejectMessage("\nSelecione um dispositivo (bloco do HYDRO).");
            options.AddAllowedClass(typeof(BlockReference), false);
            var result = editor.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                return ObjectId.Null;
            }

            using var tr = result.ObjectId.Database.TransactionManager.StartOpenCloseTransaction();
            var isDevice = DeviceRecord.IsDevice(tr.GetObject(result.ObjectId, OpenMode.ForRead), tr);
            tr.Commit();
            if (isDevice)
            {
                return result.ObjectId;
            }

            editor.WriteMessage("\nEste bloco não é um dispositivo do HYDRO.");
        }
    }

    private static string? AskParameter(Editor editor, System.Collections.Generic.IReadOnlyList<GripInstance> grips)
    {
        var names = grips.Select(g => g.Parameter).Distinct().ToList();
        if (names.Count == 1)
        {
            return names[0];
        }

        // palavras-chave só com letras, dígitos e hífen; os outros nomes ficam de fora da lista
        var keywords = names.Where(n => Regex.IsMatch(n, "^[A-Za-z][A-Za-z0-9-]*$")).ToList();
        if (keywords.Count == 0)
        {
            return names[0];
        }

        var options = new PromptKeywordOptions("\nParâmetro do grip") { AppendKeywordsToMessage = true };
        foreach (var keyword in keywords)
        {
            options.Keywords.Add(keyword);
        }

        options.Keywords.Default = keywords[0];
        var result = editor.GetKeywords(options);
        return result.Status == PromptStatus.OK ? result.StringResult : null;
    }

    /// <summary>Desliga a entrada de ponteiro da entrada dinâmica (bit 1 do DYNMODE) durante a pergunta:
    /// com ela, o número digitado iria para os campos X/Y do cursor. O valor volta ao fim.</summary>
    private static IDisposable PointerInputOff()
    {
        try
        {
            var mode = Convert.ToInt32(AcApp.GetSystemVariable("DYNMODE"));
            if (mode > 0 && (mode & 1) == 1)
            {
                AcApp.SetSystemVariable("DYNMODE", (short)(mode & ~1));
                return new Restore(() => AcApp.SetSystemVariable("DYNMODE", (short)mode));
            }
        }
        catch (Exception ex)
        {
            HydroLog.Warn("Grips", "DYNMODE: " + ex.Message);
        }

        return new Restore(() => { });
    }

    private sealed class Restore : IDisposable
    {
        private Action? _action;

        public Restore(Action action) => _action = action;

        public void Dispose()
        {
            try
            {
                _action?.Invoke();
            }
            catch (Exception ex)
            {
                HydroLog.Warn("Grips", "DYNMODE: " + ex.Message);
            }

            _action = null;
        }
    }
}
