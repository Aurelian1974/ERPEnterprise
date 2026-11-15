using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Setup.Core.Deployment;

/// <summary>Valorile pe care le controleaza installer-ul in appsettings.Installation.json (8.10). Fara secrete.</summary>
public sealed record InstallationSettingsValues(
    string Instance,
    string Version,
    string PublicOrigin,
    string InstanceDirectory,
    string SqlServer,
    string DatabaseName,
    string Authentication,
    string? AppLogin,
    string Encrypt,
    bool TrustServerCertificate);

/// <summary>
/// appsettings.Installation.json (8.10, I-16; U-11): sablonul din kit, peste care se pun valorile existente (upgrade: cheile
/// noi primesc valorile implicite din sablon), apoi valorile controlate de installer (caile, versiunea, baza).
/// </summary>
public static class AppSettingsGenerator
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Generate(string templateJson, string? existingJson, InstallationSettingsValues values)
    {
        var result = ParseObject(templateJson, "șablonul appsettings.Installation.json");
        if (existingJson is not null)
        {
            Merge(result, ParseObject(existingJson, "appsettings.Installation.json existent"));
        }

        var dir = values.InstanceDirectory;
        Merge(result, new JsonObject
        {
            ["Erp"] = new JsonObject
            {
                ["Instance"] = values.Instance,
                ["Version"] = values.Version,
                ["PublicOrigin"] = values.PublicOrigin,
                ["DataRoot"] = dir,
                ["LicensePath"] = Join(dir, @"license\erp.lic"),
                ["MaintenanceFlagPath"] = Join(dir, @"maintenance\maintenance.flag"),
                ["DataProtectionKeysPath"] = Join(dir, "keys"),
            },
            ["Database"] = new JsonObject
            {
                ["Server"] = values.SqlServer,
                ["Name"] = values.DatabaseName,
                ["Authentication"] = values.Authentication,
                ["UserId"] = values.AppLogin,
                ["Encrypt"] = values.Encrypt,
                ["TrustServerCertificate"] = values.TrustServerCertificate,
            },
            ["Logging"] = new JsonObject
            {
                ["Path"] = Join(dir, "logs"),
            },
        });

        return result.ToJsonString(Indented);
    }

    /// <summary>PublicOrigin: https://gazda, cu portul doar daca nu e 443.</summary>
    public static string PublicOrigin(string hostName, int httpsPort) =>
        httpsPort == 443 ? $"https://{hostName}" : $"https://{hostName}:{httpsPort}";

    // Caile sunt Windows, indiferent de sistemul pe care ruleaza testele.
    private static string Join(string directory, string relative) => directory.TrimEnd('\\') + '\\' + relative;

    private static JsonObject ParseObject(string json, string what) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject
        ?? throw new InvalidOperationException($"{what} nu este un obiect JSON.");

    // Obiectele se combina recursiv; valorile simple si listele din „overlay” le inlocuiesc pe cele existente.
    private static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay)
        {
            if (value is JsonObject child && target[key] is JsonObject existing)
            {
                Merge(existing, child);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
