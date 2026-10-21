using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;

namespace Hydro.Civil3D.Drawing;

internal enum OutputKind
{
    Rotulo,
    Tabela,
    Projecao,
}

/// <summary>Coluna de tabela: título e modelo de texto (<c>{H:m:2}</c>...).</summary>
internal sealed record TableColumn(string Title, string Template);

/// <summary>Ligação de uma saída do desenho (rótulo, tabela ou projeção) com os dispositivos, no
/// dicionário de extensão da entidade (<c>HYDRO_SAIDA</c>, JSON em pedaços). Os dispositivos são
/// referidos pelo handle; tabela com <see cref="AllDevices"/> pega todos os do desenho.</summary>
internal sealed class OutputRecord
{
    public const string Key = "HYDRO_SAIDA";
    private const int ChunkSize = 250;

    public OutputKind Kind { get; set; }

    public List<string> Devices { get; set; } = new();

    public bool AllDevices { get; set; }

    /// <summary>Rótulo: modelo do texto.</summary>
    public string Template { get; set; } = "";

    /// <summary>Rótulo: posição do dispositivo na última atualização (o rótulo anda junto).</summary>
    public double[] DevicePosition { get; set; } = new double[3];

    /// <summary>Tabela: colunas e coluna de ordenação (-1 = ordem dos dispositivos).</summary>
    public List<TableColumn> Columns { get; set; } = new();

    public int SortColumn { get; set; } = -1;

    public string Title { get; set; } = "";

    /// <summary>Projeção: vista de perfil, estilo, lado e distância máxima (nulos = do estilo).</summary>
    public string ProfileView { get; set; } = "";

    public string Style { get; set; } = "";

    public string? Side { get; set; }

    public double? MaxDistance { get; set; }

    /// <summary>Projeção: por que o dispositivo não aparece (fora da vista, do lado, longe demais).</summary>
    public string Status { get; set; } = "";

    public static OutputRecord? TryRead(DBObject obj, Transaction tr)
    {
        if (obj.ExtensionDictionary.IsNull)
        {
            return null;
        }

        var dictionary = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(Key) || tr.GetObject(dictionary.GetAt(Key), OpenMode.ForRead) is not Xrecord xrecord)
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

        try
        {
            return JsonSerializer.Deserialize<OutputRecord>(sb.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(DBObject obj, Transaction tr)
    {
        if (obj.ExtensionDictionary.IsNull)
        {
            obj.Writable();
            obj.CreateExtensionDictionary();
        }

        var dictionary = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
        Xrecord xrecord;
        if (dictionary.Contains(Key))
        {
            xrecord = (Xrecord)tr.GetObject(dictionary.GetAt(Key), OpenMode.ForWrite);
        }
        else
        {
            xrecord = new Xrecord();
            dictionary.SetAt(Key, xrecord);
            tr.AddNewlyCreatedDBObject(xrecord, true);
        }

        var json = JsonSerializer.Serialize(this);
        var data = new List<TypedValue>();
        for (var i = 0; i < json.Length; i += ChunkSize)
        {
            data.Add(new TypedValue(300, json.Substring(i, Math.Min(ChunkSize, json.Length - i))));
        }

        xrecord.Data = new ResultBuffer(data.ToArray());
    }

    public bool Uses(string deviceHandle) => AllDevices || Devices.Contains(deviceHandle, StringComparer.OrdinalIgnoreCase);

    /// <summary>Todas as saídas do espaço modelo, com a entidade.</summary>
    public static List<(Entity Entity, OutputRecord Record)> All(Transaction tr, Database db)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var result = new List<(Entity, OutputRecord)>();
        foreach (ObjectId id in modelSpace)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Entity entity && !entity.ExtensionDictionary.IsNull && TryRead(entity, tr) is { } record)
            {
                result.Add((entity, record));
            }
        }

        return result;
    }
}
