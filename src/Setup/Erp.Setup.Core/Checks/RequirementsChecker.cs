using System.Globalization;
using Erp.Licensing;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;

namespace Erp.Setup.Core.Checks;

/// <summary>Rezultatul verificarii kitului (C-05), facuta inaintea celorlalte de KitVerifier.</summary>
public sealed record KitCheck(bool Passed, string Message, int? ExitCode = null);

/// <summary>
/// Verificarile C-01…C-20 (08, 5.4). Toate ruleaza, ca operatorul sa vada lista completa. Singurele modificari sunt folderele
/// create de contul SQL Server cu xp_create_subdir (C-16, C-20), care sunt chiar verificarea drepturilor.
/// </summary>
public sealed class RequirementsChecker(
    IWindowsProbe windows,
    ISqlServerConnector sqlConnector,
    INetworkProbe network,
    ILicenseReader licenseReader,
    TimeProvider time)
{
    public const string RequiredCollation = "Latin1_General_100_CS_AS_SC_UTF8";

    /// <summary>Functiile IIS, cu numele DISM (aceleasi pe Windows 10/11 si Windows Server, 08, 5.11).</summary>
    public static readonly IReadOnlyList<string> IisFeatures =
    [
        "IIS-WebServerRole", "IIS-WebServer", "IIS-CommonHttpFeatures", "IIS-DefaultDocument", "IIS-StaticContent", "IIS-HttpErrors",
        "IIS-HealthAndDiagnostics", "IIS-HttpLogging", "IIS-Performance", "IIS-HttpCompressionStatic", "IIS-HttpCompressionDynamic",
        "IIS-Security", "IIS-RequestFiltering", "IIS-WindowsAuthentication", "IIS-ApplicationDevelopment", "IIS-WebSockets",
        "IIS-ManagementConsole",
    ];

    private const long GiB = 1024L * 1024 * 1024;

    public async Task<RequirementsReport> RunAsync(CheckTarget target, KitManifest manifest, KitCheck kit, InstanceState? existing, CancellationToken ct)
    {
        var run = new Run(target);

        run.Add("C-01", "Rulează ca administrator", windows.IsAdministrator()
            ? (CheckStatus.Passed, "Da.", null)
            : (CheckStatus.Failed, "Porniți installer-ul ca administrator.", ExitCodes.UnsupportedOsOrNotAdministrator));
        run.Add("C-02", "Windows suportat, 64 de biți", OperatingSystemCheck());
        run.Add("C-03", "Memorie și procesor", HardwareCheck());
        run.Add("C-04", "Spațiu pe disc local", DiskCheck(target));
        run.Add("C-05", "Kitul e întreg și autentic", kit.Passed
            ? (CheckStatus.Passed, kit.Message, null)
            : (CheckStatus.Failed, kit.Message, kit.ExitCode ?? ExitCodes.KitFilesMissingOrModified));
        run.Add("C-06", "IIS și funcțiile necesare", IisCheck());
        run.Add("C-07", "ASP.NET Core Hosting Bundle", HostingBundleCheck(target, manifest));
        run.Add("C-08", "Certificatul HTTPS", CertificateCheck(target));
        run.Add("C-09", "Legările IIS libere", BindingsCheck(target));

        ISqlServerProbe? sql = null;
        try
        {
            try
            {
                sql = await sqlConnector.ConnectAsync(target.Sql, ct).ConfigureAwait(false);
                run.TrustServerCertificate = sql.TrustedServerCertificate;
                run.Add("C-10", "Conexiunea SQL cu contul Windows curent", sql.TrustedServerCertificate
                    ? (CheckStatus.Warning, "Serverul SQL nu are un certificat de încredere; conexiunea e criptată, dar serverul nu e verificat.", null)
                    : (CheckStatus.Passed, $"Conectat la {target.Sql.Server}, criptat.", null));
            }
            catch (SqlConnectFailedException ex)
            {
                run.Add("C-10", "Conexiunea SQL cu contul Windows curent", (CheckStatus.Failed, ex.Message, ExitCodes.RequirementsNotMet));
            }

            await SqlChecksAsync(run, sql, manifest, existing, ct).ConfigureAwait(false);
        }
        finally
        {
            if (sql is not null)
            {
                await sql.DisposeAsync().ConfigureAwait(false);
            }
        }

        run.Add("C-17", "Licența", LicenseCheck(target, manifest));
        await NetworkCheckAsync(run, ct).ConfigureAwait(false);
        return run.Report();
    }

    private (CheckStatus, string, int?) OperatingSystemCheck()
    {
        var os = windows.GetOperatingSystem();
        if (!os.Is64Bit)
        {
            return (CheckStatus.Failed, "Este necesar un Windows pe 64 de biți.", ExitCodes.UnsupportedOsOrNotAdministrator);
        }

        if (os.IsServer)
        {
            return os.Build >= 17763
                ? (CheckStatus.Passed, $"Windows Server (build {os.Build}).", null)
                : (CheckStatus.Failed, $"Windows Server build {os.Build} nu e suportat; minimum Windows Server 2019 (17763).", ExitCodes.UnsupportedOsOrNotAdministrator);
        }

        return os.Build switch
        {
            >= 22000 => (CheckStatus.Passed, $"Windows 11 (build {os.Build}).", null),
            >= 19045 => (CheckStatus.Warning, "Windows 10 22H2: nu mai primește actualizări de securitate din 14.10.2025, în afara abonamentului ESU (D27).", null),
            _ => (CheckStatus.Failed, $"Windows build {os.Build} nu e suportat; minimum Windows 10 22H2 (19045).", ExitCodes.UnsupportedOsOrNotAdministrator),
        };
    }

    private (CheckStatus, string, int?) HardwareCheck()
    {
        var cores = windows.LogicalProcessorCount;
        var memory = windows.TotalPhysicalMemoryBytes / (double)GiB;
        var text = $"{cores} nuclee, {memory:F0} GB RAM";
        if (cores < 4 || memory < 7.5)
        {
            return (CheckStatus.Failed, $"{text}; minimum 4 nuclee și 8 GB RAM.", null);
        }

        return windows.GetOperatingSystem().IsServer && memory < 15.5
            ? (CheckStatus.Warning, $"{text}; în producție se recomandă 16 GB.", null)
            : (CheckStatus.Passed, text + ".", null);
    }

    private (CheckStatus, string, int?) DiskCheck(CheckTarget target)
    {
        var short5 = new List<string>();
        foreach (var path in new[] { target.InstallRoot, target.DataRoot })
        {
            if (windows.GetFreeSpaceBytes(path) is not { } free || free < 5 * GiB)
            {
                short5.Add(path);
            }
        }

        return short5.Count == 0
            ? (CheckStatus.Passed, "Cel puțin 5 GB liberi pentru program și date.", null)
            : (CheckStatus.Failed, $"Sub 5 GB liberi (sau volum inexistent): {string.Join(", ", short5)}.", null);
    }

    private (CheckStatus, string, int?) IisCheck()
    {
        var enabled = windows.GetEnabledWindowsFeatures();
        if (enabled is null)
        {
            return (CheckStatus.Warning, "Funcțiile Windows nu au putut fi interogate; se activează la instalare.", null);
        }

        var missing = IisFeatures.Where(f => !enabled.Contains(f)).ToList();
        return missing.Count == 0
            ? (CheckStatus.Passed, "IIS și funcțiile necesare sunt active.", null)
            : (CheckStatus.Warning, $"Lipsesc {missing.Count} funcții, se activează la instalare (poate cere o repornire).", null);
    }

    private (CheckStatus, string, int?) HostingBundleCheck(CheckTarget target, KitManifest manifest)
    {
        var required = Version.Parse(manifest.HostingBundleVersion);
        var installed = windows.GetAspNetCoreRuntimeVersions().Any(v => v.Major == required.Major && v >= required) && windows.IsAspNetCoreModuleInstalled();
        if (installed)
        {
            return (CheckStatus.Passed, $"Instalat (≥ {required}).", null);
        }

        var others = windows.GetIisBindings().Select(b => b.SiteName).Where(s => !string.Equals(s, target.SiteName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return (CheckStatus.Warning, others == 0
            ? $"Lipsește; se instalează din kit ({required})."
            : $"Lipsește; se instalează din kit ({required}). Instalarea repornește IIS; celelalte {others} site-uri vor fi indisponibile aproximativ un minut.", null);
    }

    private (CheckStatus, string, int?) CertificateCheck(CheckTarget target)
    {
        if (target.Certificate == CertificateKind.Internal)
        {
            return (CheckStatus.Passed, $"Se generează un certificat intern pentru {target.HostName}, valabil 2 ani.", null);
        }

        var certificate = windows.FindMachineCertificate(target.CertificateThumbprint ?? string.Empty);
        if (certificate is null)
        {
            return (CheckStatus.Failed, $"Certificatul {target.CertificateThumbprint} nu există în LocalMachine\\My.", null);
        }

        var problems = new List<string>();
        if (!certificate.HasPrivateKey)
        {
            problems.Add("nu are cheie privată");
        }

        if (!certificate.DnsNames.Any(n => DnsNameMatches(n, target.HostName)))
        {
            problems.Add($"nu conține adresa {target.HostName} în SAN");
        }

        if (!certificate.HasServerAuthenticationEku)
        {
            problems.Add("nu are EKU Server Authentication");
        }

        var now = time.GetUtcNow();
        if (certificate.NotAfter <= now)
        {
            problems.Add("a expirat");
        }

        if (problems.Count > 0)
        {
            return (CheckStatus.Failed, $"Certificatul {string.Join(", ", problems)}.", null);
        }

        return certificate.NotAfter < now.AddDays(30)
            ? (CheckStatus.Warning, $"Certificatul expiră la {certificate.NotAfter:dd.MM.yyyy}; înlocuiți-l curând.", null)
            : (CheckStatus.Passed, $"Valabil până la {certificate.NotAfter:dd.MM.yyyy}.", null);
    }

    private (CheckStatus, string, int?) BindingsCheck(CheckTarget target)
    {
        var ports = target.HttpRedirect ? new[] { target.HttpsPort, 80 } : [target.HttpsPort];
        var conflicts = windows.GetIisBindings()
            .Where(b => ports.Contains(b.Port) && string.Equals(b.HostName, target.HostName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(b.SiteName, target.SiteName, StringComparison.OrdinalIgnoreCase))
            .Select(b => $"{b.SiteName} ({b.Protocol} {b.Port})")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return conflicts.Count == 0
            ? (CheckStatus.Passed, $"Legările {string.Join(" și ", ports)} pentru {target.HostName} sunt libere.", null)
            : (CheckStatus.Failed, $"Adresa {target.HostName} e folosită de alt site: {string.Join(", ", conflicts)}.", null);
    }

    private async Task SqlChecksAsync(Run run, ISqlServerProbe? sql, KitManifest manifest, InstanceState? existing, CancellationToken ct)
    {
        string[] ids = ["C-11", "C-12", "C-13", "C-14", "C-15", "C-16", "C-18", "C-20"];
        if (sql is null)
        {
            foreach (var id in ids)
            {
                run.Add(id, Titles[id], (CheckStatus.NotApplicable, "Nu s-a verificat: conexiunea SQL (C-10) a eșuat.", null));
            }

            return;
        }

        SqlServerInfo info;
        try
        {
            info = await sql.GetServerInfoAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var id in ids)
            {
                run.Add(id, Titles[id], (CheckStatus.Failed, $"Proprietățile serverului nu au putut fi citite: {ex.Message}", null));
            }

            return;
        }

        run.Sql = info;
        var target = run.Target;
        run.Add("C-11", Titles["C-11"], info.MajorVersion < manifest.MinSqlServerMajorVersion
            ? (CheckStatus.Failed, $"Versiunea majoră {info.MajorVersion}; este necesar SQL Server 2022 (16) sau mai nou.", null)
            : info.IsExpress ? (CheckStatus.Warning, $"{info.Edition}: fără SQL Agent, deci fără backup automat; limită de dimensiune a bazei; doar pentru test și demonstrații.", null)
            : info.IsDeveloper ? (CheckStatus.Warning, $"{info.Edition}: doar pentru test.", null)
            : (CheckStatus.Passed, $"SQL Server {info.MajorVersion}, {info.Edition}.", null));
        run.Add("C-12", Titles["C-12"], info.IsSysadmin
            ? (CheckStatus.Passed, "Contul curent este sysadmin.", null)
            : (CheckStatus.Failed, "Contul Windows curent trebuie să fie sysadmin pe SQL Server pe durata operației (D17).", ExitCodes.RequirementsNotMet));

        if (info.IsExpress)
        {
            run.Add("C-13", Titles["C-13"], (CheckStatus.NotApplicable, "Express nu are SQL Server Agent.", null));
        }
        else
        {
            var agent = await Safe(() => sql.GetAgentStatusAsync(ct)).ConfigureAwait(false);
            run.Add("C-13", Titles["C-13"], string.Equals(agent, "Running", StringComparison.OrdinalIgnoreCase)
                ? (CheckStatus.Passed, "Pornit.", null)
                : (CheckStatus.Failed, "Porniți SQL Server Agent și setați-l pe Automatic.", null));
        }

        var database = await Safe(() => sql.GetDatabaseAsync(target.Sql.DatabaseName, ct)).ConfigureAwait(false);
        run.Database = database;
        run.Add("C-14", Titles["C-14"], DatabaseCheck(target, database, existing));
        run.Add("C-15", Titles["C-15"], RemoteCheck(run, info));

        var backup = target.BackupFolder ?? (info.DefaultBackupPath is { Length: > 0 } root ? Path.Combine(root, target.Sql.DatabaseName) : null);
        run.BackupFolder = backup;
        if (info.IsExpress)
        {
            run.Add("C-16", Titles["C-16"], (CheckStatus.NotApplicable, "Express: fără joburi de backup.", null));
        }
        else if (backup is null)
        {
            run.Add("C-16", Titles["C-16"], (CheckStatus.Failed, "Folderul de backup nu e ales, iar instanța SQL nu are un folder implicit de backup.", null));
        }
        else
        {
            run.Add("C-16", Titles["C-16"], await CreateFolderAsync(sql, backup, ct).ConfigureAwait(false));
        }

        var skew = (time.GetUtcNow().UtcDateTime - DateTime.SpecifyKind(info.UtcNow, DateTimeKind.Utc)).Duration();
        run.Add("C-18", Titles["C-18"], skew < TimeSpan.FromMinutes(2)
            ? (CheckStatus.Passed, $"Diferență {skew.TotalSeconds.ToString("F1", CultureInfo.GetCultureInfo("ro-RO"))} s.", null)
            : (CheckStatus.Warning, $"Ora diferă cu {skew.TotalMinutes:F0} minute de SQL Server; sincronizați ceasurile (NTP).", null));

        var folders = new[] { target.DataFolder, target.LogFolder }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0)
        {
            run.Add("C-20", Titles["C-20"], (CheckStatus.Passed, "Se folosesc folderele implicite ale instanței SQL.", null));
        }
        else
        {
            var failed = new List<string>();
            foreach (var folder in folders)
            {
                if ((await CreateFolderAsync(sql, folder, ct).ConfigureAwait(false)).Item1 == CheckStatus.Failed)
                {
                    failed.Add(folder);
                }
            }

            run.Add("C-20", Titles["C-20"], failed.Count == 0
                ? (CheckStatus.Passed, $"Accesibile pentru SQL Server: {string.Join(", ", folders)}.", null)
                : (CheckStatus.Failed, $"SQL Server nu poate crea: {string.Join(", ", failed)}.", null));
        }
    }

    private static (CheckStatus, string, int?) DatabaseCheck(CheckTarget target, DatabaseInfo? database, InstanceState? existing)
    {
        var name = target.Sql.DatabaseName;
        if (target.Mode is CheckMode.Upgrade or CheckMode.Repair or CheckMode.Recover)
        {
            if (database is null)
            {
                return (CheckStatus.Failed, $"Baza {name} nu există.", ExitCodes.SqlInstallError);
            }

            if (!string.Equals(database.Collation, RequiredCollation, StringComparison.Ordinal))
            {
                return (CheckStatus.Failed, $"Baza {name} are collation-ul {database.Collation}.", ExitCodes.WrongDatabaseCollation);
            }

            return database.IsErp
                ? (CheckStatus.Passed, $"Baza ERP a instalării {database.ErpInstance}, la {database.LastMigration ?? "nicio migrare"}.", null)
                : (CheckStatus.Failed, $"Baza {name} nu este o bază ERP.", ExitCodes.DatabaseExistsNotEmpty);
        }

        if (database is null)
        {
            return (CheckStatus.Passed, $"Baza {name} nu există; va fi creată cu {RequiredCollation}.", null);
        }

        if (!string.Equals(database.Collation, RequiredCollation, StringComparison.Ordinal))
        {
            return (CheckStatus.Failed, $"Baza {name} există și are collation-ul {database.Collation}, nu {RequiredCollation}.", ExitCodes.WrongDatabaseCollation);
        }

        if (database.IsErp)
        {
            return existing is not null && string.Equals(database.ErpInstallId, existing.InstallId.ToString(), StringComparison.OrdinalIgnoreCase)
                ? (CheckStatus.Passed, $"Baza {name} aparține acestei instalări; instalarea se reia.", null)
                : (CheckStatus.Failed, $"Baza aparține instalării {database.ErpInstance}; folosiți „Recuperează o instalare existentă”.", ExitCodes.DatabaseExistsNotEmpty);
        }

        return database.IsEmpty
            ? (CheckStatus.Passed, $"Baza existentă {name} e goală și va fi preluată.", null)
            : (CheckStatus.Failed, "Baza există deja și conține date; alegeți alt nume.", ExitCodes.DatabaseExistsNotEmpty);
    }

    private (CheckStatus, string, int?) RemoteCheck(Run run, SqlServerInfo info)
    {
        var target = run.Target;
        var local = string.Equals(info.MachineName, windows.MachineName, StringComparison.OrdinalIgnoreCase);
        run.SqlIsRemote = !local;
        if (local)
        {
            return (CheckStatus.Passed, "SQL Server e pe același calculator: aplicația folosește conturile virtuale ale pool-ului și serviciului.", null);
        }

        if (windows.GetComputerDomain() is not { } domain)
        {
            return (CheckStatus.Failed, "SQL Server e pe alt calculator, dar acest calculator nu e într-un domeniu: fără domeniu, SQL Server trebuie să fie pe același calculator.", null);
        }

        return target.AppIdentity switch
        {
            AppIdentity.Gmsa when target.GmsaAccount is { } account => windows.IsServiceAccountUsable(account)
                ? (CheckStatus.Passed, $"SQL pe {info.MachineName}; aplicația folosește contul gMSA {account}.", null)
                : (CheckStatus.Failed, $"Contul gMSA {account} nu există sau nu poate fi folosit pe acest calculator; urmați pașii din ghid (5.2).", null),
            AppIdentity.ComputerAccount => (CheckStatus.Warning,
                $"Aplicația folosește contul calculatorului {domain}\\{windows.MachineName}$: orice serviciu al acestui calculator care iese în rețea cu contul calculatorului va avea acces la baza ERP.", null),
            _ => (CheckStatus.Failed, $"SQL Server e pe alt calculator ({info.MachineName}): alegeți un cont gMSA sau contul calculatorului.", null),
        };
    }

    private (CheckStatus, string, int?) LicenseCheck(CheckTarget target, KitManifest manifest)
    {
        if (target.Mode is CheckMode.Upgrade or CheckMode.Repair or CheckMode.Recover)
        {
            return (CheckStatus.NotApplicable, "Licența instalării e în bază; dreptul la actualizări se verifică la U-01.", null);
        }

        if (!File.Exists(target.LicenseFile))
        {
            return (CheckStatus.Failed, $"Fișierul de licență nu există: {target.LicenseFile}", ExitCodes.InvalidLicense);
        }

        var result = licenseReader.Read(File.ReadAllBytes(target.LicenseFile));
        if (!result.IsValid)
        {
            return (CheckStatus.Failed, result.Message, ExitCodes.InvalidLicense);
        }

        var release = DateOnly.ParseExact(manifest.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (LicensePolicy.CheckKitRelease(result.License!, release, manifest.Version) is { } refused)
        {
            return (CheckStatus.Failed, refused, ExitCodes.LicenseExcludesKit);
        }

        return (CheckStatus.Passed, $"{result.License!.LicenseId}, {result.License.CustomerName}, {result.License.Companies} CUI-uri.", null);
    }

    private async Task NetworkCheckAsync(Run run, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(10);
        var anaf = await network.CanReachAsync(HttpMethod.Head, new Uri("https://api.anaf.ro/"), timeout, ct).ConfigureAwait(false);
        var bnr = await network.CanReachAsync(HttpMethod.Get, new Uri("https://www.bnr.ro/nbrfxrates.xml"), timeout, ct).ConfigureAwait(false);
        var down = new[] { (anaf, "api.anaf.ro"), (bnr, "www.bnr.ro") }.Where(x => !x.Item1).Select(x => x.Item2).ToList();
        run.Add("C-19", "Ieșire spre ANAF și BNR", down.Count == 0
            ? (CheckStatus.Passed, "ANAF și BNR răspund.", null)
            : (CheckStatus.Warning, $"Nu răspund: {string.Join(", ", down)}. Integrările ANAF și BNR nu vor funcționa până la deschiderea ieșirii HTTPS (443).", null));
    }

    private static async Task<(CheckStatus, string, int?)> CreateFolderAsync(ISqlServerProbe sql, string path, CancellationToken ct)
    {
        try
        {
            await sql.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
            return (CheckStatus.Passed, $"{path}: accesibil pentru contul serviciului SQL Server.", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (CheckStatus.Failed, $"{path}: contul serviciului SQL Server nu poate crea folderul ({ex.Message}).", null);
        }
    }

    private static async Task<T?> Safe<T>(Func<Task<T?>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default;
        }
    }

    public static bool DnsNameMatches(string certificateName, string hostName)
    {
        if (certificateName.StartsWith("*.", StringComparison.Ordinal))
        {
            var dot = hostName.IndexOf('.', StringComparison.Ordinal);
            return dot > 0 && string.Equals(certificateName[1..], hostName[dot..], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(certificateName, hostName, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["C-11"] = "Versiunea și ediția SQL",
        ["C-12"] = "Drepturile SQL ale operatorului",
        ["C-13"] = "SQL Server Agent pornit",
        ["C-14"] = "Starea bazei",
        ["C-15"] = "SQL pe alt calculator",
        ["C-16"] = "Folderul de backup",
        ["C-18"] = "Ceasul",
        ["C-20"] = "Folderele de date și log",
    };

    private sealed class Run(CheckTarget target)
    {
        private readonly List<CheckResult> results = [];

        public CheckTarget Target { get; } = target;

        public SqlServerInfo? Sql { get; set; }

        public DatabaseInfo? Database { get; set; }

        public bool SqlIsRemote { get; set; }

        public bool TrustServerCertificate { get; set; }

        public string? BackupFolder { get; set; }

        public void Add(string id, string title, (CheckStatus Status, string Message, int? ExitCode) outcome)
        {
            if (results.Any(r => r.Id == id))
            {
                return;
            }

            results.Add(new CheckResult(id, title, outcome.Status, outcome.Message, outcome.Status == CheckStatus.Failed ? outcome.ExitCode : null));
        }

        public RequirementsReport Report() => new([.. results.OrderBy(r => r.Id, StringComparer.Ordinal)])
        {
            Sql = Sql,
            Database = Database,
            SqlIsRemote = SqlIsRemote,
            TrustServerCertificate = TrustServerCertificate,
            BackupFolder = BackupFolder,
        };
    }
}

/// <summary>Rezultatul verificarilor (08, 5.4), codul de iesire (08, 6.3) si faptele SQL folosite de plan.</summary>
public sealed record RequirementsReport(IReadOnlyList<CheckResult> Results)
{
    public SqlServerInfo? Sql { get; init; }

    public DatabaseInfo? Database { get; init; }

    public bool SqlIsRemote { get; init; }

    /// <summary>C-10 a reusit doar fara verificarea certificatului serverului SQL; valoarea se scrie in configurare.</summary>
    public bool TrustServerCertificate { get; init; }

    /// <summary>Folderul de backup ales sau cel implicit al instantei SQL (C-16); null pe Express.</summary>
    public string? BackupFolder { get; init; }

    public bool HasFailures => Results.Any(r => r.Status == CheckStatus.Failed);

    public bool HasWarnings => Results.Any(r => r.Status == CheckStatus.Warning);

    /// <summary>Avertismentele neacceptate (08, 6.2): la --unattended opresc instalarea cu codul 12.</summary>
    public IReadOnlyList<CheckResult> UnacceptedWarnings(IReadOnlySet<string> accepted) =>
        [.. Results.Where(r => r.Status == CheckStatus.Warning && !accepted.Contains(r.Id))];

    /// <summary>Primul esec (in ordinea ID-urilor) decide codul; fara esecuri, 1 daca exista avertismente, altfel 0.</summary>
    public int ExitCode =>
        Results.FirstOrDefault(r => r.Status == CheckStatus.Failed) is { } failure ? failure.ExitCode ?? ExitCodes.RequirementsNotMet
        : HasWarnings ? ExitCodes.SuccessWithWarnings
        : ExitCodes.Success;
}
