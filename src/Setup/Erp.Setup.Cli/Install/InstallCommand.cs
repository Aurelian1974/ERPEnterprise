using System.Runtime.Versioning;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Cli;

/// <summary>
/// Comanda <c>install</c> (08, 6.1): raspunsurile, verificarile, apoi planul I-01…I-18 (acelasi cu al ferestrei), parola
/// administratorului afisata o singura data si exportul obligatoriu al cheii de recuperare.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InstallCommand(CommandContext context, Action<string> moveLogsTo)
{
    public const string RecoveryKeyPasswordVariable = "ERP_RECOVERY_KEY_PASSWORD";

    private readonly OperatorConsole console = context.Console;
    private readonly SetupInvocation invocation = context.Invocation;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (invocation.Unattended && string.IsNullOrWhiteSpace(invocation.RecoveryKeyOut))
        {
            console.Error($"Cu --unattended, opțiunea --recovery-key-out este obligatorie (cod {ExitCodes.InvalidAnswersFile}).");
            return ExitCodes.InvalidAnswersFile;
        }

        var (request, exitCode) = CheckCommand.LoadRequest(context);
        if (request is null)
        {
            return exitCode;
        }

        var (recoveryKeyPath, recoveryKeyPassword, keyExitCode) = ReadRecoveryKeyDestination();
        if (recoveryKeyPath is null || recoveryKeyPassword is null)
        {
            return keyExitCode;
        }

        var (_, report) = await CheckCommand.RunChecksAsync(context, request, CheckMode.Install, ct).ConfigureAwait(false);
        if (report.HasFailures)
        {
            console.Error($"Cerințele nu sunt îndeplinite (cod {report.ExitCode}).");
            return report.ExitCode;
        }

        var unaccepted = report.UnacceptedWarnings(request.AcceptedWarnings);
        if (!Confirm(request, unaccepted))
        {
            return invocation.Unattended ? ExitCodes.RequirementsNotMet : ExitCodes.CancelledByOperator;
        }

        var sql = new SqlOperator(new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance, report.TrustServerCertificate), context.Log);
        var session = InstallSession.Start(request, context.Manifest, context.KitRoot, sql, report, invocation.Unattended);
        if (session.Resuming)
        {
            console.Warning($"Instalarea întreruptă a instanței {request.Instance} se reia (pași terminați: {string.Join(", ", session.State!.CompletedSteps)}).");
        }

        var windows = new WindowsSetup(context.Log, context.Time);
        var plan = PlanBuilder.Install(session, windows, new LicenseReader(LicenseKeys.Embedded, context.Time), context.Time);
        var setupContext = new SetupContext { Log = context.Log, KitRoot = context.KitRoot, Unattended = invocation.Unattended };
        var result = await new StepOrchestrator(new ConsoleStepReporter(console), session).RunAsync(plan, setupContext, ct).ConfigureAwait(false);
        if (session.State is not null)
        {
            moveLogsTo(session.Paths.SetupLogs);
        }

        if (!result.Succeeded)
        {
            context.Log.Error("Instalarea s-a oprit la {Step}, cod {ExitCode}", result.FailedStep, result.ExitCode);
            console.Error($"Instalarea s-a oprit la pasul {result.FailedStep} (cod {result.ExitCode}).");
            if (session.State is not null && result.ExitCode != ExitCodes.CancelledByOperator)
            {
                console.Info("Corectați cauza și rulați din nou aceeași comandă: instalarea se reia de unde a rămas.");
            }

            return result.ExitCode;
        }

        ShowAdministrator(session);
        return ExportRecoveryKey(session, windows, recoveryKeyPath, recoveryKeyPassword) ?? result.ExitCode;
    }

    /// <summary>
    /// Cu --unattended, parola cheii vine din variabila de mediu a sesiunii, stearsa imediat dupa citire; altfel se cere de doua
    /// ori, cu ecou ascuns. Minimum 12 caractere (5.3, ecranul 6).
    /// </summary>
    private (string? Path, string? Password, int ExitCode) ReadRecoveryKeyDestination()
    {
        if (invocation.Unattended)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(RecoveryKeyPasswordVariable);
            Environment.SetEnvironmentVariable(RecoveryKeyPasswordVariable, null);
            if (fromEnvironment is null || fromEnvironment.Length < RecoveryKeyPolicy.MinimumPasswordLength)
            {
                console.Error($"Variabila {RecoveryKeyPasswordVariable} lipsește sau are sub {RecoveryKeyPolicy.MinimumPasswordLength} caractere " +
                    $"(cod {ExitCodes.InvalidAnswersFile}).");
                return (null, null, ExitCodes.InvalidAnswersFile);
            }

            return (Path.GetFullPath(invocation.RecoveryKeyOut!), fromEnvironment, ExitCodes.Success);
        }

        var path = invocation.RecoveryKeyOut;
        if (string.IsNullOrWhiteSpace(path))
        {
            console.Info("Cheia de recuperare (5.12) se salvează la final, separat de backup-uri (de preferat pe un stick USB).");
            Console.Write("Fișierul .pfx al cheii de recuperare: ");
            path = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return (null, null, ExitCodes.CancelledByOperator);
            }
        }

        var input = new ConsoleSecretInput();
        while (true)
        {
            var first = input.ReadHidden($"Parola cheii de recuperare (minimum {RecoveryKeyPolicy.MinimumPasswordLength} caractere): ");
            if (first is null)
            {
                return (null, null, ExitCodes.CancelledByOperator);
            }

            if (first.Length < RecoveryKeyPolicy.MinimumPasswordLength)
            {
                console.Warning($"Parola are sub {RecoveryKeyPolicy.MinimumPasswordLength} caractere.");
                continue;
            }

            if (input.ReadHidden("Repetați parola: ") != first)
            {
                console.Warning("Parolele diferă.");
                continue;
            }

            return (Path.GetFullPath(path), first, ExitCodes.Success);
        }
    }

    private bool Confirm(InstallRequest request, IReadOnlyList<CheckResult> unaccepted)
    {
        if (invocation.Unattended)
        {
            if (unaccepted.Count == 0)
            {
                return true;
            }

            // 6.2: un avertisment nu se accepta niciodata tacut.
            console.Error($"Avertismente neacceptate în acceptedWarnings: {string.Join(", ", unaccepted.Select(w => w.Id))} (cod {ExitCodes.RequirementsNotMet}).");
            return false;
        }

        console.Info(string.Empty);
        console.Info($"Se instalează ERP {context.Manifest.Version}, instalarea {request.Instance}: " +
            $"{AppSettingsGenerator.PublicOrigin(request.Site.HostName, request.Site.HttpsPort)}/, baza {request.Sql.Database} pe {request.Sql.Server}.");
        return Ask(unaccepted.Count > 0 ? "Am citit avertismentele. Continuați instalarea?" : "Continuați instalarea?");
    }

    private void ShowAdministrator(InstallSession session)
    {
        var url = AppSettingsGenerator.PublicOrigin(session.Request.Site.HostName, session.Request.Site.HttpsPort) + "/";
        if (session.Administrator is not { Created: true } administrator)
        {
            console.Info($"Aplicația: {url}");
            return;
        }

        if (session.InitialAdminFilePath is not null)
        {
            // 5.10: la --unattended parola merge doar in fisierul protejat; calea se afiseaza, parola nu.
            console.Warning($"Parola temporară a administratorului a fost scrisă în {session.InitialAdminFilePath} (Administrators, SYSTEM); " +
                "fișierul se șterge după prima schimbare a parolei.");
            return;
        }

        // Singura afisare a parolei: doar pe ecran, niciodata in jurnal.
        console.Info(string.Empty);
        console.Info(InitialAdminSummary.Render(url, administrator.UserName, administrator.TemporaryPassword));
        console.Info("La prima autentificare vi se cere o parolă nouă și activarea autentificării în 2 pași (2FA), apoi porniți asistentul de configurare.");
        if (session.RequireState().CertificateKind == CertificateKind.Internal)
        {
            console.Info($"Certificatul intern: distribuiți {session.Paths.InternalCertificate} pe calculatoarele utilizatorilor.");
        }

        if (!Ask("Am notat parola. Continuați?"))
        {
            console.Warning("Parola nu mai poate fi afișată; folosiți „reset-admin” dacă nu ați notat-o.");
        }
    }

    private int? ExportRecoveryKey(InstallSession session, IWindowsSetup windows, string path, string password)
    {
        try
        {
            RecoveryKeys.Export(windows, session.StatePath, path, password, context.Time);
            console.Info($"Cheia de recuperare a fost salvată în {path}. Păstrați-o separat de backup-uri, cu parola în managerul de parole.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
            or Windows.Services.WindowsSetupException)
        {
            context.Log.Error(ex, "Exportul cheii de recuperare a eșuat");
            console.Error($"Cheia de recuperare nu a putut fi salvată: {ex.Message} Rulați „export-recovery-key --instance {session.Request.Instance} " +
                $"--out <fișier.pfx>” (cod {ExitCodes.RecoveryKeyInvalid}).");
            return ExitCodes.RecoveryKeyInvalid;
        }
    }

    private bool Ask(string question)
    {
        console.Info($"{question} [d/N]");
        var answer = Console.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("d", StringComparison.OrdinalIgnoreCase) || answer.Equals("da", StringComparison.OrdinalIgnoreCase));
    }
}
