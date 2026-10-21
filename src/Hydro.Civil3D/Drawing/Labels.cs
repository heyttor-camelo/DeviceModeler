using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Hydro.Civil3D.Civil;
using Hydro.Civil3D.Devices;

namespace Hydro.Civil3D.Drawing;

/// <summary>Rótulos de dispositivo (estudo C §3.2): um MLeader nativo com a seta no ponto de inserção
/// do dispositivo e o texto do modelo de macros. O rótulo guarda o dispositivo e o modelo
/// (<see cref="OutputRecord"/>); quando o dispositivo muda, o texto se refaz e, se ele andou, o rótulo
/// anda junto, mantendo a posição relativa que o usuário deu. Editar o texto à mão é desfeito na
/// próxima atualização (como no SOLIDOS); mudar a posição fica.</summary>
internal static class Labels
{
    public const string Layer = "HY-ROTULOS";

    public static string DefaultTemplate(InstanceCache.Entry entry) =>
        entry.Definition.Templates.Tooltip?.Text ?? "{nome}\n{codigo}";

    public static MLeader Create(Transaction tr, Database db, BlockReference device, InstanceCache.Entry entry, string template, Point3d textPoint, double height)
    {
        var record = new OutputRecord
        {
            Kind = OutputKind.Rotulo,
            Devices = { device.Handle.ToString() },
            Template = template,
            DevicePosition = new[] { device.Position.X, device.Position.Y, device.Position.Z },
        };
        var leader = new MLeader();
        leader.SetDatabaseDefaults(db);
        leader.LayerId = Layers.Ensure(tr, db, Layer, 2);
        leader.ContentType = ContentType.MTextContent;
        var text = new MText();
        text.SetDatabaseDefaults(db);
        text.TextHeight = height;
        text.Location = textPoint;
        text.Contents = Escape(OutputText.Render(template, device, entry));
        leader.MText = text;
        // a linha de chamada sai do ponto de inserção do dispositivo e vai até o texto
        leader.AddLeaderLine(new Point3d(device.Position.X, device.Position.Y, 0));
        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        space.AppendEntity(leader);
        tr.AddNewlyCreatedDBObject(leader, true);
        record.Write(leader, tr);
        return leader;
    }

    /// <summary>Refaz o texto e acompanha o deslocamento do dispositivo. Falso se o dispositivo não
    /// existe mais (o chamador apaga o rótulo).</summary>
    public static bool Update(Transaction tr, Database db, MLeader leader, OutputRecord record)
    {
        if (record.Devices.Count == 0 || !CivilLinks.TryGet<BlockReference>(tr, db, record.Devices[0], out var device))
        {
            return false;
        }

        var entry = InstanceCache.Get(tr, device);
        if (entry is null)
        {
            return true;
        }

        leader.Writable();
        var delta = new Vector3d(device.Position.X - record.DevicePosition[0], device.Position.Y - record.DevicePosition[1], 0);
        if (delta.Length > 1e-9)
        {
            leader.TransformBy(Matrix3d.Displacement(delta));
        }

        var text = leader.MText;
        text.Contents = Escape(OutputText.Render(record.Template, device, entry));
        leader.MText = text;
        record.DevicePosition = new[] { device.Position.X, device.Position.Y, device.Position.Z };
        record.Write(leader, tr);
        return true;
    }

    /// <summary>Quebra de linha do modelo vira parágrafo do MText; chaves e barras invertidas escapam.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\n").Replace("\n", "\\P");
}

/// <summary>Camadas das saídas, criadas na primeira vez com a cor pedida.</summary>
internal static class Layers
{
    public static ObjectId Ensure(Transaction tr, Database db, string name, short color, string? linetype = null)
    {
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
        {
            return layers[name];
        }

        layers.Writable();
        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
        if (linetype is not null && Linetype(tr, db, linetype) is { IsNull: false } id)
        {
            layer.LinetypeObjectId = id;
        }

        var result = layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return result;
    }

    /// <summary>Tipo de linha pelo nome; carrega do acad.lin se ainda não estiver no desenho.</summary>
    public static ObjectId Linetype(Transaction tr, Database db, string name)
    {
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (!table.Has(name))
        {
            try
            {
                db.LoadLineTypeFile(name, "acad.lin");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return ObjectId.Null;
            }

            table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        }

        return table.Has(name) ? table[name] : ObjectId.Null;
    }
}
