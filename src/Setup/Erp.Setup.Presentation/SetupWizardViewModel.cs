using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Presentation;

/// <summary>
/// Asistentul ErpSetup.exe (08, 5.3): ecranele 0–6. Valorile ecranelor devin un fisier de raspunsuri si trec prin acelasi
/// AnswersValidator ca la linia de comanda, deci ambele invelisuri construiesc acelasi InstallRequest si acelasi plan (R13, IA-13).
/// Fiecare eroare afisata contine codul de iesire corespunzator (6.3).
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISetupBackend backend;
    private readonly ISetupDialogs dialogs;
    private readonly Func<Action<StepEvent>, IProgress<StepEvent>> progressFactory;
    private RequirementsReport? report;
    private InstallRequest? installedRequest;

    public SetupWizardViewModel(ISetupBackend backend, ISetupDialogs dialogs, Func<Action<StepEvent>, IProgress<StepEvent>>? progressFactory = null)
    {
        this.backend = backend;
        this.dialogs = dialogs;
        this.progressFactory = progressFactory ?? (apply => new Progress<StepEvent>(apply));
        HostName = backend.DefaultHostName;
        Pages = Options.Pages();
        Installations = new ObservableCollection<InstallationItem>(
            backend.FindInstallations().Select(s => new InstallationItem(s, backend.Manifest.Version)));
        SelectedInstallation = Installations.FirstOrDefault();
        UpdatePages();
    }

    public ObservableCollection<PageItem> Pages { get; }

    public string KitVersion => backend.Manifest.Version;

    public string CurrentUser => backend.CurrentUser;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial WizardPage CurrentPage { get; set; } = WizardPage.Welcome;

    partial void OnCurrentPageChanged(WizardPage value) => UpdatePages();

    // ---- Ecranul 0: Bun venit ----

    public ObservableCollection<InstallationItem> Installations { get; }

    public bool HasInstallations => Installations.Count > 0;

    [ObservableProperty]
    public partial InstallationItem? SelectedInstallation { get; set; }

    [RelayCommand]
    private void NewInstallation()
    {
        Instance = Installations.Any(i => i.Instance == InstallRequest.DefaultInstance) ? "TEST" : InstallRequest.DefaultInstance;
        CurrentPage = WizardPage.Database;
    }

    /// <summary>„Reia instalarea” (state = Installing): raspunsurile salvate la I-18 nu exista inca, deci se reiau din instance.json.</summary>
    [RelayCommand]
    private void ResumeInstallation()
    {
        if (SelectedInstallation?.State is not { } state)
        {
            return;
        }

        Instance = state.Instance;
        Server = state.SqlServer ?? Server;
        Database = state.DatabaseName ?? Database;
        HostName = state.HostName ?? HostName;
        HttpsPort = state.HttpsPort ?? HttpsPort;
        HttpRedirect = state.HttpRedirect;
        InstallRoot = state.InstallRoot;
        if (state.AppIdentity != AppIdentity.VirtualAccounts)
        {
            RemoteIdentity = Options.RemoteIdentities.Single(o => o.Identity == state.AppIdentity);
            GmsaAccount = state.AppIdentityAccount;
        }

        CurrentPage = WizardPage.Database;
    }

    [RelayCommand]
    private void LoadAnswers()
    {
        var path = dialogs.AskOpenAnswers();
        if (path is not null)
        {
            LoadAnswersFrom(path);
        }
    }

    /// <summary>„Încarcă un fișier de răspunsuri…” (ecranul 0): completeaza ecranele 1–3.</summary>
    public bool LoadAnswersFrom(string path)
    {
        var loaded = AnswersValidator.Load(path);
        if (!loaded.IsValid)
        {
            dialogs.ShowError("Fișier de răspunsuri invalid",
                $"{string.Join(Environment.NewLine, loaded.Errors.Select(e => $"{e.Field}: {e.Message}"))}{Environment.NewLine}(cod {ExitCodes.InvalidAnswersFile})");
            return false;
        }

        Apply(loaded.Request!);
        CurrentPage = WizardPage.Database;
        return true;
    }

    // ---- Ecranul 1: Baza de date ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLeaveDatabase))]
    public partial string Server { get; set; } = "localhost";

    partial void OnServerChanged(string value) => SqlTest = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLeaveDatabase))]
    public partial string Database { get; set; } = InstallRequest.DefaultDatabase;

    partial void OnDatabaseChanged(string value) => SqlTest = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLeaveDatabase), nameof(SqlStatus), nameof(IsSqlRemote), nameof(DefaultBackupFolder))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial SqlConnectionTest? SqlTest { get; set; }

    [ObservableProperty]
    public partial bool IsTestingSql { get; set; }

    public string SqlStatus => SqlTest switch
    {
        null => "Conexiunea nu a fost testată.",
        { Succeeded: false } failed => $"{failed.Message} (cod {ExitCodes.RequirementsNotMet})",
        { Server: { } info } ok => $"SQL Server {info.MajorVersion}, {info.Edition}, collation {info.Collation}; " +
            $"{(info.IsSysadmin ? "contul curent e sysadmin" : "contul curent NU e sysadmin")}; SQL Agent: {ok.AgentStatus ?? "—"}; " +
            $"{(ok.Database is null ? "baza nu există (se creează)" : ok.Database.IsEmpty ? "baza există și e goală (se preia)" : "baza există și conține date")}" +
            $"{(ok.IsRemote ? $"; serverul e pe alt calculator ({info.MachineName})" : string.Empty)}" +
            $"{(ok.TrustedServerCertificate ? "; ⚠ certificatul serverului SQL nu e de încredere" : string.Empty)}.",
        _ => SqlTest.Message,
    };

    public bool IsSqlRemote => SqlTest?.IsRemote == true;

    public IReadOnlyList<IdentityOption> RemoteIdentities => Options.RemoteIdentities;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGmsa), nameof(CanLeaveDatabase))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial IdentityOption RemoteIdentity { get; set; } = Options.RemoteIdentities[0];

    public bool IsGmsa => RemoteIdentity.Identity == AppIdentity.Gmsa;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLeaveDatabase))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? GmsaAccount { get; set; }

    [ObservableProperty]
    public partial string? BackupFolder { get; set; }

    public string DefaultBackupFolder => SqlTest?.Server?.DefaultBackupPath is { Length: > 0 } root
        ? Path.Combine(root, Database)
        : "folderul implicit de backup al instanței SQL";

    [ObservableProperty]
    public partial string FullBackupTime { get; set; } = "01:00";

    [ObservableProperty]
    public partial bool ShowAdvanced { get; set; }

    [ObservableProperty]
    public partial string? DataFolder { get; set; }

    [ObservableProperty]
    public partial string? LogFolder { get; set; }

    [ObservableProperty]
    public partial int LogBackupEveryMinutes { get; set; } = 15;

    [ObservableProperty]
    public partial int RetentionDays { get; set; } = 14;

    public IReadOnlyList<DayOfWeek> Days => Options.Days;

    [ObservableProperty]
    public partial DayOfWeek IntegrityCheckDay { get; set; } = DayOfWeek.Saturday;

    [ObservableProperty]
    public partial string IntegrityCheckTime { get; set; } = "02:00";

    [RelayCommand]
    private async Task TestSqlAsync(CancellationToken ct)
    {
        IsTestingSql = true;
        try
        {
            SqlTest = await backend.TestSqlConnectionAsync(Server.Trim(), Database.Trim(), ct);
        }
        finally
        {
            IsTestingSql = false;
        }
    }

    public bool CanLeaveDatabase => SqlTest is { Succeeded: true } && Issues("sql.", "backup.").Count == 0;

    // ---- Ecranul 2: Site web ----

    [ObservableProperty]
    public partial string Instance { get; set; } = InstallRequest.DefaultInstance;

    [ObservableProperty]
    public partial string HostName { get; set; }

    partial void OnHostNameChanged(string value) => Certificates.Clear();

    [ObservableProperty]
    public partial int HttpsPort { get; set; } = 443;

    [ObservableProperty]
    public partial bool HttpRedirect { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingCertificate))]
    public partial bool UseInternalCertificate { get; set; } = true;

    public bool UseExistingCertificate
    {
        get => !UseInternalCertificate;
        set => UseInternalCertificate = !value;
    }

    public ObservableCollection<CertificateChoice> Certificates { get; } = [];

    [ObservableProperty]
    public partial CertificateChoice? SelectedCertificate { get; set; }

    [ObservableProperty]
    public partial string InstallRoot { get; set; } = InstallRequest.DefaultInstallRoot;

    [RelayCommand]
    private void FindCertificates()
    {
        Certificates.Clear();
        foreach (var certificate in backend.FindCertificates(HostName.Trim()))
        {
            Certificates.Add(certificate);
        }

        SelectedCertificate = Certificates.FirstOrDefault();
    }

    // ---- Ecranul 3: Licenta ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LicenseIsValid))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? LicensePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LicenseIsValid))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial License? License { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LicenseIsValid))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? LicenseError { get; set; }

    public bool LicenseIsValid => License is not null && LicenseError is null;

    public string LicenseCompatibility => $"Compatibilă cu kitul {backend.Manifest.Version}";

    public string? LicenseFiscalCodes => License is null ? null : string.Join(", ", License.FiscalCodeList);

    public string? LicenseModules => License is null ? null : string.Join(", ", License.Modules);

    public string? LicenseValidity => License is null ? null
        : $"{(License.ValidUntil is { } until ? $"valabilă până la {until:dd.MM.yyyy}" : "fără termen")}; " +
          $"{(License.UpdatesUntil is { } updates ? $"actualizări incluse până la {updates:dd.MM.yyyy}" : "actualizări incluse fără termen")}";

    [RelayCommand]
    private void ChooseLicense()
    {
        var path = dialogs.AskOpenLicense();
        if (path is not null)
        {
            LoadLicense(path);
        }
    }

    public void LoadLicense(string path)
    {
        LicensePath = path;
        if (!File.Exists(path))
        {
            License = null;
            LicenseError = $"Fișierul de licență nu există: {path} (cod {ExitCodes.InvalidLicense})";
            return;
        }

        var result = backend.ReadLicense(path);
        if (!result.IsValid)
        {
            License = null;
            LicenseError = $"{result.Message} (cod {ExitCodes.InvalidLicense})";
        }
        else
        {
            var release = DateOnly.ParseExact(backend.Manifest.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var refused = LicensePolicy.CheckKitRelease(result.License!, release, backend.Manifest.Version);
            License = result.License;
            LicenseError = refused is null ? null : $"{refused} (cod {ExitCodes.LicenseExcludesKit})";
        }

        OnPropertyChanged(nameof(LicenseFiscalCodes));
        OnPropertyChanged(nameof(LicenseModules));
        OnPropertyChanged(nameof(LicenseValidity));
    }

    // ---- Ecranul 4: Verificare ----

    public ObservableCollection<CheckRow> CheckResults { get; } = [];

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool WarningsAccepted { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial bool HasFailures { get; set; }

    public bool HasWarnings => report?.HasWarnings == true;

    [ObservableProperty]
    public partial string? ChoicesSummary { get; set; }

    [RelayCommand]
    private async Task RunChecksAsync(CancellationToken ct)
    {
        var (request, errors) = BuildRequest();
        CheckResults.Clear();
        report = null;
        if (request is null)
        {
            HasFailures = true;
            dialogs.ShowError("Valori invalide", $"{errors}{Environment.NewLine}(cod {ExitCodes.InvalidAnswersFile})");
            return;
        }

        IsChecking = true;
        try
        {
            report = await backend.CheckAsync(request, ct);
            foreach (var result in report.Results)
            {
                CheckResults.Add(new CheckRow(result));
            }

            HasFailures = report.HasFailures;
            WarningsAccepted = false;
            ChoicesSummary = $"Instalarea {request.Instance}: {AppSettingsGenerator.PublicOrigin(request.Site.HostName, request.Site.HttpsPort)}/, " +
                $"baza {request.Sql.Database} pe {request.Sql.Server}, program în {request.InstallRoot}, " +
                $"certificat {(request.Site.Certificate == CertificateKind.Internal ? "intern" : request.Site.CertificateThumbprint)}.";
            OnPropertyChanged(nameof(HasWarnings));
            InstallCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void SaveAnswers()
    {
        var (request, errors) = BuildRequest();
        if (request is null)
        {
            dialogs.ShowError("Valori invalide", $"{errors}{Environment.NewLine}(cod {ExitCodes.InvalidAnswersFile})");
            return;
        }

        if (dialogs.AskSaveAnswers($"answers-{request.Instance}.json") is { } path)
        {
            backend.WriteAnswers(path, request);
        }
    }

    // ---- Ecranul 5: Instalare ----

    public ObservableCollection<StepRow> Steps { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(RetryCommand), nameof(BackCommand))]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string? InstallError { get; set; }

    private bool CanInstall() => report is not null && !HasFailures && !IsInstalling && (!report.HasWarnings || WarningsAccepted);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync(CancellationToken ct) => RunInstallAsync(ct);

    private bool CanRetry() => InstallError is not null && !IsInstalling;

    /// <summary>„Reîncearcă”: pasii sunt idempotenti, iar planul se reia de la primul pas neterminat (R10, 5.5).</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync(CancellationToken ct) => RunInstallAsync(ct);

    private async Task RunInstallAsync(CancellationToken ct)
    {
        var (request, _) = BuildRequest();
        if (request is null || report is null)
        {
            return;
        }

        CurrentPage = WizardPage.Install;
        Steps.Clear();
        foreach (var (id, title) in PlanBuilder.InstallSteps)
        {
            Steps.Add(new StepRow(id, title));
        }

        Progress = 0;
        InstallError = null;
        IsInstalling = true;
        try
        {
            var reporter = new ViewModelStepReporter(progressFactory(Apply));
            var outcome = await backend.InstallAsync(request, report, reporter, ct);
            Outcome = outcome;
            if (outcome.ExitCode is ExitCodes.Success or ExitCodes.SuccessWithWarnings)
            {
                installedRequest = request;
                CurrentPage = WizardPage.Done;
            }
            else
            {
                InstallError = outcome.RestartRequired
                    ? $"Windows cere repornirea calculatorului (cod {outcome.ExitCode}). Reporniți, apoi deschideți din nou ErpSetup.exe și alegeți „Reia instalarea”."
                    : $"Pasul {outcome.FailedStep} a eșuat (cod {outcome.ExitCode}): {outcome.Message}";
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void Apply(StepEvent step)
    {
        var row = Steps.FirstOrDefault(s => s.Id == step.Id);
        if (row is null)
        {
            return;
        }

        if (step.Result is null)
        {
            row.Status = StepRowStatus.Running;
            return;
        }

        row.Status = step.Result.Outcome switch
        {
            StepOutcome.Done => StepRowStatus.Done,
            StepOutcome.Warning => StepRowStatus.Warning,
            StepOutcome.Skipped => StepRowStatus.Skipped,
            _ => StepRowStatus.Failed,
        };
        row.Message = step.Result.Message;
        row.Duration = StepOrchestrator.FormatElapsed(step.Elapsed);
        Progress = 100.0 * Steps.Count(s => s.Status is not (StepRowStatus.Pending or StepRowStatus.Running)) / Steps.Count;
    }

    // ---- Ecranul 6: Gata ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplicationUrl), nameof(AdministratorUserName), nameof(TemporaryPassword), nameof(InternalCertificatePath))]
    public partial InstallOutcome? Outcome { get; set; }

    public string? ApplicationUrl => Outcome?.ApplicationUrl;

    public string? AdministratorUserName => Outcome?.AdministratorUserName;

    /// <summary>Afisata o singura data (5.10); dispare la inchiderea ferestrei.</summary>
    public string? TemporaryPassword => Outcome?.TemporaryPassword;

    public string? InternalCertificatePath => Outcome?.InternalCertificatePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    public partial bool PasswordNoted { get; set; }

    [ObservableProperty]
    public partial string RecoveryKeyPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecoveryKeyPasswordConfirm { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    public partial string? RecoveryKeyPath { get; set; }

    [ObservableProperty]
    public partial string? RecoveryKeyError { get; set; }

    public bool RecoveryKeySaved => RecoveryKeyPath is not null;

    /// <summary>„Închide” e activ doar dupa bifa „Am notat parola” si salvarea cheii de recuperare (5.3, ecranul 6).</summary>
    public bool CanClose => (PasswordNoted || TemporaryPassword is null) && RecoveryKeySaved;

    [RelayCommand]
    private void SaveRecoveryKey()
    {
        RecoveryKeyError = null;
        if (RecoveryKeyPassword.Length < RecoveryKeyPolicy.MinimumPasswordLength)
        {
            RecoveryKeyError = $"Parola are cel puțin {RecoveryKeyPolicy.MinimumPasswordLength} caractere.";
            return;
        }

        if (RecoveryKeyPassword != RecoveryKeyPasswordConfirm)
        {
            RecoveryKeyError = "Parolele diferă.";
            return;
        }

        if (installedRequest is null || dialogs.AskSaveRecoveryKey($"ERP-{installedRequest.Instance}-cheie-recuperare.pfx") is not { } path)
        {
            return;
        }

        try
        {
            backend.ExportRecoveryKey(installedRequest, path, RecoveryKeyPassword);
            RecoveryKeyPath = path;
            OnPropertyChanged(nameof(RecoveryKeySaved));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
            or InvalidOperationException or Windows.Services.WindowsSetupException)
        {
            RecoveryKeyError = $"Cheia nu a putut fi salvată: {ex.Message} (cod {ExitCodes.RecoveryKeyInvalid})";
        }
        finally
        {
            RecoveryKeyPassword = string.Empty;
            RecoveryKeyPasswordConfirm = string.Empty;
        }
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (TemporaryPassword is { } password)
        {
            dialogs.CopyToClipboard(password);
        }
    }

    [RelayCommand]
    private void OpenApplication()
    {
        if (ApplicationUrl is { } url)
        {
            dialogs.Open(url);
        }
    }

    // ---- Navigarea ----

    private bool CanGoNext() => CurrentPage switch
    {
        WizardPage.Database => CanLeaveDatabase,
        WizardPage.Site => Issues("instance", "site.").Count == 0 && (UseInternalCertificate || SelectedCertificate is not null),
        WizardPage.License => LicenseIsValid,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        CurrentPage = CurrentPage switch
        {
            WizardPage.Database => WizardPage.Site,
            WizardPage.Site => WizardPage.License,
            _ => WizardPage.Checks,
        };
        if (CurrentPage == WizardPage.Checks)
        {
            CheckResults.Clear();
            report = null;
            HasFailures = false;
        }
    }

    private bool CanGoBack() => CurrentPage is WizardPage.Database or WizardPage.Site or WizardPage.License or WizardPage.Checks && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentPage = CurrentPage - 1;

    /// <summary>Mesajele de validare ale campurilor unui ecran (aceleasi reguli ca fisierul de raspunsuri, 6.2).</summary>
    public string FieldErrors(params string[] prefixes) => string.Join(Environment.NewLine, Issues(prefixes).Select(i => i.Message));

    public string DatabaseErrors => FieldErrors("sql.", "backup.");

    public string SiteErrors => FieldErrors("instance", "site.");

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(DatabaseErrors) or nameof(SiteErrors) or nameof(CurrentPage) or nameof(Outcome) or nameof(CanClose)
            or nameof(Progress) or nameof(IsInstalling) or nameof(Steps))
        {
            return;
        }

        OnPropertyChanged(nameof(DatabaseErrors));
        OnPropertyChanged(nameof(SiteErrors));
        NextCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AnswersIssue> Issues(params string[] prefixes)
    {
        var issues = AnswersValidator.Validate(BuildAnswers(), backend.DataRoot).Errors;
        return [.. issues.Where(i => prefixes.Any(p => i.Field.StartsWith(p, StringComparison.Ordinal)))];
    }

    /// <summary>Valorile ecranelor, in formatul fisierului de raspunsuri (6.2).</summary>
    public AnswersFile BuildAnswers()
    {
        var identity = IsSqlRemote ? RemoteIdentity.Identity : AppIdentity.VirtualAccounts;
        var accepted = WarningsAccepted && report is not null
            ? report.Results.Where(r => r.Status == CheckStatus.Warning).Select(r => r.Id).ToList()
            : [];
        return new AnswersFile
        {
            FormatVersion = AnswersFile.CurrentFormatVersion,
            Instance = Instance.Trim(),
            Sql = new AnswersFile.SqlSection
            {
                Server = Server.Trim(),
                Database = Database.Trim(),
                DataFolder = Blank(DataFolder),
                LogFolder = Blank(LogFolder),
                AppIdentity = identity.ToString(),
                GmsaAccount = identity == AppIdentity.Gmsa ? Blank(GmsaAccount) : null,
            },
            Backup = new AnswersFile.BackupSection
            {
                Folder = Blank(BackupFolder),
                FullBackupTime = FullBackupTime.Trim(),
                LogBackupEveryMinutes = LogBackupEveryMinutes,
                RetentionDays = RetentionDays,
                IntegrityCheckDay = IntegrityCheckDay.ToString(),
                IntegrityCheckTime = IntegrityCheckTime.Trim(),
            },
            Site = new AnswersFile.SiteSection
            {
                HostName = HostName.Trim(),
                HttpsPort = HttpsPort,
                HttpRedirect = HttpRedirect,
                Certificate = new AnswersFile.CertificateSection
                {
                    Kind = UseInternalCertificate ? nameof(CertificateKind.Internal) : nameof(CertificateKind.Existing),
                    Thumbprint = UseInternalCertificate ? null : SelectedCertificate?.Thumbprint,
                },
                InstallRoot = InstallRoot.Trim(),
            },
            License = new AnswersFile.LicenseSection { File = LicensePath },
            AcceptedWarnings = accepted,
        };
    }

    /// <summary>Acelasi InstallRequest ca la linia de comanda (R13): valorile trec prin AnswersValidator.</summary>
    public (InstallRequest? Request, string Errors) BuildRequest()
    {
        var result = AnswersValidator.Validate(BuildAnswers(), backend.DataRoot);
        return (result.IsValid ? result.Request : null, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Field}: {e.Message}")));
    }

    private void Apply(InstallRequest request)
    {
        Instance = request.Instance;
        Server = request.Sql.Server;
        Database = request.Sql.Database;
        DataFolder = request.Sql.DataFolder;
        LogFolder = request.Sql.LogFolder;
        if (request.Sql.AppIdentity != AppIdentity.VirtualAccounts)
        {
            RemoteIdentity = Options.RemoteIdentities.Single(o => o.Identity == request.Sql.AppIdentity);
            GmsaAccount = request.Sql.GmsaAccount;
        }

        BackupFolder = request.Backup.Folder;
        FullBackupTime = request.Backup.FullBackupTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        LogBackupEveryMinutes = request.Backup.LogBackupEveryMinutes;
        RetentionDays = request.Backup.RetentionDays;
        IntegrityCheckDay = request.Backup.IntegrityCheckDay;
        IntegrityCheckTime = request.Backup.IntegrityCheckTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        HostName = request.Site.HostName;
        HttpsPort = request.Site.HttpsPort;
        HttpRedirect = request.Site.HttpRedirect;
        UseInternalCertificate = request.Site.Certificate == CertificateKind.Internal;
        if (request.Site.CertificateThumbprint is { } thumbprint)
        {
            var existing = backend.FindCertificates(request.Site.HostName).FirstOrDefault(c => c.Thumbprint == thumbprint);
            Certificates.Clear();
            if (existing is not null)
            {
                Certificates.Add(existing);
            }

            SelectedCertificate = existing;
        }

        InstallRoot = request.InstallRoot;
        LoadLicense(request.LicenseFile);
    }

    private void UpdatePages()
    {
        foreach (var page in Pages)
        {
            page.IsCurrent = page.Page == CurrentPage;
            page.IsDone = page.Page < CurrentPage;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Un eveniment al orchestratorului: inceputul (fara rezultat) sau sfarsitul unui pas.</summary>
public sealed record StepEvent(string Id, StepResult? Result, TimeSpan Elapsed);

/// <summary>Raportarea pasilor spre fereastra (5.2): orchestratorul ruleaza in fundal, IProgress trece pe firul ferestrei.</summary>
public sealed class ViewModelStepReporter(IProgress<StepEvent> progress) : IStepReporter
{
    public void Started(IInstallStep step) => progress.Report(new StepEvent(step.Id, null, TimeSpan.Zero));

    public void Finished(IInstallStep step, StepResult result, TimeSpan elapsed) => progress.Report(new StepEvent(step.Id, result, elapsed));
}
