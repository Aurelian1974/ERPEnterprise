using System.Globalization;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Agent;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Licensing;
using Erp.Setup.Sql.Logins;

namespace Erp.Setup.Engine;

/// <summary>
/// Planul instalarii (08, 5.5): aceeasi lista de pasi pentru fereastra si pentru linia de comanda (R13). Ordinea respecta
/// dependentele (D24): functiile Windows care pot cere repornire primele, identitatile Windows inaintea drepturilor si
/// loginurilor, nimic din SQL inainte ca programul sa fie pe disc si verificat. Toti pasii sunt idempotenti (R10).
/// </summary>
public static class PlanBuilder
{
    /// <summary>Pasii instalarii, in ordine, cu titlurile afisate pe ecranul 5 si in consola (5.5).</summary>
    public static readonly IReadOnlyList<(string Id, string Title)> InstallSteps =
    [
        ("I-01", "Pregătesc folderele instanței și instance.json"),
        ("I-02", "Activez funcțiile IIS lipsă"),
        ("I-03", "Instalez ASP.NET Core Hosting Bundle, dacă lipsește"),
        ("I-04", "Pregătesc certificatul HTTPS"),
        ("I-05", "Copiez programul și verific hash-urile"),
        ("I-06", "Creez site-ul IIS și serviciul Windows (oprite)"),
        ("I-07", "Scriu configurarea, web.config și drepturile NTFS"),
        ("I-08", "Creez certificatul de protecție a datelor"),
        ("I-09", "Creez baza de date (case sensitive)"),
        ("I-10", "Creez schemele și rolurile bazei"),
        ("I-11", "Creez loginurile Windows ale aplicației"),
        ("I-12", "Rulez migrațiile, obiectele programabile și datele livrate"),
        ("I-13", "Salvez licența în bază"),
        ("I-14", "Creez utilizatorul Administrator"),
        ("I-15", "Creez regulile de firewall"),
        ("I-16", "Creez joburile de backup și fac primul backup complet"),
        ("I-17", "Pornesc aplicația și verific health-check-ul"),
        ("I-18", "Finalizez instalarea"),
    ];

    /// <summary>Identificatorii pasilor, in ordine; testul IA-13 compara planurile celor doua invelisuri.</summary>
    public static IReadOnlyList<string> InstallStepIds => [.. InstallSteps.Select(s => s.Id)];

