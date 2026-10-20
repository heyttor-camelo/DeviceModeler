using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Modeler.Core.Definition;
using Modeler.Core.Errors;

namespace Modeler.Civil3D.Catalog;

/// <summary>Pasta de catálogo (arquivos .yaml) e cache das definições já lidas.
/// A pasta padrão é <c>catalog</c> ao lado da DLL; pode ser trocada com DM_CATALOGO
/// (gravada em %APPDATA%\DeviceModeler\settings.json).</summary>
internal static class DeviceCatalog
{
    private static readonly Dictionary<string, (DateTime Stamp, DeviceDefinition Definition)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeviceModeler", "settings.json");

    public static string DefaultFolder =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "catalog");

    public static string Folder
    {
        get
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile));
                    if (!string.IsNullOrWhiteSpace(settings?.CatalogFolder) && Directory.Exists(settings.CatalogFolder))
                    {
                        return settings.CatalogFolder;
                    }
                }
            }
            catch (Exception)
            {
                // configuração corrompida: volta ao padrão
            }

            return DefaultFolder;
        }

        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Settings { CatalogFolder = value }));
            Cache.Clear();
        }
    }

    /// <summary>Todas as definições válidas da pasta (e subpastas). Arquivos com erro vão para
    /// <paramref name="errors"/> em vez de interromper.</summary>
    public static List<DeviceDefinition> LoadAll(out List<string> errors)
    {
        errors = new List<string>();
        var result = new List<DeviceDefinition>();
        if (!Directory.Exists(Folder))
        {
            errors.Add($"pasta de catálogo não encontrada: {Folder}");
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(Folder, "*.yaml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Folder, "*.yml", SearchOption.AllDirectories))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                result.Add(LoadFile(file));
            }
            catch (ModelerException ex)
            {
                errors.Add(ex.Message);
            }
            catch (IOException ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        var duplicated = result.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        foreach (var group in duplicated)
        {
            errors.Add($"dispositivo '{group.Key}' definido em mais de um arquivo: {string.Join(", ", group.Select(d => d.SourceFile))} (vale o primeiro)");
        }

        return result.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    /// <summary>Lê (com cache por data de modificação) uma definição.</summary>
    public static DeviceDefinition LoadFile(string path)
    {
        var full = Path.GetFullPath(path);
        var stamp = File.GetLastWriteTimeUtc(full);
        if (Cache.TryGetValue(full, out var cached) && cached.Stamp == stamp)
        {
            return cached.Definition;
        }

        var definition = DeviceDefinition.Load(full);
        Cache[full] = (stamp, definition);
        return definition;
    }

    /// <summary>Acha a definição de uma instância: primeiro o arquivo gravado (se ainda existir e
    /// tiver o mesmo id), depois qualquer arquivo do catálogo com esse id.</summary>
    public static DeviceDefinition? Find(string id, string? recordedFile)
    {
        if (!string.IsNullOrEmpty(recordedFile) && File.Exists(recordedFile))
        {
            try
            {
                var definition = LoadFile(recordedFile);
                if (string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }
            catch (ModelerException)
            {
                // cai para a busca no catálogo
            }
        }

        return LoadAll(out _).FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Settings
    {
        public string? CatalogFolder { get; set; }
    }
}
