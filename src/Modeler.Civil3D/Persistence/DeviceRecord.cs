using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Core.Persistence;

namespace Modeler.Civil3D.Persistence;

/// <summary>Dados de uma instância gravados no DWG, no dicionário de extensão da entidade
/// hospedeira (BlockReference):
/// <code>
/// ExtensionDictionary
///   └─ "DEVICEMODELER" (Xrecord)
///        (1)  "DeviceModeler"      marcador
///        (70) versão do esquema
///        (1)  id da definição      ex.: pv_retangular
///        (1)  versão da definição  ex.: 1.0.0
///        (1)  hash SHA-256 do YAML usado
///        (1)  caminho do YAML usado
///        (1)  nome da instância    ex.: PV-01
///        (300 ...) JSON dos parâmetros, em pedaços de até 250 caracteres
/// </code>
/// Sem o plugin o desenho continua válido: a geometria é um bloco anônimo comum.</summary>
internal sealed class DeviceRecord
{
    public const string Key = "DEVICEMODELER";
    private const string Marker = "DeviceModeler";
    private const short Schema = 1;
    private const int ChunkSize = 250;

    /// <summary>Código 300 (texto arbitrário) para os pedaços do JSON.</summary>
    private const short JsonChunkCode = 300;

    public string DefinitionId { get; set; } = "";

    public string DefinitionVersion { get; set; } = "";

    public string DefinitionHash { get; set; } = "";

    public string SourceFile { get; set; } = "";

    public string Name { get; set; } = "";

    public Dictionary<string, object> Values { get; set; } = new(StringComparer.Ordinal);

    public static bool IsDevice(DBObject obj, Transaction tr) => TryRead(obj, tr) is not null;

    /// <summary>Lê o registro; nulo se a entidade não for um dispositivo.</summary>
    public static DeviceRecord? TryRead(DBObject obj, Transaction tr)
    {
        if (obj.ExtensionDictionary.IsNull)
        {
            return null;
        }

        var dictionary = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(Key))
        {
            return null;
        }

        var xrecord = tr.GetObject(dictionary.GetAt(Key), OpenMode.ForRead) as Xrecord;
        var values = xrecord?.Data?.AsArray();
        if (values is null || values.Length < 7 || values[0].Value as string != Marker)
        {
            return null;
        }

        var json = new StringBuilder();
        foreach (var value in values.Skip(7).Where(v => v.TypeCode == JsonChunkCode))
        {
            json.Append((string)value.Value);
        }

        return new DeviceRecord
        {
            DefinitionId = (string)values[2].Value,
            DefinitionVersion = (string)values[3].Value,
            DefinitionHash = (string)values[4].Value,
            SourceFile = (string)values[5].Value,
            Name = (string)values[6].Value,
            Values = json.Length == 0 ? new Dictionary<string, object>(StringComparer.Ordinal) : ParameterCodec.Deserialize(json.ToString()),
        };
    }

    /// <summary>Grava (cria ou substitui) o registro. A entidade precisa estar aberta para escrita.</summary>
    public void Write(DBObject obj, Transaction tr)
    {
        if (obj.ExtensionDictionary.IsNull)
        {
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

        var data = new List<TypedValue>
        {
            new((int)DxfCode.Text, Marker),
            new((int)DxfCode.Int16, Schema),
            new((int)DxfCode.Text, DefinitionId),
            new((int)DxfCode.Text, DefinitionVersion),
            new((int)DxfCode.Text, DefinitionHash),
            new((int)DxfCode.Text, SourceFile),
            new((int)DxfCode.Text, Name),
        };
        var json = ParameterCodec.Serialize(Values);
        for (var i = 0; i < json.Length; i += ChunkSize)
        {
            data.Add(new TypedValue(JsonChunkCode, json.Substring(i, Math.Min(ChunkSize, json.Length - i))));
        }

        xrecord.Data = new ResultBuffer(data.ToArray());
    }
}
