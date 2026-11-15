using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Erp.Setup.Core.Answers;

/// <summary>Citirea si validarea completa a fisierului de raspunsuri (6.2). Fisierul nu contine niciodata parole.</summary>
public static partial class AnswersValidator
{
    public const string DefaultInstance = "PROD";
    public const string DefaultInstallRoot = @"C:\Program Files\Erp";
    public const string DefaultDataRoot = @"C:\ProgramData\Erp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AnswersValidationResult Load(string path, Func<string, bool>? fileExists = null)
    {
        if (!File.Exists(path))
        {
            return AnswersValidationResult.Invalid(new AnswersIssue("--answers", $"Fișierul de răspunsuri nu există: {path}"));
        }

        return Parse(File.ReadAllText(path), fileExists ?? File.Exists);
    }

    public static AnswersValidationResult Parse(string json, Func<string, bool> fileExists)
    {
        AnswersFile? file;
        try
        {
            file = JsonSerializer.Deserialize<AnswersFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var field = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? "(fișier)" : ex.Path.TrimStart('$', '.');
            var message = ex.Message.Contains("could not be mapped", StringComparison.Ordinal)
                ? "Câmp necunoscut. Fișierul de răspunsuri nu acceptă câmpuri în plus și nu conține parole."
                : $"JSON invalid sau valoare de tip greșit (linia {ex.LineNumber + 1}).";
            return AnswersValidationResult.Invalid(new AnswersIssue(field, message));
        }

        return Validate(file ?? new AnswersFile(), fileExists);
    }

