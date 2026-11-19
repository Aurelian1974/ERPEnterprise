using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Erp.Setup.Core.Answers;

/// <summary>Rezultatul citirii fisierului de raspunsuri: cererea (daca e valid), erorile si avertismentele pe camp.</summary>
public sealed record AnswersLoadResult(InstallRequest? Request, IReadOnlyList<AnswersIssue> Issues)
{
    public bool IsValid => Request is not null && Errors.Count == 0;

    public IReadOnlyList<AnswersIssue> Errors => [.. Issues.Where(i => i.Severity == IssueSeverity.Error)];

    public IReadOnlyList<AnswersIssue> Warnings => [.. Issues.Where(i => i.Severity == IssueSeverity.Warning)];
}

/// <summary>
/// Validarea fisierului de raspunsuri (08, 6.2): aceleasi reguli ca ecranele (08, 5.3). Orice camp invalid opreste cu codul 11
/// si numele campului; campurile lipsa primesc valorile implicite.
/// </summary>
public static partial class AnswersValidator
{
    public static AnswersLoadResult Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AnswersLoadResult(null, [new AnswersIssue("(fișier)", $"Fișierul de răspunsuri nu există: {path}")]);
        }

        try
        {
            return Validate(AnswersFile.Parse(File.ReadAllText(path)));
        }
        catch (JsonException ex)
        {
            return new AnswersLoadResult(null, [new AnswersIssue("(fișier)", $"Fișierul de răspunsuri nu este JSON valid: {ex.Message}")]);
        }
    }

    public static AnswersLoadResult Validate(AnswersFile file, string? dataRoot = null)
    {
        var issues = new List<AnswersIssue>();
        void Error(string field, string message) => issues.Add(new AnswersIssue(field, message));

        if (file.FormatVersion != AnswersFile.CurrentFormatVersion)
        {
            Error("formatVersion", file.FormatVersion is null
                ? "Lipsește versiunea formatului (formatVersion: 1)."
                : $"Versiunea formatului {file.FormatVersion} nu este cunoscută de acest installer (așteptat 1).");
        }

        var instance = file.Instance ?? InstallRequest.DefaultInstance;
        if (!InstancePattern().IsMatch(instance))
        {
            Error("instance", "Numele instalării are 2–10 caractere: o majusculă, apoi majuscule sau cifre (de exemplu PROD, TEST).");
        }

        var sqlSection = file.Sql ?? new AnswersFile.SqlSection();
        var server = sqlSection.Server?.Trim();
        if (string.IsNullOrEmpty(server))
        {
            Error("sql.server", "Serverul SQL este obligatoriu.");
        }
        else if (!SqlServerPattern().IsMatch(server))
        {
            Error("sql.server", "Serverul SQL are forma server, server\\instanță sau server,port.");
        }

        var database = sqlSection.Database?.Trim() ?? string.Empty;
        if (!DatabasePattern().IsMatch(database))
        {
            Error("sql.database", "Numele bazei are 3–64 caractere: litere, cifre și „_”, începe cu o literă.");
        }

        var dataFolder = OptionalPath(sqlSection.DataFolder, "sql.dataFolder", issues);
        var logFolder = OptionalPath(sqlSection.LogFolder, "sql.logFolder", issues);
        var identity = ParseEnum(sqlSection.AppIdentity, AppIdentity.VirtualAccounts, "sql.appIdentity", issues);
        var gmsa = sqlSection.GmsaAccount?.Trim();
        if (identity == AppIdentity.Gmsa)
        {
            if (gmsa is null || !GmsaPattern().IsMatch(gmsa))
            {
                Error("sql.gmsaAccount", "Contul gMSA are forma DOMENIU\\nume$ (numele are cel mult 15 caractere).");
            }
        }
        else
        {
            gmsa = null;
        }

        var backupSection = file.Backup ?? new AnswersFile.BackupSection();
        var backupFolder = OptionalPath(backupSection.Folder, "backup.folder", issues);
        var fullBackup = ParseTime(backupSection.FullBackupTime, new TimeOnly(1, 0), "backup.fullBackupTime", issues);
        var logEvery = backupSection.LogBackupEveryMinutes ?? 15;
        if (logEvery is < 5 or > 60)
        {
            Error("backup.logBackupEveryMinutes", "Backup-ul de log se face la 5–60 de minute.");
        }

        var retention = backupSection.RetentionDays ?? 14;
        if (retention is < 3 or > 90)
        {
            Error("backup.retentionDays", "Backup-urile se păstrează 3–90 de zile.");
        }

        var checkDay = ParseEnum(backupSection.IntegrityCheckDay, DayOfWeek.Saturday, "backup.integrityCheckDay", issues);
        var checkTime = ParseTime(backupSection.IntegrityCheckTime, new TimeOnly(2, 0), "backup.integrityCheckTime", issues);

        var siteSection = file.Site ?? new AnswersFile.SiteSection();
        var hostName = siteSection.HostName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!HostNamePattern().IsMatch(hostName))
        {
            Error("site.hostName", "Adresa site-ului este obligatorie și trebuie să fie un nume DNS (de exemplu srv-erp.firma.local).");
        }

        var port = siteSection.HttpsPort ?? 443;
        if (port is < 1 or > 65535)
        {
            Error("site.httpsPort", "Portul HTTPS este între 1 și 65535.");
        }

        var certificate = ParseEnum(siteSection.Certificate?.Kind, CertificateKind.Internal, "site.certificate.kind", issues);
        var thumbprint = siteSection.Certificate?.Thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (certificate == CertificateKind.Existing && (thumbprint is null || !ThumbprintPattern().IsMatch(thumbprint)))
        {
            Error("site.certificate.thumbprint", "Pentru un certificat existent, amprenta are 40 de caractere hexazecimale.");
        }

        var installRoot = OptionalPath(siteSection.InstallRoot, "site.installRoot", issues) ?? InstallRequest.DefaultInstallRoot;
        var license = file.License?.File?.Trim();
        if (string.IsNullOrEmpty(license))
        {
            Error("license.file", "Fișierul de licență este obligatoriu.");
        }

        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var warning in file.AcceptedWarnings ?? [])
        {
            if (CheckIdPattern().IsMatch(warning))
            {
                accepted.Add(warning);
            }
            else
            {
                Error("acceptedWarnings", $"„{warning}” nu este un ID de verificare (de exemplu C-11).");
            }
        }

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new AnswersLoadResult(null, issues);
        }

        var request = new InstallRequest(
            instance,
            installRoot,
            new SqlRequest(server!, database, dataFolder, logFolder, identity, gmsa),
            new SiteRequest(hostName, port, siteSection.HttpRedirect ?? true, certificate, certificate == CertificateKind.Existing ? thumbprint : null),
            new BackupRequest(backupFolder, fullBackup, logEvery, retention, checkDay, checkTime),
            license!,
            accepted);
        return new AnswersLoadResult(dataRoot is null ? request : request with { DataRoot = dataRoot }, issues);
    }

    /// <summary>Cale absoluta Windows (C:\… sau \\server\share\…), fara caractere care ar strica textul SQL (08, 5.6).</summary>
    public static bool IsSafeAbsolutePath(string path) =>
        AbsolutePathPattern().IsMatch(path) && !path.Contains('\'', StringComparison.Ordinal) && !path.Contains(';', StringComparison.Ordinal)
        && !path.Contains("--", StringComparison.Ordinal) && !path.Any(char.IsControl);

    private static string? OptionalPath(string? value, string field, List<AnswersIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Trim().TrimEnd('\\');
        if (!IsSafeAbsolutePath(path))
        {
            issues.Add(new AnswersIssue(field, "Calea trebuie să fie absolută (de exemplu D:\\SQLData) și fără apostrof, „;” sau „--”."));
            return null;
        }

        return path;
    }

    private static T ParseEnum<T>(string? value, T fallback, string field, List<AnswersIssue> issues)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
        {
            return parsed;
        }

        issues.Add(new AnswersIssue(field, $"Valoarea „{value}” nu este permisă; valori: {string.Join(", ", Enum.GetNames<T>())}."));
        return fallback;
    }

    private static TimeOnly ParseTime(string? value, TimeOnly fallback, string field, List<AnswersIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        issues.Add(new AnswersIssue(field, "Ora are forma HH:mm (de exemplu 01:00)."));
        return fallback;
    }

    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabasePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{0,252}(\\[A-Za-z0-9_$\-]{1,16})?(,\d{1,5})?$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlServerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{0,63}\\[A-Za-z0-9._\-]{1,15}\$$", RegexOptions.CultureInvariant)]
    private static partial Regex GmsaPattern();

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HostNamePattern();

    [GeneratedRegex(@"^[0-9A-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbprintPattern();

    [GeneratedRegex(@"^C-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CheckIdPattern();

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\[^\\/:*?""<>|]+\\[^\\/:*?""<>|]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathPattern();
}
