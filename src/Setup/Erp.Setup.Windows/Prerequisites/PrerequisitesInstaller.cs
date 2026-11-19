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
/// Pasii I-02 si I-03 (5.11, C-06, C-07): activeaza functiile IIS lipsa prin DISM (aceleasi nume pe Windows 10/11 si Server) si
/// instaleaza Hosting Bundle-ul din kit (/install /quiet /norestart, apoi iisreset /restart). Codul 3010 = repornire necesara.
/// </summary>
public sealed class PrerequisitesInstaller(IProcessRunner processes)
{
    public const int RestartRequiredExitCode = 3010;

    public PrerequisiteResult EnableIisFeatures(IReadOnlyCollection<string> missing)
    {
        if (missing.Count == 0)
        {
            return PrerequisiteResult.AlreadyPresent;
        }

        // Numele vin doar din lista fixa (5.11), deci comanda nu contine text primit din afara.
        var unknown = missing.Where(f => !RequirementsChecker.IisFeatures.Contains(f)).ToList();
        if (unknown.Count > 0)
        {
            throw new WindowsSetupException($"Funcții Windows necunoscute: {string.Join(", ", unknown)}.");
        }

        var result = processes.Run(ProcessRunner.System32("dism.exe"), EnableFeaturesArguments(missing));
        return result.ExitCode switch
        {
            0 => PrerequisiteResult.Installed,
            RestartRequiredExitCode => PrerequisiteResult.RestartRequired,
            _ => throw new WindowsSetupException($"dism /Enable-Feature a eșuat (cod {result.ExitCode}): {result.Output}"),
        };
    }

    public static IReadOnlyList<string> EnableFeaturesArguments(IEnumerable<string> features) =>
        ["/Online", "/Enable-Feature", .. features.Select(f => $"/FeatureName:{f}"), "/All", "/NoRestart"];

    public PrerequisiteResult InstallHostingBundle(string kitRoot)
    {
        var folder = Path.Combine(kitRoot, "prerequisites");
        var installer = Directory.Exists(folder) ? Directory.GetFiles(folder, "dotnet-hosting-*-win.exe").SingleOrDefault() : null;
        if (installer is null)
        {
            throw new WindowsSetupException("Kitul nu conține ASP.NET Core Hosting Bundle (prerequisites\\dotnet-hosting-*-win.exe).");
        }

        var result = processes.Run(installer, ["/install", "/quiet", "/norestart"]);
        if (result.ExitCode is not (0 or RestartRequiredExitCode))
        {
            throw new WindowsSetupException($"Instalarea Hosting Bundle a eșuat (cod {result.ExitCode}).");
        }

        var reset = processes.Run(ProcessRunner.System32("iisreset.exe"), ["/restart"]);
        if (reset.ExitCode != 0)
        {
            throw new WindowsSetupException($"iisreset /restart a eșuat (cod {reset.ExitCode}): {reset.Output}");
        }

        return result.ExitCode == RestartRequiredExitCode ? PrerequisiteResult.RestartRequired : PrerequisiteResult.Installed;
    }
}

/// <summary>Pornirea si oprirea serviciului (I-17, actualizarea si revenirea, 10.2).</summary>
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
