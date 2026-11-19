using System.Runtime.Versioning;
using System.Text.Json;
using Erp.Licensing;
using Erp.Setup.Cli;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Core.State;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Probes;
using Serilog;

namespace Erp.Setup;

/// <summary>Comanda <c>check</c> (7): fisierul de raspunsuri, parola SQL a operatorului daca e cazul, C-01…C-23.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CheckCommand(TimeProvider time)
{
    public async Task<int> RunAsync(SetupInvocation invocation, KitManifest manifest, ILogger log, OperatorConsole console, string logDirectory)
    {
        var loaded = AnswersValidator.Load(invocation.AnswersPath!);
        foreach (var warning in loaded.Warnings)
        {
            log.Warning("Răspunsuri {Field}: {Message}", warning.Field, warning.Message);
            console.Warning($"  {warning.Field}: {warning.Message}");
        }

        if (!loaded.IsValid)
        {
            log.Error("Fișierul de răspunsuri este invalid: {Errors}", loaded.Errors.Select(e => $"{e.Field}: {e.Message}"));
            console.Error("Fișierul de răspunsuri este invalid:");
            foreach (var issue in loaded.Errors)
            {
                console.Error($"  {issue.Field}: {issue.Message}");
            }

            return ExitCodes.InvalidAnswersFile;
        }

        var answers = loaded.Answers!;
        var target = CheckTarget.FromAnswers(answers, CheckMode.Check);

        string? password = null;
        if (answers.Sql.OperatorAuthentication == SqlAuthentication.Sql)
        {
            var fromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SecretReader.OperatorPasswordVariable));
            var secret = new SecretReader(Environment.GetEnvironmentVariable, new ConsoleSecretInput())
                .ReadOperatorSqlPassword(unattended: fromEnvironment || invocation.Unattended);
            if (secret.Outcome != SecretOutcome.Provided)
            {
                console.Error(secret.Message ?? "Parola SQL a operatorului lipsește.");
                return secret.ExitCode;
            }

            password = secret.Value;
        }

        var existing = ReadExistingInstance(answers, log);
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(new WindowsProbe(), new SqlServerConnector(), network,
            new LicenseReader(LicenseKeys.Embedded, time), time);

        console.Info("Verific cerințele...");
        var report = await checker.RunAsync(target, manifest, existing, password, CancellationToken.None).ConfigureAwait(false);
        foreach (var result in report.Results)
        {
            log.Information("[{Id}] {Status} {Title}: {Message}", result.Id, result.Status, result.Title, result.Message);
        }

        RequirementsReportPrinter.Print(console, report);

        var setupLogs = Path.Combine(answers.DataRoot, answers.Instance, "logs", "setup");
        var reportPath = RequirementsReportFile.Write(Directory.Exists(setupLogs) ? setupLogs : logDirectory, report, target, time.GetUtcNow());
        log.Information("Rezultatul verificărilor: {Path}, cod {ExitCode}", reportPath, report.ExitCode);
        console.Info($"Rezultat salvat în {reportPath}");
        return report.ExitCode;
    }

    private static InstanceState? ReadExistingInstance(InstallAnswers answers, ILogger log)
    {
        var path = InstanceStateStore.PathFor(answers.DataRoot, answers.Instance);
        try
        {
            return InstanceStateStore.Read(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Un instance.json ilizibil inseamna tot o instanta existenta (C-23).
            log.Warning(ex, "{Path} nu poate fi citit", path);
            return new InstanceState
            {
                Instance = answers.Instance,
                State = InstanceLifecycle.Failed,
                InstallRoot = answers.InstallRoot,
                DataRoot = answers.DataRoot,
            };
        }
    }
}
