using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Core.Drawing;

namespace Modeler.Civil3D.Drawing;

/// <summary>Estilos guardados no próprio DWG, no dicionário de objetos nomeados (estudo C §3.1):
/// <c>HYDRO_ESTILOS/&lt;nome&gt;</c> (dispositivo) e <c>HYDRO_ESTILOS_PROJECAO/&lt;nome&gt;</c> (projeção em
/// perfil), um Xrecord com o JSON em pedaços (código 300). O estilo "Padrão" existe sempre: o de
/// dispositivo é vazio (camada DM-&lt;NOME&gt; por sólido, como antes) e o de projeção tem os
/// componentes padrão.</summary>
internal static class StyleStore
{
    public const string DeviceKey = "HYDRO_ESTILOS";
    public const string ProjectionKey = "HYDRO_ESTILOS_PROJECAO";
    private const int ChunkSize = 250;

    public static List<string> DeviceStyleNames(Transaction tr, Database db) =>
        Names(tr, db, DeviceKey).Prepend(DeviceStyle.DefaultName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<string> ProjectionStyleNames(Transaction tr, Database db) =>
        Names(tr, db, ProjectionKey).Prepend(ProjectionStyle.DefaultName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static DeviceStyle Device(Transaction tr, Database db, string? name)
    {
        var json = Read(tr, db, DeviceKey, string.IsNullOrEmpty(name) ? DeviceStyle.DefaultName : name);
        return json is null ? new DeviceStyle() : DeviceStyle.FromJson(json);
    }

    public static ProjectionStyle Projection(Transaction tr, Database db, string? name)
    {
        var json = Read(tr, db, ProjectionKey, string.IsNullOrEmpty(name) ? ProjectionStyle.DefaultName : name);
        return json is null ? new ProjectionStyle() : ProjectionStyle.FromJson(json);
    }

    public static void Save(Transaction tr, Database db, DeviceStyle style) => Write(tr, db, DeviceKey, style.Name, style.ToJson());

    public static void Save(Transaction tr, Database db, ProjectionStyle style) => Write(tr, db, ProjectionKey, style.Name, style.ToJson());

    public static bool Delete(Transaction tr, Database db, bool projection, string name)
    {
        var dictionary = Dictionary(tr, db, projection ? ProjectionKey : DeviceKey, create: false);
        if (dictionary is null || !dictionary.Contains(name))
        {
            return false;
        }

        tr.GetObject(dictionary.GetAt(name), OpenMode.ForWrite).Erase();
        return true;
    }

    private static IEnumerable<string> Names(Transaction tr, Database db, string key)
    {
        var dictionary = Dictionary(tr, db, key, create: false);
        if (dictionary is null)
        {
            yield break;
        }

        foreach (DBDictionaryEntry entry in dictionary)
        {
            yield return entry.Key;
        }
    }

    private static string? Read(Transaction tr, Database db, string key, string name)
    {
        var dictionary = Dictionary(tr, db, key, create: false);
        if (dictionary is null || !dictionary.Contains(name) || tr.GetObject(dictionary.GetAt(name), OpenMode.ForRead) is not Xrecord xrecord)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var value in xrecord.Data?.AsArray() ?? Array.Empty<TypedValue>())
        {
            if (value.TypeCode == 300)
            {
                sb.Append((string)value.Value);
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void Write(Transaction tr, Database db, string key, string name, string json)
    {
        var dictionary = Dictionary(tr, db, key, create: true)!;
        Xrecord xrecord;
        if (dictionary.Contains(name))
        {
            xrecord = (Xrecord)tr.GetObject(dictionary.GetAt(name), OpenMode.ForWrite);
        }
        else
        {
            dictionary.Writable();
            xrecord = new Xrecord();
            dictionary.SetAt(name, xrecord);
            tr.AddNewlyCreatedDBObject(xrecord, true);
        }

        var data = new List<TypedValue>();
        for (var i = 0; i < json.Length; i += ChunkSize)
        {
            data.Add(new TypedValue(300, json.Substring(i, Math.Min(ChunkSize, json.Length - i))));
        }

        xrecord.Data = new ResultBuffer(data.ToArray());
    }

    private static DBDictionary? Dictionary(Transaction tr, Database db, string key, bool create)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(key))
        {
            return (DBDictionary)tr.GetObject(nod.GetAt(key), OpenMode.ForRead);
        }

        if (!create)
        {
            return null;
        }

        nod.Writable();
        var dictionary = new DBDictionary();
        nod.SetAt(key, dictionary);
        tr.AddNewlyCreatedDBObject(dictionary, true);
        return dictionary;
    }
}
