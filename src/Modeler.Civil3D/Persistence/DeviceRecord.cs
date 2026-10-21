using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Core.Persistence;

namespace Modeler.Civil3D.Persistence;

/// <summary>Dados de uma instância gravados no DWG, no dicionário de extensão da entidade
/// hospedeira (BlockReference):
/// <code>
/// ExtensionDictionary
///   └─ "DEVICEMODELER" (Xrecord)
///        (1)  "DeviceModeler"      marcador
///        (70) versão do esquema    1 ou 2
///        (1)  id da definição      ex.: pv_retangular
///        (1)  versão da definição  ex.: 1.0.0
///        (1)  hash SHA-256 do YAML usado
///        (1)  caminho do YAML usado
///        (1)  nome da instância    ex.: PV-01
///        (300 ...) JSON dos parâmetros, em pedaços de até 250 caracteres
///        (301 ...) esquema 2: JSON dos metadados (id da instância, superfície seguida, número, rede,
///                  estrutura nula da Pipe Network, modo das pontas)
/// </code>
/// O esquema 1 (sem metadados) continua legível; ele vira 2 na próxima gravação (DM_AUDITAR migra
/// tudo de uma vez). Sem o plugin o desenho continua válido: a geometria é um bloco anônimo comum.</summary>
internal sealed class DeviceRecord
{
    public const string Key = "DEVICEMODELER";
    public const short CurrentSchema = 2;
    private const string Marker = "DeviceModeler";
    private const int ChunkSize = 250;

    /// <summary>Código 300 (texto arbitrário) para os pedaços do JSON dos parâmetros.</summary>
    private const short JsonChunkCode = 300;

    /// <summary>Código 301 para os pedaços do JSON dos metadados (esquema 2).</summary>
    private const short MetaChunkCode = 301;

    public short Schema { get; private set; } = CurrentSchema;

    public string DefinitionId { get; set; } = "";

    public string DefinitionVersion { get; set; } = "";

    public string DefinitionHash { get; set; } = "";

    public string SourceFile { get; set; } = "";

    public string Name { get; set; } = "";

    public Dictionary<string, object> Values { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Identificador estável da instância (GUID); uma cópia ganha outro.</summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Superfície seguida (handle) e a impressão digital dela na última aplicação.</summary>
    public string SurfaceHandle { get; set; } = "";

    public string SurfaceFingerprint { get; set; } = "";

    /// <summary>Refaz a elevação quando a superfície muda (DM_SUPERFICIE liga).</summary>
    public bool FollowSurface { get; set; }

    /// <summary>Número na rede (numeração, F3); 0 = sem número.</summary>
    public int Number { get; set; }

    /// <summary>Nome da rede a que o dispositivo pertence (F3); vazio = nenhuma.</summary>
    public string Network { get; set; } = "";

    /// <summary>Estrutura nula da Pipe Network sincronizada com o dispositivo (handle); vazio = nenhuma.</summary>
    public string StructureHandle { get; set; } = "";

    /// <summary>Modo de recálculo das pontas dos tubos ligados (<c>ajustar_corrigir</c>...); vazio = o padrão.</summary>
    public string EndMode { get; set; } = "";

    /// <summary>Estilo de dispositivo (<c>HYDRO_ESTILOS</c>); vazio = "Padrão".</summary>
    public string Style { get; set; } = "";

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
        var meta = new StringBuilder();
        foreach (var value in values.Skip(7))
        {
            if (value.TypeCode == JsonChunkCode)
            {
                json.Append((string)value.Value);
            }
            else if (value.TypeCode == MetaChunkCode)
            {
                meta.Append((string)value.Value);
            }
        }

        var record = new DeviceRecord
        {
            Schema = Convert.ToInt16(values[1].Value),
            DefinitionId = (string)values[2].Value,
            DefinitionVersion = (string)values[3].Value,
            DefinitionHash = (string)values[4].Value,
            SourceFile = (string)values[5].Value,
            Name = (string)values[6].Value,
            Values = json.Length == 0 ? new Dictionary<string, object>(StringComparer.Ordinal) : ParameterCodec.Deserialize(json.ToString()),
        };
        if (meta.Length > 0)
        {
            record.ReadMeta(meta.ToString());
        }
        else
        {
            // esquema 1: id derivado do handle (estável até a migração)
            record.InstanceId = "h" + obj.Handle;
        }

        return record;
    }

