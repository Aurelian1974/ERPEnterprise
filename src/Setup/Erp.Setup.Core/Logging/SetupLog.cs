using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Erp.Setup.Core.Logging;

/// <summary>
/// Jurnalul installer-ului (6.6): text UTC + copie CLEF pentru diagnose.
/// Inainte de a exista DataRoot, jurnalul se scrie in %TEMP%\ErpSetup\.
/// Niciun mesaj nu are voie sa contina secrete (R6).
/// </summary>
public static class SetupLog
{
    public const string TextTemplate =
        "{UtcTimestamp:yyyy-MM-dd HH:mm:ss.fff}Z [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static string DefaultTempDirectory => Path.Combine(Path.GetTempPath(), "ErpSetup");

    public static SetupLogFiles CreateFiles(string directory, DateTimeOffset utcNow)
    {
        var stamp = utcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = Path.Combine(directory, $"setup-{stamp}");
        return new SetupLogFiles(baseName + ".log", baseName + ".clef");
    }

    public static Logger Create(SetupLogFiles files, LogEventLevel minimumLevel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(files.TextPath)!);
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.With<UtcTimestampEnricher>()
            .WriteTo.File(files.TextPath, outputTemplate: TextTemplate, encoding: new System.Text.UTF8Encoding(false))
            .WriteTo.File(new CompactJsonFormatter(), files.ClefPath)
            .CreateLogger();
    }

    public static LogEventLevel ParseLevel(string value) => value switch
    {
        "Debug" => LogEventLevel.Debug,
        _ => LogEventLevel.Information,
    };

    private sealed class UtcTimestampEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UtcTimestamp", logEvent.Timestamp.UtcDateTime));
    }
}

public sealed record SetupLogFiles(string TextPath, string ClefPath);
