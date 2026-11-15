using System.Globalization;
using Erp.Licensing;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;

namespace Erp.Setup.Core.Checks;

/// <summary>
/// Verificarile C-01…C-23 (7). Toate verificarile ruleaza, ca operatorul sa vada lista completa.
/// La <see cref="CheckMode.Check"/> nu se modifica nimic (C-05, C-06, C-17 doar raporteaza ce va face instalarea).
/// </summary>
public sealed class RequirementsChecker(
    IWindowsProbe windows,
    ISqlServerConnector sqlConnector,
    INetworkProbe network,
    LicenseVerifier licenseVerifier,
    TimeProvider time)
{
    public const string RequiredCollation = "Latin1_General_100_CS_AS_SC_UTF8";
    public const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";

    public static readonly IReadOnlyList<string> IisFeatures =
    [
        "Web-Server", "Web-WebServer", "Web-Static-Content", "Web-Default-Doc", "Web-Http-Errors", "Web-Http-Logging",
        "Web-Stat-Compression", "Web-Dyn-Compression", "Web-Filtering", "Web-Windows-Auth", "Web-WebSockets", "Web-Mgmt-Console",
    ];

    private static readonly int[] SupportedBuilds = [17763, 20348, 26100];
    private const long GiB = 1024L * 1024 * 1024;

    public async Task<RequirementsReport> RunAsync(
        CheckTarget target,
        KitManifest manifest,
        InstanceState? existingInstance,
        string? operatorPassword,
        CancellationToken ct)
    {
        var results = new List<CheckResult>();
        var run = new Run(target, manifest, existingInstance, results);

        Add(run, "C-01", "Rulează ca administrator", CheckAdministrator);
        Add(run, "C-02", "Sistem de operare", CheckOperatingSystem);
        Add(run, "C-03", "Procesor și memorie", CheckHardware);
        Add(run, "C-04", "Spațiu pe disc (InstallRoot, DataRoot)", CheckLocalDisk);
        Add(run, "C-05", "Roluri IIS", CheckIisFeatures);
        Add(run, "C-06", "ASP.NET Core Hosting Bundle", CheckHostingBundle);
        Add(run, "C-07", "Certificat HTTPS", CheckCertificate);
        Add(run, "C-08", "Port HTTPS liber", CheckPort);

        ISqlServerProbe? sql = null;
        try
        {
            try
            {
                sql = await sqlConnector.ConnectAsync(target.Sql, operatorPassword, ct).ConfigureAwait(false);
                run.Add("C-09", "Conexiune SQL", CheckStatus.Passed, $"Conectat la {target.Sql.Server}.");
            }
            catch (SqlConnectFailedException ex)
            {
                run.Add("C-09", "Conexiune SQL", CheckStatus.Failed, ex.Message);
            }

            await RunSqlChecksAsync(run, sql, ct).ConfigureAwait(false);
        }
        finally
        {
            if (sql is not null)
            {
                await sql.DisposeAsync().ConfigureAwait(false);
            }
        }

        await AddAsync(run, "C-20", "Ieșire spre ANAF și BNR", CheckInternetAsync, ct).ConfigureAwait(false);
        Add(run, "C-21", "TLS 1.2 activ", CheckTls);
        Add(run, "C-22", "Licența", CheckLicense);
        Add(run, "C-23", "Instanța nu există deja", CheckInstanceIsNew);

        results.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return new RequirementsReport(results);
    }

    private async Task RunSqlChecksAsync(Run run, ISqlServerProbe? sql, CancellationToken ct)
    {
        string[] ids = ["C-10", "C-11", "C-12", "C-13", "C-14", "C-15", "C-16", "C-17", "C-18", "C-19"];
        string[] titles =
        [
            "Versiune SQL Server", "Ediție SQL Server", "Drepturile operatorului", "SQL Server Agent pornit", "Collation-ul serverului",
            "Baza nu există", "Collation-ul bazei existente", "Căile SQL de date, log și backup", "Spațiu pe disc la SQL", "Ceasul",
        ];

        if (sql is null)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                run.Add(ids[i], titles[i], CheckStatus.NotApplicable, "Nu s-a verificat: conexiunea SQL (C-09) a eșuat.");
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
            for (var i = 0; i < ids.Length; i++)
            {
                run.Add(ids[i], titles[i], CheckStatus.Failed, $"Proprietățile serverului nu au putut fi citite: {ex.Message}");
            }

            return;
        }

        var minVersion = run.Manifest.MinSqlServerMajorVersion;
        run.Add("C-10", titles[0], info.MajorVersion >= minVersion ? CheckStatus.Passed : CheckStatus.Failed,
            info.MajorVersion >= minVersion
                ? $"Versiunea majoră {info.MajorVersion}."
                : $"Versiunea majoră {info.MajorVersion}; este necesar SQL Server 2022 (16) sau mai nou.");

        CheckEdition(run, info);
        CheckOperatorRights(run, info);
        await SafeAsync(run, "C-13", titles[3], () => CheckAgentAsync(run, sql, ct)).ConfigureAwait(false);

        var serverCaseSensitive = info.Collation.Contains("_CS_", StringComparison.Ordinal);
        run.Add("C-14", titles[4], serverCaseSensitive ? CheckStatus.Passed : CheckStatus.Warning,
            serverCaseSensitive
                ? $"Serverul are collation {info.Collation}."
                : $"Serverul are collation {info.Collation} (nu este case sensitive); baza va fi totuși {RequiredCollation}, iar regula SQL-23 (tabele temporare cu COLLATE DATABASE_DEFAULT) devine critică.");

        await SafeAsync(run, "C-15", titles[5], () => CheckDatabaseAsync(run, sql, ct)).ConfigureAwait(false);
        await SafeAsync(run, "C-17", titles[7], () => CheckSqlPathsAsync(run, sql, info, ct)).ConfigureAwait(false);
        await SafeAsync(run, "C-18", titles[8], () => CheckSqlDiskAsync(run, sql, info, ct)).ConfigureAwait(false);

        var skew = (time.GetUtcNow().UtcDateTime - DateTime.SpecifyKind(info.UtcNow, DateTimeKind.Utc)).Duration();
        run.Add("C-19", titles[9], skew < TimeSpan.FromMinutes(2) ? CheckStatus.Passed : CheckStatus.Warning,
            skew < TimeSpan.FromMinutes(2)
                ? "Ceasurile serverelor sunt sincronizate."
                : $"Ora serverului de aplicație diferă cu {skew.TotalMinutes:F0} minute de SQL Server; sincronizați ceasurile (NTP).");

        // C-16 este adaugat de CheckDatabaseAsync (depinde de C-15).
        run.Add("C-16", titles[6], CheckStatus.NotApplicable, "Nu s-a verificat: verificarea bazei (C-15) a eșuat.");
    }

    private static async Task SafeAsync(Run run, string id, string title, Func<Task> check)
    {
        try
        {
            await check().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Add(id, title, CheckStatus.Failed, $"Verificarea nu a putut fi efectuată: {ex.Message}");
        }
    }

    private void Add(Run run, string id, string title, Func<Run, (CheckStatus Status, string Message, int? ExitCode)> check)
    {
        try
        {
            var (status, message, exitCode) = check(run);
            run.Add(id, title, status, message, exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Add(id, title, CheckStatus.Failed, $"Verificarea nu a putut fi efectuată: {ex.Message}");
        }
    }

    private static async Task AddAsync(Run run, string id, string title, Func<Run, CancellationToken, Task<(CheckStatus, string)>> check, CancellationToken ct)
    {
        try
        {
            var (status, message) = await check(run, ct).ConfigureAwait(false);
            run.Add(id, title, status, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Add(id, title, CheckStatus.Failed, $"Verificarea nu a putut fi efectuată: {ex.Message}");
        }
    }

    private (CheckStatus, string, int?) CheckAdministrator(Run run) => windows.IsAdministrator()
        ? (CheckStatus.Passed, "Installer-ul rulează ca administrator.", null)
        : (CheckStatus.Failed, "Rulați ErpSetup dintr-o consolă deschisă ca administrator.", ExitCodes.UnsupportedOsOrNotAdministrator);

    private (CheckStatus, string, int?) CheckOperatingSystem(Run run)
    {
        var os = windows.GetOperatingSystem();
        if (!os.Is64Bit || string.Equals(os.ProductType, "WinNT", StringComparison.Ordinal))
        {
            return (CheckStatus.Failed, $"Este necesar Windows Server 2019, 2022 sau 2025 pe 64 de biți (detectat: build {os.Build}, tip {os.ProductType}).",
                ExitCodes.UnsupportedOsOrNotAdministrator);
        }

        return SupportedBuilds.Contains(os.Build)
            ? (CheckStatus.Passed, $"Windows Server, build {os.Build}.", null)
            : (CheckStatus.Failed, $"Build-ul Windows Server {os.Build} nu este suportat (17763, 20348, 26100).", ExitCodes.UnsupportedOsOrNotAdministrator);
    }

    private (CheckStatus, string, int?) CheckHardware(Run run)
    {
        var cores = windows.LogicalProcessorCount;
        var memoryGb = windows.TotalPhysicalMemoryBytes / (double)GiB;
        var text = $"{cores} nuclee logice, {memoryGb:F1} GB RAM.";

        // Memoria raportata e putin sub valoarea nominala (memorie rezervata); 0,5 GB toleranta.
        if (cores < 4 || memoryGb < 7.5)
        {
            return (CheckStatus.Failed, $"{text} Minimum: 4 nuclee logice și 8 GB RAM (recomandat 16 GB).", null);
        }

        return memoryGb < 15.5
            ? (CheckStatus.Warning, $"{text} Recomandat cel puțin 16 GB RAM.", null)
            : (CheckStatus.Passed, text, null);
    }

    private (CheckStatus, string, int?) CheckLocalDisk(Run run)
    {
        var problems = new List<string>();
        var details = new List<string>();
        foreach (var (name, path) in new[] { ("InstallRoot", run.Target.InstallRoot), ("DataRoot", run.Target.DataRoot) })
        {
            var free = windows.GetFreeSpaceBytes(path);
            if (free is null)
            {
                problems.Add($"{name}: volumul căii {path} nu există.");
            }
            else if (free < 5 * GiB)
            {
                problems.Add($"{name}: {free / (double)GiB:F1} GB liberi pe volumul {path}; sunt necesari 5 GB.");
            }
            else
            {
                details.Add($"{name}: {free / (double)GiB:F1} GB liberi");
            }
        }

        return problems.Count > 0
            ? (CheckStatus.Failed, string.Join(" ", problems), null)
            : (CheckStatus.Passed, string.Join("; ", details) + ".", null);
    }

    private (CheckStatus, string, int?) CheckIisFeatures(Run run)
    {
        var installed = windows.GetInstalledWindowsFeatures();
        if (installed is null)
        {
            return (CheckStatus.Failed, "Funcțiile Windows nu au putut fi interogate (Get-WindowsFeature).", null);
        }

        var missing = IisFeatures.Where(f => !installed.Contains(f)).ToList();
        if (missing.Count == 0)
        {
            return (CheckStatus.Passed, "Toate rolurile IIS necesare sunt instalate.", null);
        }

        var list = string.Join(", ", missing);
        return run.Target.InstallIisFeatures && run.Target.Mode != CheckMode.Repair
            ? (CheckStatus.Warning, $"Lipsesc: {list}. Vor fi instalate la pasul I-04, după confirmare; poate fi necesară o repornire.", null)
            : (CheckStatus.Failed, $"Lipsesc: {list}. Instalați-le sau setați prerequisites.installIisFeatures = true.", null);
    }

    private (CheckStatus, string, int?) CheckHostingBundle(Run run)
    {
        var required = Version.Parse(run.Manifest.HostingBundleVersion);
        var best = windows.GetAspNetCoreRuntimeVersions().Where(v => v.Major == required.Major).DefaultIfEmpty().Max();
        var module = windows.IsAspNetCoreModuleInstalled();
        if (best is not null && best >= required && module)
        {
            return (CheckStatus.Passed, $"ASP.NET Core {best} și modulul IIS (aspnetcorev2.dll) sunt instalate.", null);
        }

        var found = best is null ? "lipsește" : $"găsit {best}";
        var message = $"Este necesar ASP.NET Core Hosting Bundle ≥ {required} ({found}{(module ? string.Empty : ", modulul IIS lipsește")}).";
        return run.Target.InstallHostingBundle && run.Target.Mode != CheckMode.Repair
            ? (CheckStatus.Warning, message + " Va fi instalat din kit la pasul I-04.", null)
            : (CheckStatus.Failed, message, null);
    }

    private (CheckStatus, string, int?) CheckCertificate(Run run)
    {
        var target = run.Target;
        if (target.CertificateThumbprint is null)
        {
            return target.AllowSelfSignedCertificate
                ? (CheckStatus.Warning, "Se va genera un certificat autosemnat, valabil 1 an; numai pentru test.", null)
                : (CheckStatus.Failed, "Lipsește amprenta certificatului HTTPS.", null);
        }

        var certificate = windows.FindMachineCertificate(target.CertificateThumbprint);
        if (certificate is null)
        {
            return (CheckStatus.Failed, $"Certificatul {target.CertificateThumbprint} nu există în LocalMachine\\My.", null);
        }

        var problems = new List<string>();
        if (!certificate.HasPrivateKey)
        {
            problems.Add("nu are cheie privată");
        }

        if (!certificate.HasServerAuthenticationEku)
        {
            problems.Add($"nu are EKU Server Authentication ({ServerAuthenticationEku})");
        }

        if (!certificate.DnsNames.Any(name => DnsNameMatches(name, target.HostName)))
        {
            problems.Add($"nu conține {target.HostName} în SAN sau CN");
        }

        var now = time.GetUtcNow();
        if (certificate.NotAfter <= now)
        {
            problems.Add($"a expirat la {certificate.NotAfter:yyyy-MM-dd}");
        }

        if (problems.Count > 0)
        {
            return (CheckStatus.Failed, $"Certificatul {string.Join(", ", problems)}.", null);
        }

        var days = (int)(certificate.NotAfter - now).TotalDays;
        return days < 30
            ? (CheckStatus.Warning, $"Certificatul expiră în {days} zile ({certificate.NotAfter:yyyy-MM-dd}); reînnoiți-l.", null)
            : (CheckStatus.Passed, $"Certificat valabil până la {certificate.NotAfter:yyyy-MM-dd}.", null);
    }

    // Conflict = alt site are deja aceeasi pereche nume de gazda + port, sau http.sys are legat pe hostName:port
    // alt certificat decat cel cerut, fara ca legarea sa apartina acestei instante.
    private (CheckStatus, string, int?) CheckPort(Run run)
    {
        var target = run.Target;
        var bindings = windows.GetIisBindings();
        var ports = new List<(string Protocol, int Port)> { ("https", target.HttpsPort) };
        if (target.HttpRedirect)
        {
            ports.Add(("http", 80));
        }

        var conflicts = new List<string>();
        foreach (var (protocol, port) in ports)
        {
            conflicts.AddRange(bindings
                .Where(b => b.Port == port
                    && string.Equals(b.Protocol, protocol, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(b.HostName, target.HostName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(b.SiteName, target.SiteName, StringComparison.OrdinalIgnoreCase))
                .Select(b => $"site-ul „{b.SiteName}” are deja legarea {protocol} *:{port}:{b.HostName}"));
        }

        var ownsHttps = bindings.Any(b => string.Equals(b.SiteName, target.SiteName, StringComparison.OrdinalIgnoreCase) && b.Port == target.HttpsPort);
        var sni = windows.GetSniBindingThumbprint(target.HostName, target.HttpsPort);
        if (sni is not null && !ownsHttps
            && !string.Equals(sni, target.CertificateThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add($"http.sys are legat pe {target.HostName}:{target.HttpsPort} certificatul {sni} (netsh http show sslcert)");
        }

        return conflicts.Count > 0
            ? (CheckStatus.Failed, string.Join("; ", conflicts) + ".", null)
            : (CheckStatus.Passed, $"{target.HostName}:{target.HttpsPort} este liber sau aparține acestei instanțe.", null);
    }

    private static void CheckEdition(Run run, SqlServerInfo info)
    {
        const string title = "Ediție SQL Server";
        if (info.EngineEdition == 4)
        {
            run.Add("C-11", title, CheckStatus.Failed, "SQL Server Express nu este suportat (nu are SQL Server Agent).");
        }
        else if (info.EngineEdition is not (2 or 3))
        {
            run.Add("C-11", title, CheckStatus.Failed, $"Ediția {info.Edition} nu este suportată; este necesar Standard sau Enterprise.");
        }
        else if (info.Edition.Contains("Developer", StringComparison.OrdinalIgnoreCase))
        {
            run.Add("C-11", title, CheckStatus.Warning, $"{info.Edition}: doar pentru test.");
        }
        else
        {
            run.Add("C-11", title, CheckStatus.Passed, info.Edition);
        }
    }

    private static void CheckOperatorRights(Run run, SqlServerInfo info)
    {
        const string title = "Drepturile operatorului";
        if (info.IsSysadmin)
        {
            run.Add("C-12", title, CheckStatus.Passed, "Operatorul are rolul sysadmin.");
        }
        else
        {
            // Joburile SQL Agent cu proprietarul sa si xp_create_subdir cer sysadmin (8.13); dbcreator + securityadmin nu ajung.
            run.Add("C-12", title, CheckStatus.Failed,
                "Operatorul trebuie să aibă pe SQL Server rolul sysadmin, pe durata instalării sau a actualizării.");
        }
    }

    private static async Task CheckAgentAsync(Run run, ISqlServerProbe sql, CancellationToken ct)
    {
        var status = await sql.GetAgentStatusAsync(ct).ConfigureAwait(false);
        var running = string.Equals(status, "Running", StringComparison.Ordinal);
        run.Add("C-13", "SQL Server Agent pornit", running ? CheckStatus.Passed : CheckStatus.Failed,
            running ? "SQL Server Agent rulează." : "Porniți SQL Server Agent și setați-l pe Automatic.");
    }

    private static async Task CheckDatabaseAsync(Run run, ISqlServerProbe sql, CancellationToken ct)
    {
        const string c15 = "Baza nu există";
        const string c16 = "Collation-ul bazei existente";
        var name = run.Target.Sql.DatabaseName;
        var database = await sql.GetDatabaseAsync(name, ct).ConfigureAwait(false);
        var installing = run.Target.Mode is CheckMode.Check or CheckMode.Install;

        if (database is null)
        {
            if (installing)
            {
                run.Add("C-15", c15, CheckStatus.Passed, $"Baza {name} nu există; va fi creată.");
                run.Add("C-16", c16, CheckStatus.NotApplicable, "Baza nu există.");
            }
            else
            {
                run.Add("C-15", c15, CheckStatus.NotApplicable, "Se aplică doar la instalare.");
                run.Add("C-16", c16, CheckStatus.Failed, $"Baza {name} nu există pe {run.Target.Sql.Server}.");
            }

            return;
        }

        var collationOk = string.Equals(database.Collation, RequiredCollation, StringComparison.Ordinal);
        if (installing)
        {
            var resumed = run.ExistingInstance is { State: InstanceLifecycle.Installing } existing
                && existing.InstallId != Guid.Empty
                && string.Equals(database.ErpInstallId, existing.InstallId.ToString(), StringComparison.OrdinalIgnoreCase);

            if (resumed)
            {
                run.Add("C-15", c15, CheckStatus.Passed, $"Baza {name} a fost creată de această instalare; instalarea se reia.");
            }
            else if (database.UserTableCount > 0)
            {
                run.Add("C-15", c15, CheckStatus.Failed,
                    $"Baza {name} există și conține {database.UserTableCount} tabele; instalarea nouă nu suprascrie o bază existentă.",
                    ExitCodes.DatabaseExistsNotEmpty);
                run.Add("C-16", c16, CheckStatus.NotApplicable, "Baza nu va fi folosită.");
                return;
            }
            else
            {
                run.Add("C-15", c15, CheckStatus.Warning, $"Baza {name} există și este goală; va fi folosită dacă are collation-ul corect.");
            }
        }
        else
        {
            run.Add("C-15", c15, CheckStatus.NotApplicable, "Se aplică doar la instalare.");
        }

        run.Add("C-16", c16, collationOk ? CheckStatus.Passed : CheckStatus.Failed,
            collationOk
                ? $"Baza {name} are collation {RequiredCollation}."
                : $"Baza {name} are collation {database.Collation}; este necesar {RequiredCollation}. Collation-ul unei baze existente nu se schimbă.",
            collationOk ? null : ExitCodes.WrongDatabaseCollation);
    }

    private static async Task CheckSqlPathsAsync(Run run, ISqlServerProbe sql, SqlServerInfo info, CancellationToken ct)
    {
        var target = run.Target;
        var paths = new List<(string Name, string? Path)>
        {
            ("date", target.DataFilePath ?? info.DefaultDataPath),
            ("log", target.LogFilePath ?? info.DefaultLogPath),
            ("backup", target.BackupRoot),
        };

        var problems = new List<string>();
        var created = new List<string>();
        foreach (var (name, path) in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                problems.Add($"calea de {name} nu este cunoscută");
                continue;
            }

            if (target.Mode is CheckMode.Install or CheckMode.Upgrade)
            {
                await sql.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
                continue;
            }

            switch (await sql.GetDirectoryStateAsync(path, ct).ConfigureAwait(false))
            {
                case SqlDirectoryState.WillBeCreated:
                    created.Add(path);
                    break;
                case SqlDirectoryState.Unreachable:
                    problems.Add($"calea de {name} {path} nu este accesibilă pentru SQL Server");
                    break;
            }
        }

        if (problems.Count > 0)
        {
            run.Add("C-17", "Căile SQL de date, log și backup", CheckStatus.Failed, Capitalize(string.Join("; ", problems)) + ".");
        }
        else if (created.Count > 0)
        {
            run.Add("C-17", "Căile SQL de date, log și backup", CheckStatus.Warning,
                $"Folderele {string.Join(", ", created)} nu există; vor fi create la instalare cu contul serviciului SQL Server.");
        }
        else
        {
            run.Add("C-17", "Căile SQL de date, log și backup", CheckStatus.Passed, "Căile există și sunt accesibile pentru SQL Server.");
        }
    }

    private static async Task CheckSqlDiskAsync(Run run, ISqlServerProbe sql, SqlServerInfo info, CancellationToken ct)
    {
        const string title = "Spațiu pe disc la SQL";
        var target = run.Target;
        string path;
        long required;
        string what;
        if (target.Mode == CheckMode.Upgrade)
        {
            var database = await sql.GetDatabaseAsync(target.Sql.DatabaseName, ct).ConfigureAwait(false);
            path = target.BackupRoot;
            required = (long)Math.Ceiling((database?.SizeBytes ?? 0) * 1.2);
            what = "backup (1,2 × dimensiunea bazei)";
        }
        else if (target.Mode == CheckMode.Repair)
        {
            run.Add("C-18", title, CheckStatus.NotApplicable, "Nu se aplică la reparare.");
            return;
        }
        else
        {
            path = target.DataFilePath ?? info.DefaultDataPath ?? string.Empty;
            required = 2L * target.InitialDataSizeMb * 1024 * 1024;
            what = "date (2 × initialDataSizeMb)";
        }

        var free = await sql.GetFreeSpaceBytesAsync(path, ct).ConfigureAwait(false);
        if (free is null)
        {
            run.Add("C-18", title, CheckStatus.Warning, $"Spațiul liber pe volumul {path} nu a putut fi determinat; verificați manual {required / (double)GiB:F1} GB pentru {what}.");
        }
        else if (free < required)
        {
            run.Add("C-18", title, CheckStatus.Failed,
                $"{free / (double)GiB:F1} GB liberi pe volumul {path}; sunt necesari {required / (double)GiB:F1} GB pentru {what}.");
        }
        else
        {
            run.Add("C-18", title, CheckStatus.Passed, $"{free / (double)GiB:F1} GB liberi pe volumul {path} ({what}: {required / (double)GiB:F1} GB).");
        }
    }

    private async Task<(CheckStatus, string)> CheckInternetAsync(Run run, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(10);
        var anaf = network.CanReachAsync(HttpMethod.Head, new Uri("https://api.anaf.ro/"), timeout, ct);
        var bnr = network.CanReachAsync(HttpMethod.Get, new Uri("https://www.bnr.ro/nbrfxrates.xml"), timeout, ct);
        await Task.WhenAll(anaf, bnr).ConfigureAwait(false);

        var unreachable = new List<string>();
        if (!anaf.Result)
        {
            unreachable.Add("api.anaf.ro");
        }

        if (!bnr.Result)
        {
            unreachable.Add("www.bnr.ro");
        }

        return unreachable.Count == 0
            ? (CheckStatus.Passed, "ANAF și BNR sunt accesibile.")
            : (CheckStatus.Warning, $"Nu răspund: {string.Join(", ", unreachable)}. Integrările ANAF/BNR nu vor funcționa până la deschiderea ieșirii HTTPS (443) sau configurarea proxy-ului.");
    }

    private (CheckStatus, string, int?) CheckTls(Run run) => windows.IsTls12ClientDisabled()
        ? (CheckStatus.Failed, "TLS 1.2 este dezactivat în registru (SCHANNEL\\Protocols\\TLS 1.2\\Client); activați-l.", null)
        : (CheckStatus.Passed, "TLS 1.2 este activ.", null);

    private (CheckStatus, string, int?) CheckLicense(Run run)
    {
        var path = run.Target.LicenseFilePath;
        if (!File.Exists(path))
        {
            return (CheckStatus.Failed, $"Fișierul de licență nu există: {path}", ExitCodes.InvalidLicense);
        }

        var result = licenseVerifier.Verify(File.ReadAllText(path));
        if (!result.IsValid)
        {
            return (CheckStatus.Failed, result.Message, ExitCodes.InvalidLicense);
        }

        return result.UnknownModules.Count > 0
            ? (CheckStatus.Warning, $"{result.Message} Module necunoscute de această versiune: {string.Join(", ", result.UnknownModules)}.", null)
            : (CheckStatus.Passed, result.Message, null);
    }

    private (CheckStatus, string, int?) CheckInstanceIsNew(Run run)
    {
        var target = run.Target;
        if (target.Mode is not (CheckMode.Check or CheckMode.Install))
        {
            return (CheckStatus.NotApplicable, "Se aplică doar la instalare.", null);
        }

        if (run.ExistingInstance is { State: InstanceLifecycle.Installing } existing
            && string.Equals(existing.Instance, target.Instance, StringComparison.Ordinal))
        {
            return (CheckStatus.Passed, $"Instalarea întreruptă a instanței {target.Instance} va fi reluată.", null);
        }

        var found = new List<string>();
        if (run.ExistingInstance is not null)
        {
            found.Add($"{StateStoreName(target)} (stare {run.ExistingInstance.State})");
        }

        if (windows.IisSiteExists(target.SiteName))
        {
            found.Add($"site-ul IIS {target.SiteName}");
        }

        if (windows.ServiceExists(target.ServiceName))
        {
            found.Add($"serviciul {target.ServiceName}");
        }

        return found.Count > 0
            ? (CheckStatus.Failed, $"Instanța {target.Instance} există deja: {string.Join(", ", found)}. Folosiți upgrade sau repair, ori alt nume de instanță.", null)
            : (CheckStatus.Passed, $"Instanța {target.Instance} nu există.", null);
    }

    private static string StateStoreName(CheckTarget target) => InstanceStateStore.PathFor(target.DataRoot, target.Instance);

    internal static bool DnsNameMatches(string certificateName, string hostName)
    {
        if (certificateName.StartsWith("*.", StringComparison.Ordinal))
        {
            var dot = hostName.IndexOf('.', StringComparison.Ordinal);
            return dot > 0 && string.Equals(certificateName[1..], hostName[dot..], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(certificateName, hostName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("ro-RO")) + text[1..];

    private sealed class Run(CheckTarget target, KitManifest manifest, InstanceState? existingInstance, List<CheckResult> results)
    {
        public CheckTarget Target { get; } = target;

        public KitManifest Manifest { get; } = manifest;

        public InstanceState? ExistingInstance { get; } = existingInstance;

        public List<CheckResult> Results { get; } = results;

        public void Add(string id, string title, CheckStatus status, string message, int? exitCode = null)
        {
            if (Results.Any(r => r.Id == id))
            {
                return;
            }

            Results.Add(new CheckResult(id, title, status, message, status == CheckStatus.Failed ? exitCode : null));
        }
    }
}

/// <summary>Rezultatul verificarilor si codul de iesire (6.4, 7).</summary>
public sealed record RequirementsReport(IReadOnlyList<CheckResult> Results)
{
    public bool HasFailures => Results.Any(r => r.Status == CheckStatus.Failed);

    public bool HasWarnings => Results.Any(r => r.Status == CheckStatus.Warning);

    /// <summary>
    /// Primul esec (in ordinea ID-urilor) decide codul: 10 (C-01, C-02), 31 (C-15), 32 (C-16), 40 (C-22), altfel 12.
    /// Fara esecuri: 1 daca exista avertismente, altfel 0.
    /// </summary>
    public int ExitCode
    {
        get
        {
            var firstFailure = Results.FirstOrDefault(r => r.Status == CheckStatus.Failed);
            if (firstFailure is not null)
            {
                return firstFailure.ExitCode ?? ExitCodes.RequirementsNotMet;
            }

            return HasWarnings ? ExitCodes.SuccessWithWarnings : ExitCodes.Success;
        }
    }
}
