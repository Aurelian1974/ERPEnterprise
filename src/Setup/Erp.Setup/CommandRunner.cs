using System.Runtime.Versioning;
using System.Security.Principal;
using Erp.Setup.Cli;
using Erp.Setup.Core;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Logging;
using Erp.Setup.Install;
using Erp.Setup.Windows.Security;

namespace Erp.Setup;

/// <summary>Executa o comanda: drepturi, jurnal, verificarea kitului (I-01/U-01), apoi comanda propriu-zisa.</summary>
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
            exitCode = Run(invocation, log, logFiles, console, folder => moveLogsTo = folder);
        }

        // 6.6: dupa crearea instantei, jurnalul installer-ului ajunge in DataRoot\<I>\logs\setup.
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
        log.Information("ErpSetup {Version}, comanda {Command}, instanța {Instance}, unattended {Unattended}",
            typeof(CommandRunner).Assembly.GetName().Version, invocation.Command, invocation.Instance, invocation.Unattended);

        // 4.3: kitul si installer-ul se verifica inainte de orice alta actiune.
        var kitRoot = AppContext.BaseDirectory;
        var kit = new KitVerifier(KitPublicKey.Load()).Verify(kitRoot);
        if (!kit.Succeeded)
        {
            log.Error("Verificarea kitului a eșuat ({ExitCode}): {Message} {Details}", kit.ExitCode, kit.Message, kit.Details);
            console.Error(kit.Message!);
            foreach (var detail in kit.Details)
            {
                console.Error($"  - {detail}");
            }

            return kit.ExitCode;
        }

        if (!invocation.DevSkipSignature && !AuthenticodeVerifier.IsTrusted(Environment.ProcessPath!))
        {
            log.Error("Semnătura Authenticode a installer-ului este invalidă");
            console.Error("Installer-ul nu este semnat de producător sau a fost modificat.");
            return ExitCodes.InvalidKitSignature;
        }

        if (invocation.DevSkipSignature)
        {
            log.Warning("Verificarea Authenticode a fost sărită (--dev-skip-signature, build Debug)");
            console.Warning("Atenție: verificarea Authenticode a fost sărită (build de dezvoltare).");
        }

        log.Information("Kit verificat: {Product} {Version}", kit.Manifest!.Product, kit.Manifest.Version);
        console.Info($"Kit verificat: {kit.Manifest.Product} {kit.Manifest.Version}.");

        // La check, drepturile de administrator sunt verificarea C-01, raportata impreuna cu celelalte.
        if (invocation.Command == "check")
        {
            return new CheckCommand(time)
                .RunAsync(invocation, kit.Manifest, log, console, Path.GetDirectoryName(logFiles.TextPath)!)
                .GetAwaiter().GetResult();
        }

        if (!IsAdministrator())
        {
            log.Error("Installer-ul nu rulează ca administrator");
            console.Error("ErpSetup trebuie rulat ca administrator.");
            return ExitCodes.UnsupportedOsOrNotAdministrator;
        }

        if (invocation.Command == "install")
        {
            var install = new InstallCommand(time);
            var exitCode = install.RunAsync(invocation, kitRoot, kit.Manifest, log, console).GetAwaiter().GetResult();
            if (install.SetupLogDirectory is not null)
            {
                moveLogsTo(install.SetupLogDirectory);
            }

            return exitCode;
        }

        console.Error($"Comanda „{invocation.Command}” nu este încă implementată în această versiune a installer-ului.");
        return ExitCodes.CommandNotImplemented;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
