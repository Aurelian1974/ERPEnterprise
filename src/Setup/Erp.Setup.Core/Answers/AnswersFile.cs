using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Setup.Core.Answers;

/// <summary>
/// Fisierul de raspunsuri, versiunea 1 (08, 6.2, D14): produs de expert (install\answers.json, „Salveaza raspunsurile…”) si
/// citit de linia de comanda si de „Incarca un fisier de raspunsuri…”. Campurile lipsa primesc valorile implicite din 5.3.
/// </summary>
public sealed class AnswersFile
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "answers.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public int? FormatVersion { get; set; }

    public string? Instance { get; set; }

    public SqlSection? Sql { get; set; }

    public BackupSection? Backup { get; set; }

    public SiteSection? Site { get; set; }

    public LicenseSection? License { get; set; }

    public List<string>? AcceptedWarnings { get; set; }

    public sealed class SqlSection
    {
        public string? Server { get; set; }

        public string? Database { get; set; }

        public string? DataFolder { get; set; }

        public string? LogFolder { get; set; }

        public string? AppIdentity { get; set; }

        public string? GmsaAccount { get; set; }
    }

    public sealed class BackupSection
    {
        public string? Folder { get; set; }

        public string? FullBackupTime { get; set; }

        public int? LogBackupEveryMinutes { get; set; }

        public int? RetentionDays { get; set; }

        public string? IntegrityCheckDay { get; set; }

        public string? IntegrityCheckTime { get; set; }
    }

    public sealed class SiteSection
    {
        public string? HostName { get; set; }

        public int? HttpsPort { get; set; }

        public bool? HttpRedirect { get; set; }

        public CertificateSection? Certificate { get; set; }

        public string? InstallRoot { get; set; }
    }

    public sealed class CertificateSection
    {
        public string? Kind { get; set; }

        public string? Thumbprint { get; set; }
    }

    public sealed class LicenseSection
    {
        public string? File { get; set; }
    }

    public static AnswersFile Parse(string json) =>
        JsonSerializer.Deserialize<AnswersFile>(json, JsonOptions) ?? throw new JsonException("Fișierul de răspunsuri este gol.");

    /// <summary>Fisierul pentru o cerere (08, 6.2): toate campurile scrise explicit, fara secrete.</summary>
    public static AnswersFile From(InstallRequest request) => new()
    {
        FormatVersion = CurrentFormatVersion,
        Instance = request.Instance,
        Sql = new SqlSection
        {
            Server = request.Sql.Server,
            Database = request.Sql.Database,
            DataFolder = request.Sql.DataFolder,
            LogFolder = request.Sql.LogFolder,
            AppIdentity = request.Sql.AppIdentity.ToString(),
            GmsaAccount = request.Sql.GmsaAccount,
        },
        Backup = new BackupSection
        {
            Folder = request.Backup.Folder,
            FullBackupTime = request.Backup.FullBackupTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            LogBackupEveryMinutes = request.Backup.LogBackupEveryMinutes,
            RetentionDays = request.Backup.RetentionDays,
            IntegrityCheckDay = request.Backup.IntegrityCheckDay.ToString(),
            IntegrityCheckTime = request.Backup.IntegrityCheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        },
        Site = new SiteSection
        {
            HostName = request.Site.HostName,
            HttpsPort = request.Site.HttpsPort,
            HttpRedirect = request.Site.HttpRedirect,
            Certificate = new CertificateSection { Kind = request.Site.Certificate.ToString(), Thumbprint = request.Site.CertificateThumbprint },
            InstallRoot = request.InstallRoot,
        },
        License = new LicenseSection { File = request.LicenseFile },
        AcceptedWarnings = [.. request.AcceptedWarnings.Order(StringComparer.Ordinal)],
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;

    /// <summary>Scriere atomica, in afara kitului (08, 6.2).</summary>
    public static void Write(string path, InstallRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, From(request).ToJson());
        File.Move(temporary, path, overwrite: true);
    }
}