    public static AnswersValidationResult Validate(AnswersFile file, Func<string, bool> fileExists)
    {
        var issues = new List<AnswersIssue>();
        void Error(string field, string message) => issues.Add(new AnswersIssue(field, message));
        void Warn(string field, string message) => issues.Add(new AnswersIssue(field, message, IssueSeverity.Warning));

        var instance = file.Instance ?? DefaultInstance;
        if (!InstanceName().IsMatch(instance))
        {
            Error("instance", "Numele instanței are 2–10 caractere, majuscule și cifre, începând cu o literă (de exemplu PROD).");
        }

        var installRoot = file.InstallRoot ?? DefaultInstallRoot;
        ValidateLocalRoot("installRoot", installRoot, Error);
        var dataRoot = file.DataRoot ?? DefaultDataRoot;
        ValidateLocalRoot("dataRoot", dataRoot, Error);

        // site
        var site = file.Site ?? new AnswersFile.SiteSection();
        if (string.IsNullOrWhiteSpace(site.HostName))
        {
            Error("site.hostName", "Câmpul este obligatoriu.");
        }
        else if (!IsDnsName(site.HostName))
        {
            Error("site.hostName", $"„{site.HostName}” nu este un nume DNS valid.");
        }

        var httpsPort = site.HttpsPort ?? 443;
        if (httpsPort is < 1 or > 65535)
        {
            Error("site.httpsPort", "Portul trebuie să fie între 1 și 65535.");
        }

        var allowSelfSigned = site.AllowSelfSignedCertificate ?? false;
        string? thumbprint = null;
        if (!string.IsNullOrWhiteSpace(site.CertificateThumbprint))
        {
            thumbprint = WhiteSpace().Replace(site.CertificateThumbprint, string.Empty).ToUpperInvariant();
            if (!Thumbprint().IsMatch(thumbprint))
            {
                Error("site.certificateThumbprint", "Amprenta certificatului are 40 de caractere hexazecimale.");
            }
        }
        else if (!allowSelfSigned)
        {
            Error("site.certificateThumbprint", "Câmpul este obligatoriu când allowSelfSignedCertificate este false.");
        }

        if (allowSelfSigned && thumbprint is null)
        {
            Warn("site.allowSelfSignedCertificate", "Se va genera un certificat autosemnat, valabil 1 an; folosiți-l doar pentru test.");
        }

        // sql
        var sql = file.Sql ?? new AnswersFile.SqlSection();
        if (string.IsNullOrWhiteSpace(sql.Server))
        {
            Error("sql.server", "Câmpul este obligatoriu.");
        }
        else if (!IsSqlServerName(sql.Server))
        {
            Error("sql.server", "Folosiți forma server, server\\instanță sau server,port.");
        }

        var databaseName = sql.DatabaseName ?? $"ERP_{instance}";
        if (!DatabaseName().IsMatch(databaseName))
        {
            Error("sql.databaseName", "Numele bazei începe cu o literă și are 3–64 de caractere: litere, cifre și _.");
        }

        var operatorAuth = ParseAuthentication("sql.operatorAuthentication", sql.OperatorAuthentication, Error);
        if (operatorAuth == SqlAuthentication.Sql && string.IsNullOrWhiteSpace(sql.OperatorSqlLogin))
        {
            Error("sql.operatorSqlLogin", "Câmpul este obligatoriu când operatorAuthentication este Sql.");
        }

        var appAuth = ParseAuthentication("sql.appAuthentication", sql.AppAuthentication, Error);
        if (appAuth == SqlAuthentication.Windows)
        {
            if (string.IsNullOrWhiteSpace(sql.AppWindowsAccount))
            {
                Error("sql.appWindowsAccount", "Câmpul este obligatoriu când appAuthentication este Windows.");
            }
            else if (!WindowsAccount().IsMatch(sql.AppWindowsAccount))
            {
                Error("sql.appWindowsAccount", "Folosiți forma DOMENIU\\cont sau DOMENIU\\gmsa$.");
            }
        }

        if (sql.DataFilePath is not null)
        {
            ValidateSqlPath("sql.dataFilePath", sql.DataFilePath, Error);
        }

        if (sql.LogFilePath is not null)
        {
            ValidateSqlPath("sql.logFilePath", sql.LogFilePath, Error);
        }

        var dataSize = sql.InitialDataSizeMb ?? 1024;
        if (dataSize < 256)
        {
            Error("sql.initialDataSizeMb", "Valoarea minimă este 256.");
        }

        var logSize = sql.InitialLogSizeMb ?? 512;
        if (logSize < 256)
        {
            Error("sql.initialLogSizeMb", "Valoarea minimă este 256.");
        }

        var encrypt = SqlEncrypt.Mandatory;
        if (sql.Encrypt is not null && !TryParseExact(sql.Encrypt, out encrypt))
        {
            Error("sql.encrypt", "Valorile permise sunt Mandatory, Strict și Optional.");
        }

        var trustServerCertificate = sql.TrustServerCertificate ?? false;
        if (trustServerCertificate)
        {
            Warn("sql.trustServerCertificate", "Certificatul SQL Server nu va fi validat; folosiți doar dacă serverul nu are un certificat de încredere.");
        }

        // backup
        var backup = file.Backup ?? new AnswersFile.BackupSection();
        if (string.IsNullOrWhiteSpace(backup.BackupRoot))
        {
            Error("backup.backupRoot", "Câmpul este obligatoriu.");
        }
        else if (ValidateSqlPath("backup.backupRoot", backup.BackupRoot, Error)
            && sql.DataFilePath is not null && SameDrive(backup.BackupRoot, sql.DataFilePath))
        {
            Warn("backup.backupRoot", "Backup-urile sunt pe același disc cu datele; de preferat un disc separat.");
        }

        var fullBackupTime = ParseTime("backup.fullBackupTime", backup.FullBackupTime, new TimeOnly(1, 0), Error);
        var logEvery = backup.LogBackupEveryMinutes ?? 15;
        if (logEvery is < 5 or > 60)
        {
            Error("backup.logBackupEveryMinutes", "Valoarea trebuie să fie între 5 și 60 de minute.");
        }

        var retention = backup.RetentionDays ?? 14;
        if (retention is < 3 or > 90)
        {
            Error("backup.retentionDays", "Valoarea trebuie să fie între 3 și 90 de zile.");
        }

        var integrityDay = DayOfWeek.Sunday;
        if (backup.IntegrityCheckDay is not null && !TryParseExact(backup.IntegrityCheckDay, out integrityDay))
        {
            Error("backup.integrityCheckDay", "Folosiți numele zilei în engleză: Monday … Sunday.");
        }

        var integrityTime = ParseTime("backup.integrityCheckTime", backup.IntegrityCheckTime, new TimeOnly(3, 0), Error);

        // license
        var licensePath = file.License?.FilePath;
        if (string.IsNullOrWhiteSpace(licensePath))
        {
            Error("license.filePath", "Câmpul este obligatoriu.");
        }
        else if (!fileExists(licensePath))
        {
            Error("license.filePath", $"Fișierul de licență nu există: {licensePath}");
        }

        // administrator
        var admin = file.Administrator ?? new AnswersFile.AdministratorSection();
        var userName = admin.UserName ?? "administrator";
        if (!AdminUserName().IsMatch(userName))
        {
            Error("administrator.userName", "Numele de utilizator are 3–50 de caractere, doar litere mici, cifre, . _ -, începând cu o literă.");
        }

        var displayName = admin.DisplayName ?? "Administrator ERP";
        if (displayName.Length is < 1 or > 200)
        {
            Error("administrator.displayName", "Numele afișat are 1–200 de caractere.");
        }

        if (admin.Email is not null && !IsEmail(admin.Email))
        {
            Error("administrator.email", $"„{admin.Email}” nu este o adresă de email validă.");
        }

        var prerequisites = file.Prerequisites ?? new AnswersFile.PrerequisitesSection();

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new AnswersValidationResult(null, issues);
        }

