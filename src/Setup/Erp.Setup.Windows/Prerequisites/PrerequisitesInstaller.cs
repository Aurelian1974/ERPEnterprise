using System.ServiceProcess;
using Erp.Setup.Core.Checks;
using Erp.Setup.Windows.Processes;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Windows.Prerequisites;

public enum PrerequisiteResult
{
    AlreadyPresent,
    Installed,
    RestartRequired,
}

/// <summary>
/// Pasul I-04 (C-05, C-06): instaleaza rolurile IIS lipsa (Install-WindowsFeature, lista fixa din C-05) si Hosting Bundle-ul
/// din kit (/install /quiet /norestart, apoi iisreset /restart). O repornire necesara opreste instalarea cu codul 12.
/// </summary>
public sealed class PrerequisitesInstaller(IProcessRunner processes)
{
    public PrerequisiteResult InstallIisFeatures(IReadOnlyCollection<string> missing)
    {
        if (missing.Count == 0)
        {
            return PrerequisiteResult.AlreadyPresent;
        }

        // Numele vin doar din lista fixa C-05, deci comanda nu contine text primit din afara.
        var unknown = missing.Where(f => !RequirementsChecker.IisFeatures.Contains(f)).ToList();
        if (unknown.Count > 0)
        {
            throw new WindowsSetupException($"Funcții Windows necunoscute: {string.Join(", ", unknown)}.");
        }

        var result = processes.Run(ProcessRunner.System32(@"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command", InstallFeaturesCommand(missing)]);
        if (result.ExitCode != 0)
        {
            throw new WindowsSetupException($"Install-WindowsFeature a eșuat (cod {result.ExitCode}): {result.Output}");
        }

        return result.Output.Contains("Yes", StringComparison.OrdinalIgnoreCase) ? PrerequisiteResult.RestartRequired : PrerequisiteResult.Installed;
    }

    public static string InstallFeaturesCommand(IReadOnlyCollection<string> features) =>
        $"(Install-WindowsFeature -Name {string.Join(',', features)}).RestartNeeded";

    public PrerequisiteResult InstallHostingBundle(string kitRoot)
    {
        var folder = Path.Combine(kitRoot, "prerequisites");
        var installer = Directory.Exists(folder) ? Directory.GetFiles(folder, "dotnet-hosting-*-win.exe").SingleOrDefault() : null;
        if (installer is null)
        {
            throw new WindowsSetupException("Kitul nu conține ASP.NET Core Hosting Bundle (prerequisites\\dotnet-hosting-*-win.exe).");
        }

        var result = processes.Run(installer, ["/install", "/quiet", "/norestart"]);
        if (result.ExitCode is not (0 or 3010))
        {
            throw new WindowsSetupException($"Instalarea Hosting Bundle a eșuat (cod {result.ExitCode}).");
        }

        var reset = processes.Run(ProcessRunner.System32("iisreset.exe"), ["/restart"]);
        if (reset.ExitCode != 0)
        {
            throw new WindowsSetupException($"iisreset /restart a eșuat (cod {reset.ExitCode}): {reset.Output}");
        }

        return result.ExitCode == 3010 ? PrerequisiteResult.RestartRequired : PrerequisiteResult.Installed;
    }
}

/// <summary>Pornirea si oprirea serviciului (I-23, U-07, U-13, R-01, R-05).</summary>
public static class ServiceControl
{
    public static void Start(string serviceName, TimeSpan timeout)
    {
        using var service = new ServiceController(serviceName);
        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status is not (ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending))
            {
                service.Start();
            }

            WaitFor(service, ServiceControllerStatus.Running, timeout);
        }
    }

    public static void Stop(string serviceName, TimeSpan timeout)
    {
        using var service = new ServiceController(serviceName);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            if (service.Status != ServiceControllerStatus.StopPending)
            {
                service.Stop();
            }

            WaitFor(service, ServiceControllerStatus.Stopped, timeout);
        }
    }

    private static void WaitFor(ServiceController service, ServiceControllerStatus status, TimeSpan timeout)
    {
        try
        {
            service.WaitForStatus(status, timeout);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new WindowsSetupException($"Serviciul {service.ServiceName} nu a ajuns în starea {status} în {timeout.TotalSeconds:F0} s.", ex);
        }
    }
}