    /// <summary>Grava (cria ou substitui) o registro no esquema atual. A entidade precisa estar aberta para escrita.</summary>
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

        Schema = CurrentSchema;
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
        AddChunks(data, JsonChunkCode, ParameterCodec.Serialize(Values));
        AddChunks(data, MetaChunkCode, WriteMeta());
        xrecord.Data = new ResultBuffer(data.ToArray());
        DrawingVersion.Stamp(obj.Database, tr);
    }

    private static void AddChunks(List<TypedValue> data, short code, string text)
    {
        for (var i = 0; i < text.Length; i += ChunkSize)
        {
            data.Add(new TypedValue(code, text.Substring(i, Math.Min(ChunkSize, text.Length - i))));
        }
    }

    private string WriteMeta() => new JsonObject
    {
        ["id"] = InstanceId,
        ["superficie"] = SurfaceHandle,
        ["superficie_fp"] = SurfaceFingerprint,
        ["seguir_superficie"] = FollowSurface,
        ["numero"] = Number,
        ["rede"] = Network,
        ["estrutura"] = StructureHandle,
        ["modo_ponta"] = EndMode,
        ["estilo"] = Style,
    }.ToJsonString();

    private void ReadMeta(string json)
    {
        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null)
            {
                return;
            }

            InstanceId = node["id"]?.GetValue<string>() ?? InstanceId;
            SurfaceHandle = node["superficie"]?.GetValue<string>() ?? "";
            SurfaceFingerprint = node["superficie_fp"]?.GetValue<string>() ?? "";
            FollowSurface = node["seguir_superficie"]?.GetValue<bool>() ?? false;
            Number = node["numero"]?.GetValue<int>() ?? 0;
            Network = node["rede"]?.GetValue<string>() ?? "";
            StructureHandle = node["estrutura"]?.GetValue<string>() ?? "";
            EndMode = node["modo_ponta"]?.GetValue<string>() ?? "";
            Style = node["estilo"]?.GetValue<string>() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // metadados corrompidos: ficam os padrões (DM_AUDITAR acusa)
        }
    }
}

/// <summary>Versão do HYDRO e do esquema no dicionário de objetos nomeados do desenho
/// (<c>DEVICEMODELER_VERSAO</c>): mostra com que versão o DWG foi gravado e permite migrar.</summary>
internal static class DrawingVersion
{
    public const string Key = "DEVICEMODELER_VERSAO";

    public static void Stamp(Database? db, Transaction tr)
    {
        if (db is null)
        {
            return;
        }

        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        var version = typeof(DrawingVersion).Assembly.GetName().Version?.ToString() ?? "";
        if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord existing &&
            existing.Data?.AsArray() is { Length: >= 2 } current && current[0].Value as string == version)
        {
            return;
        }

        Xrecord xrecord;
        if (nod.Contains(Key))
        {
            xrecord = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        }
        else
        {
            nod.UpgradeOpen();
            xrecord = new Xrecord();
            nod.SetAt(Key, xrecord);
            tr.AddNewlyCreatedDBObject(xrecord, true);
        }

        xrecord.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, version), new TypedValue((int)DxfCode.Int16, DeviceRecord.CurrentSchema));
    }

    /// <summary>Versão do plugin e esquema gravados no desenho; nulo se nunca gravado.</summary>
    public static (string Version, short Schema)? Read(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Key) || tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is not Xrecord xrecord ||
            xrecord.Data?.AsArray() is not { Length: >= 2 } values)
        {
            return null;
        }

        return (values[0].Value as string ?? "", Convert.ToInt16(values[1].Value));
    }
}
