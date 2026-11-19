using System.Runtime.Versioning;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Probes;

namespace Erp.Setup.Cli;

/// <summary>
/// Comanda <c>check</c> (08, 6.1) si prima parte a comenzii <c>install</c>: fisierul de raspunsuri, apoi verificarile C-01…C-20.
/// Nu modifica nimic.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CheckCommand(CommandContext context)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var (request, exitCode) = LoadRequest(context);
        if (request is null)
        {
            return exitCode;
        }

        var (target, report) = await RunChecksAsync(context, request, CheckMode.Check, ct).ConfigureAwait(false);
        var setupLogs = Path.Combine(request.InstanceDirectory, "logs", "setup");
        var reportPath = RequirementsReportFile.Write(Directory.Exists(setupLogs) ? setupLogs : context.TemporaryLogDirectory, report, target,
            context.Time.GetUtcNow());
        context.Log.Information("Rezultatul verificărilor: {Path}, cod {ExitCode}", reportPath, report.ExitCode);
        context.Console.Info($"Rezultat salvat în {reportPath}");
        return report.ExitCode;
    }

    /// <summary>Citeste si valideaza fisierul de raspunsuri (cod 11 la orice camp invalid, 6.2).</summary>
    public static (InstallRequest? Request, int ExitCode) LoadRequest(CommandContext context)
    {
        var loaded = AnswersValidator.Load(context.Invocation.AnswersPath!);
        foreach (var warning in loaded.Warnings)
        {
            context.Log.Warning("Răspunsuri {Field}: {Message}", warning.Field, warning.Message);
            context.Console.Warning($"  {warning.Field}: {warning.Message}");
        }

        if (!loaded.IsValid)
        {
            context.Log.Error("Fișierul de răspunsuri este invalid: {Errors}", loaded.Errors.Select(e => $"{e.Field}: {e.Message}"));
            context.Console.Error($"Fișierul de răspunsuri este invalid (cod {ExitCodes.InvalidAnswersFile}):");
            foreach (var issue in loaded.Errors)
            {
                context.Console.Error($"  {issue.Field}: {issue.Message}");
            }

            return (null, ExitCodes.InvalidAnswersFile);
        }

        return (loaded.Request, ExitCodes.Success);
    }

    public static async Task<(CheckTarget Target, RequirementsReport Report)> RunChecksAsync(CommandContext context, InstallRequest request, CheckMode mode,
        CancellationToken ct)
    {
        var target = CheckTarget.From(request, mode);
        var existing = InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance));
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(new WindowsProbe(), new SqlServerConnector(), network,
            new LicenseReader(LicenseKeys.Embedded, context.Time), context.Time);

        context.Console.Info("Verific cerințele...");
        // Kitul a fost verificat de CommandRunner inainte de orice comanda (C-05).
        var report = await checker.RunAsync(target, context.Manifest, new KitCheck(true, $"{context.Manifest.Product} {context.Manifest.Version}"),
            existing, ct).ConfigureAwait(false);
        foreach (var result in report.Results)
        {
            context.Log.Information("[{Id}] {Status} {Title}: {Message}", result.Id, result.Status, result.Title, result.Message);
        }

        RequirementsReportPrinter.Print(context.Console, report);
        return (target, report);
    }
}
