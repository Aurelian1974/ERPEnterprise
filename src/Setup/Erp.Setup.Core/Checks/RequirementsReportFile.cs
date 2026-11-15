using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Setup.Core.Checks;

/// <summary>Salvarea rezultatului ca logs\setup\check-&lt;data&gt;.json (7); fara secrete.</summary>
public static class RequirementsReportFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(string directory, RequirementsReport report, CheckTarget target, DateTimeOffset utcNow)
    {
        Directory.CreateDirectory(directory);
        var stamp = utcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"check-{stamp}.json");
        var document = new
        {
            createdAt = utcNow.UtcDateTime,
            mode = target.Mode,
            instance = target.Instance,
            sqlServer = target.Sql.Server,
            databaseName = target.Sql.DatabaseName,
            exitCode = report.ExitCode,
            results = report.Results,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        return path;
    }
}
