using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Modeler.Civil3D;

/// <summary>DLL única: <c>Modeler.Core.dll</c> e <c>YamlDotNet.dll</c> vêm embutidos como recursos
/// <c>Embedded.&lt;nome&gt;.dll</c> (alvo EmbedDependencies do csproj). O inicializador do módulo roda
/// antes de qualquer outro código desta DLL e registra a resolução, que carrega o recurso no mesmo
/// contexto de carga do plugin. A resolução só roda quando o .NET não acha a DLL: uma DLL avulsa
/// antiga que tenha sobrado ao lado do plugin poderia ser carregada antes. Por isso o csproj apaga
/// esses restos da saída e o Install-Bundle limpa a pasta Contents antes de copiar.</summary>
internal static class EmbeddedAssemblies
{
    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static AssemblyLoadContext? _context;

    // CA2255: o plugin é o "aplicativo" aqui; o AutoCAD não dá outro ponto que rode antes do
    // primeiro uso de um tipo do Core.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        var self = typeof(EmbeddedAssemblies).Assembly;
        _context = AssemblyLoadContext.GetLoadContext(self) ?? AssemblyLoadContext.Default;
        _context.Resolving += (context, name) => Resolve(name);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(new AssemblyName(args.Name));
    }

    /// <summary>Nomes das dependências embutidas (para o DM_INFO/diagnóstico).</summary>
    public static IEnumerable<string> Names
    {
        get
        {
            foreach (var resource in typeof(EmbeddedAssemblies).Assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith("Embedded.", StringComparison.Ordinal))
                {
                    yield return resource["Embedded.".Length..];
                }
            }
        }
    }

    private static Assembly? Resolve(AssemblyName name)
    {
        if (name.Name is not ("Modeler.Core" or "YamlDotNet"))
        {
            return null;
        }

        lock (Gate)
        {
            if (Loaded.TryGetValue(name.Name, out var cached))
            {
                return cached;
            }

            var self = typeof(EmbeddedAssemblies).Assembly;
            var context = _context ?? AssemblyLoadContext.Default;
            using var stream = self.GetManifestResourceStream($"Embedded.{name.Name}.dll");
            var assembly = stream is null ? null : context.LoadFromStream(stream);

            if (assembly is not null)
            {
                Loaded[name.Name] = assembly;
            }

            return assembly;
        }
    }
}
