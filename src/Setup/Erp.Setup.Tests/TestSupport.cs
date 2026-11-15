using Serilog.Core;
using Serilog.Events;

namespace Erp.Setup.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Erp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Rădăcina depozitului nu a fost găsită.");
    }
}

/// <summary>Colecteaza jurnalul ca text complet (mesaje, proprietati, exceptii) pentru verificari de tip „fara secrete”.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly List<string> lines = [];

    public string Text
    {
        get
        {
            lock (lines)
            {
                return string.Join('\n', lines);
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var line = $"{logEvent.RenderMessage()} {string.Join(' ', logEvent.Properties.Select(p => p.Value.ToString()))} {logEvent.Exception}";
        lock (lines)
        {
            lines.Add(line);
        }
    }
}