    /// <summary>Planul pentru instalare, reparare sau recuperare (dupa <see cref="InstallSession.Mode"/>).</summary>
    public static IReadOnlyList<PlannedStep> Install(InstallSession session, IWindowsSetup windows, ILicenseReader licenses, TimeProvider time) =>
    [
        Step("I-01", ExitCodes.IisServiceOrFileError, (_, _) => Prepare(session, windows)),
        Step("I-02", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(EnableIis(session, windows))),
        Step("I-03", ExitCodes.IisServiceOrFileError,
            (_, _) => Task.FromResult(InstallHostingBundle(session, windows))),
        Step("I-04", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(HttpsCertificate(session, windows, time))),
        Step("I-05", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            var copied = windows.DeployBinaries(session.KitRoot, session.Manifest, session.Request);
            return Task.FromResult(StepResult.Done($"{copied} fișiere copiate în versions\\{session.Version}"));
        }),
        Step("I-06", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            windows.CreateSiteAndService(session.Request, session.Version, session.RequireState().CertificateThumbprint
                ?? throw new InvalidOperationException("Certificatul HTTPS lipsește (I-04)."));
            return Task.FromResult(StepResult.Done($"{session.Request.SiteName}, {session.Request.ServiceName}"));
        }),
        Step("I-07", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            windows.WriteConfiguration(session.KitRoot, session.Request, session.Version, SettingsValues(session));
            return Task.FromResult(StepResult.Done(session.Paths.AppSettings));
        }),
        Step("I-08", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(DataProtection(session, windows))),
        Step("I-09", ExitCodes.SqlInstallError, (_, ct) => CreateDatabaseAsync(session, ct)),
        Step("I-10", ExitCodes.SqlInstallError, async (_, ct) =>
        {
            var count = await new BootstrapRunner(session.Sql).RunAsync(session.Database, Path.Combine(session.DatabaseRoot, "bootstrap"), ct)
                .ConfigureAwait(false);
            return StepResult.Done($"{count} scripturi");
        }),
        Step("I-11", ExitCodes.SqlInstallError, (_, ct) => CreateLoginsAsync(session, windows, ct)),
        Step("I-12", ExitCodes.SqlInstallError, (context, _) =>
            Task.FromResult(RunMigrations(session, context))),
        Step("I-13", ExitCodes.InvalidLicense, (_, ct) => session.Mode switch
        {
            SetupMode.Install => SaveLicenseAsync(session, licenses, session.Request.LicenseFile, checkKitRelease: true, ct),
            SetupMode.Repair when session.LicenseOverride is { } file => SaveLicenseAsync(session, licenses, file, checkKitRelease: false, ct),
            _ => Task.FromResult(StepResult.Skipped("licența e în bază")),
        }),
        Step("I-14", ExitCodes.SqlInstallError, (_, ct) => session.Mode == SetupMode.Install
            ? CreateAdministratorAsync(session, ct)
            : Task.FromResult(StepResult.Skipped("administratorul există; utilizatorii nu se modifică"))),
        Step("I-15", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            windows.ApplyFirewall(session.Request);
            return Task.FromResult(StepResult.Done(session.Request.Site.HttpRedirect
                ? $"TCP {session.Request.Site.HttpsPort} și 80"
                : $"TCP {session.Request.Site.HttpsPort}"));
        }),
        new PlannedStep(new DelegateStep("I-16", Title("I-16"), (_, ct) => BackupAsync(session, ct)),
            ExitCodes.SqlInstallError, SkipWhenCompleted: true),
        Step("I-17", ExitCodes.HealthCheckFailed, (_, ct) => StartAsync(session, windows, ct)),
        Step("I-18", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(Finish(session, windows, time))),
    ];

    public static BackupSettings BackupSettings(InstallRequest request, string backupFolder) => new(request.Sql.Database, backupFolder,
        request.Backup.FullBackupTime, request.Backup.LogBackupEveryMinutes, request.Backup.RetentionDays, request.Backup.IntegrityCheckDay,
        request.Backup.IntegrityCheckTime);

    public static InstallationSettingsValues SettingsValues(InstallSession session)
    {
        var request = session.Request;
        var state = session.RequireState();
        return new InstallationSettingsValues(request.Instance, session.Version,
            AppSettingsGenerator.PublicOrigin(request.Site.HostName, request.Site.HttpsPort), session.Paths.InstanceDirectory, request.Sql.Server,
            request.Sql.Database, state.TrustServerCertificate, state.DataProtectionCertificateThumbprint);
    }

    /// <summary>
    /// I-08 (5.12): instalarea creeaza certificatul; „Repară” il cere existent (cod 80, altfel „Recuperează”); „Recuperează” il
    /// importa din cheia de recuperare sau, fara ea, creeaza unul nou (datele criptate cu inelul vechi raman ilizibile).
    /// </summary>
    private static StepResult DataProtection(InstallSession session, IWindowsSetup windows)
    {
        var existing = session.RequireState().DataProtectionCertificateThumbprint;
        string thumbprint;
        var warning = false;
        switch (session.Mode)
        {
            case SetupMode.Repair:
                if (existing is null || !windows.DataProtectionCertificateExists(existing))
                {
                    return StepResult.Failed("Certificatul de protecție a datelor lipsește; un certificat nou nu poate citi inelul de chei existent. " +
                        "Folosiți „Recuperează o instalare existentă” cu cheia de recuperare.", ExitCodes.RecoveryKeyInvalid);
                }

                thumbprint = windows.EnsureDataProtectionCertificate(session.Request, existing);
                break;
            case SetupMode.Recover when session.RecoveryKey is { } key:
                thumbprint = windows.ImportDataProtectionCertificate(session.Request, key.PfxPath, key.Password);
                break;
            case SetupMode.Recover:
                thumbprint = windows.EnsureDataProtectionCertificate(session.Request, null);
                warning = true;
                break;
            default:
                thumbprint = windows.EnsureDataProtectionCertificate(session.Request, existing);
                break;
        }

        session.UpdateState(state => state with { DataProtectionCertificateThumbprint = thumbprint });
        windows.WriteConfiguration(session.KitRoot, session.Request, session.Version, SettingsValues(session));
        return warning
            ? StepResult.Warning($"certificat nou {thumbprint}, fără cheia de recuperare: datele criptate anterior (2FA, secretele din Configurare) nu mai pot fi citite")
            : StepResult.Done(thumbprint);
    }

    private static Task<StepResult> Prepare(InstallSession session, IWindowsSetup windows)
    {
        var request = session.Request;
        windows.CreateDataFolders(request);
        if (session.State is not null)
        {
            return Task.FromResult(StepResult.Done($"reluare, installId {session.InstallId}"));
        }

        session.CreateState(new InstanceState
        {
            Instance = request.Instance,
            InstallId = session.InstallId,
            State = InstanceLifecycle.Installing,
            InstallRoot = request.InstallRoot,
            SiteName = request.SiteName,
            HostName = request.Site.HostName,
            HttpsPort = request.Site.HttpsPort,
            HttpRedirect = request.Site.HttpRedirect,
            CertificateKind = request.Site.Certificate,
            ServiceName = request.ServiceName,
            SqlServer = request.Sql.Server,
            DatabaseName = request.Sql.Database,
            SqlIsRemote = session.SqlIsRemote,
            TrustServerCertificate = session.Sql.Target.TrustServerCertificate,
            AppIdentity = request.Sql.AppIdentity,
            AppIdentityAccount = request.Sql.AppIdentity == AppIdentity.Gmsa ? request.Sql.GmsaAccount : null,
            BackupFolder = session.SqlIsExpress ? null : session.BackupFolder,
            BackupEdition = session.SqlIsExpress ? BackupEdition.None : BackupEdition.Agent,
            FullBackupTime = request.Backup.FullBackupTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            LogBackupEveryMinutes = request.Backup.LogBackupEveryMinutes,
            RetentionDays = request.Backup.RetentionDays,
            IntegrityCheckDay = request.Backup.IntegrityCheckDay,
            IntegrityCheckTime = request.Backup.IntegrityCheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            CompletedSteps = [.. session.CompletedThisRun],
        });
        return Task.FromResult(StepResult.Done($"instalare nouă, installId {session.InstallId}"));
    }

    private static StepResult EnableIis(InstallSession session, IWindowsSetup windows)
    {
        var missing = windows.MissingIisFeatures();
        if (missing.Count == 0)
        {
            return StepResult.Skipped("funcțiile IIS sunt deja active");
        }

        if (windows.EnableIisFeatures(missing))
        {
            session.RestartRequired = true;
            return RestartRequired($"{missing.Count} funcții IIS activate");
        }

        return StepResult.Done($"{missing.Count} funcții activate");
    }

    private static StepResult InstallHostingBundle(InstallSession session, IWindowsSetup windows)
    {
        if (windows.IsHostingBundleInstalled(session.Manifest.HostingBundleVersion))
        {
            return StepResult.Skipped("Hosting Bundle e deja instalat");
        }

        if (windows.InstallHostingBundle(session.KitRoot))
        {
            session.RestartRequired = true;
            return RestartRequired("Hosting Bundle instalat");
        }

        return StepResult.Done($"Hosting Bundle {session.Manifest.HostingBundleVersion}");
    }

    /// <summary>Repornirea opreste instalarea; dupa repornire installer-ul o reia de la acest pas (C-06).</summary>
    private static StepResult RestartRequired(string what) =>
        StepResult.Failed($"{what}; Windows cere repornirea calculatorului. Reporniți, apoi instalarea se reia de la acest pas.",
            ExitCodes.RequirementsNotMet);

    private static StepResult HttpsCertificate(InstallSession session, IWindowsSetup windows, TimeProvider time)
    {
        var site = session.Request.Site;
        if (site.Certificate == CertificateKind.Existing)
        {
            var thumbprint = site.CertificateThumbprint ?? throw new InvalidOperationException("Amprenta certificatului ales lipsește.");
            var info = windows.FindMachineCertificate(thumbprint);
            if (info is null || !info.HasPrivateKey || !info.HasServerAuthenticationEku || info.NotAfter <= time.GetUtcNow())
            {
                return StepResult.Failed($"Certificatul {thumbprint} lipsește, nu are cheie privată, nu e pentru autentificarea serverului sau a expirat.");
            }

            session.UpdateState(state => state with { CertificateKind = CertificateKind.Existing, CertificateThumbprint = thumbprint });
            return StepResult.Done($"certificatul ales, valabil până la {info.NotAfter:dd.MM.yyyy}");
        }

        var existing = session.RequireState().CertificateThumbprint;
        if (existing is not null && windows.FindMachineCertificate(existing) is { } current)
        {
            // „Repară” (11.1) regenereaza certificatul intern cu cel putin 60 de zile inainte de expirare; trebuie redistribuit.
            if (session.Mode != SetupMode.Repair || current.NotAfter > time.GetUtcNow().AddDays(60))
            {
                return StepResult.Skipped($"certificatul intern există ({existing})");
            }
        }

        var created = windows.CreateInternalHttpsCertificate(session.Request);
        session.UpdateState(state => state with { CertificateKind = CertificateKind.Internal, CertificateThumbprint = created });
        return StepResult.Done($"certificat intern {created}; distribuiți {session.Paths.InternalCertificate} pe calculatoarele utilizatorilor");
    }

    private static async Task<StepResult> CreateDatabaseAsync(InstallSession session, CancellationToken ct)
    {
        var request = session.Request;
        var outcome = await new DatabaseCreator(session.Sql).CreateAsync(
            new DatabaseCreateOptions(request.Sql.Database, request.Instance, session.InstallId, request.Sql.DataFolder, request.Sql.LogFolder,
                Recover: session.Mode == SetupMode.Recover), ct)
            .ConfigureAwait(false);
        return outcome switch
        {
            DatabaseCreateOutcome.Created => StepResult.Done($"baza {request.Sql.Database} creată"),
            DatabaseCreateOutcome.AdoptedEmpty => StepResult.Done($"baza goală {request.Sql.Database} a fost preluată"),
            DatabaseCreateOutcome.Recovered => StepResult.Done($"baza ERP {request.Sql.Database} a fost preluată"),
            _ => StepResult.Done($"baza {request.Sql.Database} verificată"),
        };
    }

    private static async Task<StepResult> CreateLoginsAsync(InstallSession session, IWindowsSetup windows, CancellationToken ct)
    {
        var request = session.Request;
        var account = request.Sql.AppIdentity switch
        {
            AppIdentity.Gmsa => request.Sql.GmsaAccount,
            AppIdentity.ComputerAccount => windows.ComputerAccount(),
            _ => null,
        };
        var principals = AppPrincipals.For(request.Instance, request.Sql.AppIdentity, account);
        await new LoginManager(session.Sql).EnsureAsync(request.Sql.Database, principals, ct).ConfigureAwait(false);
        var logins = principals.Select(p => p.Login).ToList();
        session.UpdateState(state => state with
        {
            AppSqlLogins = logins,
            AppIdentityAccount = request.Sql.AppIdentity == AppIdentity.VirtualAccounts ? null : account,
        });
        return StepResult.Done(string.Join(", ", logins));
    }

    private static StepResult RunMigrations(InstallSession session, SetupContext context)
    {
        var runner = new MigrationRunner(context.Log);
        var connection = session.Sql.ConnectionString(session.Database);
        var scripts = session.Scripts;
        var migrations = runner.RunMigrations(connection, scripts);
        var programmability = runner.RunProgrammability(connection, scripts);
        var seed = runner.RunSeed(connection, scripts);
        session.UpdateState(state => state with { LastMigration = scripts.LastMigration });
        return StepResult.Done($"{migrations.ScriptsExecuted} migrații noi din {scripts.Migrations.Count}, " +
            $"{programmability.ScriptsExecuted} obiecte, {seed.ScriptsExecuted} scripturi de date");
    }

    /// <summary>I-13; la „Repară --license” dreptul la actualizari nu se verifica (IA-15: codul 41 nu opreste repararea).</summary>
    private static async Task<StepResult> SaveLicenseAsync(InstallSession session, ILicenseReader licenses, string file, bool checkKitRelease,
        CancellationToken ct)
    {
        var installer = new LicenseInstaller(licenses, session.Sql);
        var verified = checkKitRelease
            ? installer.Verify(file, DateOnly.ParseExact(session.Manifest.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), session.Version)
            : installer.Verify(file);
        await using var connection = await session.Sql.OpenAsync(session.Database, ct).ConfigureAwait(false);
        await installer.SaveAsync(connection, verified, ct).ConfigureAwait(false);
        var result = verified.Result;
        return result.UnknownModules.Count > 0
            ? StepResult.Warning($"{result.Message} Module necunoscute: {string.Join(", ", result.UnknownModules)}.")
            : StepResult.Done(result.Message);
    }

    private static async Task<StepResult> CreateAdministratorAsync(InstallSession session, CancellationToken ct)
    {
        await using var connection = await session.Sql.OpenAsync(session.Database, ct).ConfigureAwait(false);
        session.Administrator = await new InitialAdminCreator(session.Sql).CreateAsync(connection,
            new InitialAdminRequest(InstallRequest.AdministratorUserName, InstallRequest.AdministratorDisplayName, null, session.Version,
                Environment.MachineName), ct).ConfigureAwait(false);
        return session.Administrator.Created
            ? StepResult.Done($"{InstallRequest.AdministratorUserName} (parolă temporară, schimbare și 2FA obligatorii)")
            : StepResult.Warning(InitialAdminCreator.SkippedMessage + " Pentru o parolă nouă folosiți „Resetează accesul administratorului”.");
    }

    private static async Task<StepResult> BackupAsync(InstallSession session, CancellationToken ct)
    {
        if (session.SqlIsExpress)
        {
            return StepResult.Warning("SQL Express nu are SQL Agent: instalarea nu are backup automat.");
        }

        var folder = session.BackupFolder ?? throw new InvalidOperationException("Folderul de backup nu a fost stabilit (C-16).");
        var changes = await new AgentJobInstaller(session.Sql).EnsureAsync(BackupSettings(session.Request, folder), ct).ConfigureAwait(false);
        if (session.Mode == SetupMode.Repair)
        {
            return StepResult.Done($"{changes.Count} joburi verificate");
        }

        var run = await new AgentJobRunner(session.Sql).RunAndWaitAsync(AgentJobDefinitions.FullBackupJob(session.Database), ct).ConfigureAwait(false);
        return run.Succeeded
            ? StepResult.Done($"{changes.Count} joburi; primul backup: {run.Message}")
            : StepResult.Failed($"Primul backup complet a eșuat: {run.Message}", ExitCodes.SqlInstallError);
    }

    private static async Task<StepResult> StartAsync(InstallSession session, IWindowsSetup windows, CancellationToken ct)
    {
        windows.StartApplication(session.Request, session.Version);
        var state = session.RequireState();
        var pinned = state.CertificateKind == CertificateKind.Internal ? state.CertificateThumbprint : null;
        var outcome = await windows.CheckHealthAsync(session.Request, session.Version, session.Manifest.LastMigration, pinned, ct).ConfigureAwait(false);
        if (outcome.Succeeded)
        {
            return StepResult.Done(outcome.Message);
        }

        var tail = string.Join(Environment.NewLine, windows.ComponentLogTail(session.Request));
        return StepResult.Failed($"Health-check eșuat: {outcome.Message}{Environment.NewLine}{tail}", ExitCodes.HealthCheckFailed);
    }

    private static StepResult Finish(InstallSession session, IWindowsSetup windows, TimeProvider time)
    {
        session.UpdateState(state => state with
        {
            State = InstanceLifecycle.Installed,
            CurrentVersion = session.Version,
            InstalledAt = state.InstalledAt ?? time.GetUtcNow(),
            LastMigration = session.Manifest.LastMigration ?? state.LastMigration,
        });
        if (session.Mode != SetupMode.Install)
        {
            AnswersFile.Write(session.Paths.Answers, session.Request);
            return StepResult.Done(session.Mode == SetupMode.Repair ? $"instalarea {session.Request.Instance} a fost reparată"
                : $"instalarea {session.Request.Instance} a fost recuperată");
        }

        AnswersFile.Write(session.Paths.Answers, session.Request);

        if (session.Unattended && session.Administrator is { Created: true, TemporaryPassword: not null } administrator)
        {
            var url = AppSettingsGenerator.PublicOrigin(session.Request.Site.HostName, session.Request.Site.HttpsPort) + "/";
            session.InitialAdminFilePath = windows.WriteInitialAdminFile(session.Request,
                InitialAdminSummary.Render(url, administrator.UserName, administrator.TemporaryPassword));
        }

        return StepResult.Done($"instalarea {session.Request.Instance} {session.Version} s-a încheiat");
    }

    private static PlannedStep Step(string id, int failureExitCode, Func<SetupContext, CancellationToken, Task<StepResult>> execute) =>
        new(new DelegateStep(id, Title(id), execute), failureExitCode);

    private static string Title(string id) => InstallSteps.Single(s => s.Id == id).Title;
}
