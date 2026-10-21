using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Hydro.Civil3D.Infra;

internal enum LogLevel
{
    Info,
    Aviso,
    Erro,
    Tempo,
}

internal sealed record LogEntry(DateTime When, LogLevel Level, string Module, string Text)
{
    public string Label => Level switch
    {
        LogLevel.Aviso => "AVISO",
        LogLevel.Erro => "ERRO",
        LogLevel.Tempo => "TEMPO",
        _ => "INFO",
    };

    /// <summary>Linha para copiar ou gravar; as linhas seguintes de um texto longo vão indentadas.</summary>
    public string Line => $"{When:HH:mm:ss.fff} {Label,-5} [{Module}] {Text.Replace("\n", "\n                         ")}";
}

/// <summary>Log comum do HYDRO: memória (últimas entradas, para a janela de log do painel) e arquivo
/// diário em %LOCALAPPDATA%\HYDRO\logs. Registra também o início, o fim e o tempo dos
/// comandos HY_* e C3DHYDRO em todos os desenhos. Mesmo molde do PluginLog do Civil3D2026Plugin.</summary>
internal static class HydroLog
{
    private const int MaxEntries = 5000;
    private const int KeepDays = 14;
    private static readonly object Gate = new();
    private static readonly Queue<LogEntry> Entries = new();
    private static readonly Dictionary<Document, Stopwatch> Running = new();
    private static readonly HashSet<Document> Attached = new();
    private static StreamWriter? _file;
    private static string _day = "";
    private static bool _started;

    /// <summary>Nova entrada (pode vir de qualquer thread).</summary>
    public static event Action<LogEntry>? Added;

    public static string Folder => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "HYDRO", "logs");

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RemoveOld();
        Write(LogLevel.Info, "HYDRO", "Início da sessão. " + Environment());
        try
        {
            AcApp.DocumentManager.DocumentCreated += (_, e) => Attach(e.Document);
            foreach (Document document in AcApp.DocumentManager)
            {
                Attach(document);
            }
        }
        catch (System.Exception)
        {
            // sem gerenciador de documentos (carga sem interface): fica só o registro manual
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
        }

        _started = false;
    }

    public static void Info(string module, string text) => Write(LogLevel.Info, module, text);

    public static void Warn(string module, string text) => Write(LogLevel.Aviso, module, text);

    public static void Error(string module, string text, System.Exception? ex = null) =>
        Write(LogLevel.Erro, module, ex is null ? text : text + "\n" + Describe(ex));

    /// <summary>Mede uma etapa: ao descartar, registra o tempo gasto.</summary>
    public static IDisposable Measure(string module, string step)
    {
        var watch = Stopwatch.StartNew();
        return new Disposer(() => Write(LogLevel.Tempo, module, $"{step}: {watch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s"));
    }

    public static List<LogEntry> Snapshot()
    {
        lock (Gate)
        {
            return Entries.ToList();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }

    /// <summary>Texto para copiar e mandar: cabeçalho com o ambiente e as linhas.</summary>
    public static string Text(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== HYDRO - log ===");
        sb.AppendLine(Environment());
        sb.AppendLine();
        foreach (var entry in entries)
        {
            sb.AppendLine(entry.Line);
        }

        return sb.ToString();
    }

    internal static string Environment()
    {
        var parts = new List<string> { $"HYDRO {typeof(HydroLog).Assembly.GetName().Version}" };
        try
        {
            parts.Add($"AutoCAD {AcApp.GetSystemVariable("ACADVER")}");
            parts.Add($"Civil {typeof(Autodesk.Civil.ApplicationServices.CivilDocument).Assembly.GetName().Version}");
        }
        catch (System.Exception)
        {
            // fora do AutoCAD
        }

        parts.Add($".NET {System.Environment.Version}");
        return string.Join(" | ", parts);
    }

    private static string Describe(System.Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!ReferenceEquals(e, ex))
            {
                sb.Append("  -> ");
            }

            sb.Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);
        }

        sb.Append(string.Join("\n", (ex.StackTrace ?? "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(20)));
        return sb.ToString().TrimEnd();
    }

    private static void Write(LogLevel level, string module, string text)
    {
        var entry = new LogEntry(DateTime.Now, level, module, (text ?? "").TrimEnd());
        lock (Gate)
        {
            Entries.Enqueue(entry);
            while (Entries.Count > MaxEntries)
            {
                Entries.Dequeue();
            }

            WriteFile(entry);
        }

        try
        {
            Added?.Invoke(entry);
        }
        catch (System.Exception)
        {
            // quem escuta não derruba o log
        }
    }

    private static void WriteFile(LogEntry entry)
    {
        try
        {
            var day = entry.When.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (_file is null || day != _day)
            {
                _file?.Dispose();
                Directory.CreateDirectory(Folder);
                // um arquivo por dia e por sessão: duas sessões no mesmo arquivo se atropelariam
                var stream = new FileStream(Path.Combine(Folder, $"hydro-{day}-{System.Environment.ProcessId}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _day = day;
            }

            _file.WriteLine(entry.Line);
        }
        catch (System.Exception)
        {
            // sem disco: fica só na memória
            _file = null;
        }
    }

    private static void RemoveOld()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(Folder, "hydro-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (System.Exception)
        {
            // limpeza é conveniência
        }
    }

    private static bool IsOurs(string command) =>
        command.StartsWith("HY_", StringComparison.OrdinalIgnoreCase) || command.StartsWith("C3DHYDRO", StringComparison.OrdinalIgnoreCase);

    private static void Attach(Document? document)
    {
        if (document is null || !Attached.Add(document))
        {
            return;
        }

        document.CommandWillStart += (_, e) =>
        {
            if (!IsOurs(e.GlobalCommandName))
            {
                return;
            }

            lock (Gate)
            {
                Running[document] = Stopwatch.StartNew();
            }
        };
        document.CommandEnded += (_, e) => Finish(document, e.GlobalCommandName, "terminado");
        document.CommandCancelled += (_, e) => Finish(document, e.GlobalCommandName, "cancelado");
        document.CommandFailed += (_, e) => Finish(document, e.GlobalCommandName, "FALHOU");
        document.BeginDocumentClose += (_, _) =>
        {
            Attached.Remove(document);
            lock (Gate)
            {
                Running.Remove(document);
            }
        };
    }

    private static void Finish(Document document, string command, string how)
    {
        if (!IsOurs(command))
        {
            return;
        }

        Stopwatch? watch;
        lock (Gate)
        {
            Running.Remove(document, out watch);
        }

        var time = watch is null ? "" : $" ({watch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s)";
        Write(how == "FALHOU" ? LogLevel.Erro : LogLevel.Info, "Comandos", $"{command} {how}{time}.");
    }

    private sealed class Disposer : IDisposable
    {
        private Action? _action;

        public Disposer(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            var action = _action;
            _action = null;
            action?.Invoke();
        }
    }
}