        return new AnswersValidationResult(
            new InstallAnswers(
                instance,
                installRoot,
                dataRoot,
                new SiteAnswers(site.HostName!, httpsPort, site.HttpRedirect ?? true, thumbprint, allowSelfSigned),
                new SqlAnswers(sql.Server!, databaseName, operatorAuth, sql.OperatorSqlLogin, appAuth, sql.AppWindowsAccount,
                    sql.DataFilePath, sql.LogFilePath, dataSize, logSize, encrypt, trustServerCertificate),
                new BackupAnswers(backup.BackupRoot!, fullBackupTime, logEvery, retention, integrityDay, integrityTime),
                licensePath!,
                new AdministratorAnswers(userName, displayName, admin.Email),
                new PrerequisitesAnswers(prerequisites.InstallIisFeatures ?? true, prerequisites.InstallHostingBundle ?? true)),
            issues);
    }

    private static void ValidateLocalRoot(string field, string path, Action<string, string> error)
    {
        if (!DriveAbsolutePath().IsMatch(path) || HasInvalidPathChars(path))
        {
            error(field, "Folosiți o cale absolută locală (de exemplu C:\\Program Files\\Erp), nu o cale UNC.");
        }
        else if (DriveRoot().IsMatch(path))
        {
            error(field, "Calea nu poate fi rădăcina unui disc.");
        }
    }

    // Validarea din 8.2: caile ajung literal in comenzi SQL, deci fara ', ;, -- sau caractere de control.
    private static bool ValidateSqlPath(string field, string path, Action<string, string> error)
    {
        if (!(DriveAbsolutePath().IsMatch(path) || UncPath().IsMatch(path)))
        {
            error(field, "Folosiți o cale absolută pe serverul SQL (de exemplu D:\\SQLData).");
            return false;
        }

        if (HasInvalidPathChars(path) || path.Contains('\'') || path.Contains(';') || path.Contains("--", StringComparison.Ordinal))
        {
            error(field, "Calea conține caractere nepermise (' ; -- < > \" | ? * sau caractere de control).");
            return false;
        }

        return true;
    }

    private static bool HasInvalidPathChars(string path) =>
        path.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '|' or '?' or '*')
        || (path.Length > 2 && path.IndexOf(':', 2) >= 0);

    private static bool SameDrive(string a, string b) =>
        DriveAbsolutePath().IsMatch(a) && DriveAbsolutePath().IsMatch(b) && char.ToUpperInvariant(a[0]) == char.ToUpperInvariant(b[0]);

    private static SqlAuthentication ParseAuthentication(string field, string? value, Action<string, string> error)
    {
        if (value is null)
        {
            return field == "sql.appAuthentication" ? SqlAuthentication.Sql : SqlAuthentication.Windows;
        }

        if (TryParseExact(value, out SqlAuthentication result))
        {
            return result;
        }

        error(field, "Valorile permise sunt Windows și Sql.");
        return SqlAuthentication.Windows;
    }

    // Valorile tehnice se compara exact (NET-21), fara numere in locul numelor.
    private static bool TryParseExact<TEnum>(string value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal) && Enum.TryParse(value, false, out result);
    }

    private static TimeOnly ParseTime(string field, string? value, TimeOnly defaultValue, Action<string, string> error)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        error(field, "Folosiți formatul HH:mm (de exemplu 01:00).");
        return defaultValue;
    }

    private static bool IsDnsName(string value) =>
        value.Length <= 253 && value.Split('.').All(label => DnsLabel().IsMatch(label));

    private static bool IsSqlServerName(string value)
    {
        var match = SqlServerName().Match(value);
        return match.Success
            && (!match.Groups["port"].Success || int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture) is >= 1 and <= 65535);
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.');

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseName();

    [GeneratedRegex("^[a-z][a-z0-9._-]{2,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdminUserName();

    [GeneratedRegex(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant)]
    private static partial Regex DriveAbsolutePath();

    [GeneratedRegex(@"^[A-Za-z]:\\?$", RegexOptions.CultureInvariant)]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^\\\\[^\\]+\\[^\\]+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPath();

    [GeneratedRegex("^[0-9A-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex Thumbprint();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex DnsLabel();

    [GeneratedRegex(@"^(\.|\(local\)|[A-Za-z0-9][A-Za-z0-9.-]*)((\\[A-Za-z0-9_$#-]{1,16})|(,(?<port>\d{1,5})))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlServerName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,63}\\[A-Za-z0-9._-]{1,20}\$?$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAccount();
}

public sealed record AnswersValidationResult(InstallAnswers? Answers, IReadOnlyList<AnswersIssue> Issues)
{
    public bool IsValid => Answers is not null;

    public IEnumerable<AnswersIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<AnswersIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

    public static AnswersValidationResult Invalid(AnswersIssue issue) => new(null, [issue]);
}
