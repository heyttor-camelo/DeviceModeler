using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Persistence;
using Hydro.Civil3D.Ui.Panel;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(Hydro.Civil3D.PanelCommands))]

namespace Hydro.Civil3D;

/// <summary>Comandos do painel HYDRO: abrir o painel (opcionalmente numa categoria) e ir até um
/// dispositivo pelo handle (usado pelo painel antes de HY_EDITAR, HY_INFO...).</summary>
public sealed class PanelCommands
{
    /// <summary><c>C3DHYDRO</c> abre o painel; <c>C3DHYDRO Dispositivos</c> (ou outra categoria) abre
    /// direto nela. Enter abre na última categoria usada.</summary>
    [CommandMethod("HYDRO", "C3DHYDRO", CommandFlags.Modal)]
    public void Panel()
    {
        var editor = AcApp.DocumentManager.MdiActiveDocument.Editor;
        var options = new PromptKeywordOptions("\nCategoria do painel HYDRO [" + string.Join("/", HydroPanel.Keywords) + "] <última>: ")
        {
            AllowNone = true,
        };
        foreach (var keyword in HydroPanel.Keywords)
        {
            options.Keywords.Add(keyword);
        }

        var result = editor.GetKeywords(options);
        if (result.Status is not (PromptStatus.OK or PromptStatus.None))
        {
            return;
        }

        try
        {
            HydroPanel.ShowPanel(result.Status == PromptStatus.OK ? result.StringResult : null);
        }
        catch (System.Exception ex)
        {
            HydroLog.Error("Painel", "falha ao abrir o painel", ex);
            editor.WriteMessage($"\nHYDRO: falha ao abrir o painel: {ex.Message}");
        }
    }

    /// <summary>Seleciona (pickfirst) e enquadra a entidade de um handle. Fica selecionada para o
    /// comando seguinte (HY_EDITAR, HY_INFO...).</summary>
    [CommandMethod("HYDRO", "HY_IR", CommandFlags.Modal | CommandFlags.Redraw | CommandFlags.NoHistory)]
    public void GoTo()
    {
        var document = AcApp.DocumentManager.MdiActiveDocument;
        var editor = document.Editor;
        var text = editor.GetString(new PromptStringOptions("\nHandle do dispositivo: ") { AllowSpaces = false });
        if (text.Status != PromptStatus.OK)
        {
            return;
        }

        ObjectId id;
        try
        {
            id = document.Database.GetObjectId(false, new Handle(Convert.ToInt64(text.StringResult, 16)), 0);
        }
        catch (System.Exception)
        {
            editor.WriteMessage($"\nHandle '{text.StringResult}' não existe neste desenho.");
            return;
        }

        if (id.IsNull || id.IsErased)
        {
            editor.WriteMessage($"\nHandle '{text.StringResult}' foi apagado.");
            return;
        }

        using (var tr = document.Database.TransactionManager.StartOpenCloseTransaction())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Entity entity)
            {
                try
                {
                    var extents = entity.GeometricExtents;
                    var margin = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y) * 0.6 + 1;
                    Zoom(editor, new Point3d(extents.MinPoint.X - margin, extents.MinPoint.Y - margin, 0), new Point3d(extents.MaxPoint.X + margin, extents.MaxPoint.Y + margin, 0));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // sem extensão (bloco vazio): só seleciona
                }

                var name = DeviceRecord.TryRead(entity, tr)?.Name;
                if (name is not null)
                {
                    editor.WriteMessage($"\n{name} selecionado.");
                }
            }

            tr.Commit();
        }

        editor.SetImpliedSelection(new[] { id });
    }

    private static void Zoom(Editor editor, Point3d min, Point3d max)
    {
        using var view = editor.GetCurrentView();
        // WCS → DCS (receita clássica da ViewTableRecord)
        var toEye = (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
                     Matrix3d.Displacement(view.Target - Point3d.Origin) *
                     Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();
        var a = min.TransformBy(toEye);
        var b = max.TransformBy(toEye);
        var corners = new[] { a, b, new Point3d(a.X, b.Y, 0), new Point3d(b.X, a.Y, 0) }.ToList();
        var width = corners.Max(p => p.X) - corners.Min(p => p.X);
        var height = corners.Max(p => p.Y) - corners.Min(p => p.Y);
        var center = new Point2d((corners.Max(p => p.X) + corners.Min(p => p.X)) / 2, (corners.Max(p => p.Y) + corners.Min(p => p.Y)) / 2);
        var ratio = view.Width / view.Height;
        view.CenterPoint = center;
        view.Width = Math.Max(width, height * ratio);
        view.Height = Math.Max(height, width / ratio);
        editor.SetCurrentView(view);
    }
}
