using Serilog.Core;
using Serilog.Events;

namespace Erp.Setup.Tests;

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
