using System.Runtime.Versioning;
using System.Security.Principal;
using Erp.Setup.Core;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Logging;
using Erp.Setup.Windows.Security;

namespace Erp.Setup.Cli;

/// <summary>Executa o comanda: jurnalul, verificarea kitului si a installer-ului, drepturile, apoi comanda propriu-zisa.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CommandRunner(TimeProvider time)
{
    public int Run(SetupInvocation invocation)
    {
        var logFiles = SetupLog.CreateFiles(SetupLog.DefaultTempDirectory, time.GetUtcNow());
        var console = new OperatorConsole(Console.Out, Console.Error, !invocation.NoColor && !Console.IsOutputRedirected);
        string? moveLogsTo = null;
        int exitCode;
        using (var log = SetupLog.Create(logFiles, SetupLog.ParseLevel(invocation.LogLevel)))
        {
            try
            {
                exitCode = Run(invocation, log, logFiles, console, folder => moveLogsTo = folder);
            }
            catch (Exception ex)
            {
                log.Fatal(ex, "Eroare neașteptată");
                console.Error($"Eroare neașteptată: {ex.Message}");
                exitCode = ex.GetType().GetProperty("ExitCode")?.GetValue(ex) is int code ? code : ExitCodes.IisServiceOrFileError;
            }
        }

        // 08, 5.2: dupa crearea instantei, jurnalul installer-ului ajunge in C:\ProgramData\Erp\<I>\logs\setup.
        var finalPath = logFiles.TextPath;
        if (moveLogsTo is not null && Directory.Exists(moveLogsTo))
        {
            try
            {
                finalPath = SetupLog.MoveFiles(logFiles, moveLogsTo).TextPath;
            }
            catch (IOException ex)
            {
                console.Warning($"Jurnalul nu a putut fi mutat în {moveLogsTo}: {ex.Message}");
            }
        }

        console.Info($"Jurnal: {finalPath}");
        return exitCode;
    }

    private int Run(SetupInvocation invocation, Serilog.ILogger log, SetupLogFiles logFiles, OperatorConsole console, Action<string> moveLogsTo)
    {
        log.Information("ErpSetup.Cli {Version}, comanda {Command}, instanța {Instance}, unattended {Unattended}",
            typeof(CommandRunner).Assembly.GetName().Version, invocation.Command, invocation.Instance, invocation.Unattended);

        // C-05: kitul si installer-ul se verifica inainte de orice alta actiune.
        var kitRoot = AppContext.BaseDirectory;
        var kit = new KitVerifier(KitPublicKey.Load()).Verify(kitRoot);
        if (!kit.Succeeded)
        {
            log.Error("Verificarea kitului a eșuat ({ExitCode}): {Message} {Details}", kit.ExitCode, kit.Message, kit.Details);
            console.Error($"{kit.Message} (cod {kit.ExitCode})");
            foreach (var detail in kit.Details)
            {
                console.Error($"  - {detail}");
            }

            return kit.ExitCode;
        }

        if (!invocation.DevSkipSignature && !AuthenticodeVerifier.IsTrusted(Environment.ProcessPath!))
        {
            log.Error("Semnătura Authenticode a installer-ului este invalidă");
            console.Error($"Installer-ul nu este semnat de producător sau a fost modificat (cod {ExitCodes.InvalidKitSignature}).");
            return ExitCodes.InvalidKitSignature;
        }

        if (invocation.DevSkipSignature)
        {
            log.Warning("Verificarea Authenticode a fost sărită (--dev-skip-signature, build Debug)");
            console.Warning("Atenție: verificarea Authenticode a fost sărită (build de dezvoltare).");
        }

        var manifest = kit.Manifest!;
        log.Information("Kit verificat: {Product} {Version}", manifest.Product, manifest.Version);
        console.Info($"Kit verificat: {manifest.Product} {manifest.Version}.");
        var context = new CommandContext(invocation, kitRoot, manifest, log, console, time, Path.GetDirectoryName(logFiles.TextPath)!);

        // La check, drepturile de administrator sunt verificarea C-01, raportata impreuna cu celelalte.
        if (invocation.Command == "check")
        {
            return new CheckCommand(context).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        if (!IsAdministrator())
        {
            log.Error("Installer-ul nu rulează ca administrator");
            console.Error($"ErpSetup.Cli trebuie rulat dintr-o consolă deschisă ca administrator (cod {ExitCodes.UnsupportedOsOrNotAdministrator}).");
            return ExitCodes.UnsupportedOsOrNotAdministrator;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var exitCode = invocation.Command switch
            {
                "install" => new InstallCommand(context, moveLogsTo).RunAsync(cancellation.Token).GetAwaiter().GetResult(),
                "status" => InstanceCommands.Status(context),
                "export-recovery-key" => InstanceCommands.ExportRecoveryKey(context),
                "verify-recovery-key" => InstanceCommands.VerifyRecoveryKey(context),
                _ => NotAvailable(context),
            };
            log.Information("Comanda {Command} s-a încheiat cu codul {ExitCode}", invocation.Command, exitCode);
            return exitCode;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static int NotAvailable(CommandContext context)
    {
        context.Console.Error($"Comanda „{context.Invocation.Command}” nu este disponibilă în această versiune a installer-ului.");
        return ExitCodes.InvalidCommandLine;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>Ce primeste fiecare comanda de la <see cref="CommandRunner"/>.</summary>
internal sealed record CommandContext(
    SetupInvocation Invocation,
    string KitRoot,
    KitManifest Manifest,
    Serilog.ILogger Log,
    OperatorConsole Console,
    TimeProvider Time,
    string TemporaryLogDirectory);
