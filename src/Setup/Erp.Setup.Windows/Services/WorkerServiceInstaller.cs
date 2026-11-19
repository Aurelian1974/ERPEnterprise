using Erp.Setup.Windows.Processes;

namespace Erp.Setup.Windows.Services;

/// <summary>Serviciul ErpWorker-&lt;Instanta&gt; (I-06, 5.11; actualizarea si revenirea, 10.2).</summary>
public sealed record WorkerServiceSpec(string Instance, string WorkerExecutable, string InstanceDirectory, string? GmsaAccount = null)
{
    public string Name => $"ErpWorker-{Instance}";

    public string DisplayName => $"ERP Worker ({Instance})";

    public string Account => GmsaAccount ?? $@"NT SERVICE\{Name}";

    public string BinaryPath => $"\"{WorkerExecutable}\" --instance-dir \"{InstanceDirectory.TrimEnd('\\')}\"";
}

/// <summary>Comenzile sc.exe (5.11), construite ca liste de argumente; numele parametrilor sc.exe includ „=”.</summary>
public static class ServiceCommands
{
    public const string Description = "Procesare în fundal ERP: outbox, joburi Hangfire, sincronizări ANAF și BNR.";

    public static IReadOnlyList<string> Create(WorkerServiceSpec spec) =>
        ["create", spec.Name, .. Settings(spec), "DisplayName=", spec.DisplayName];

    public static IReadOnlyList<string> Config(WorkerServiceSpec spec) =>
        ["config", spec.Name, .. Settings(spec), "DisplayName=", spec.DisplayName];

    public static IReadOnlyList<string> SetDescription(WorkerServiceSpec spec) => ["description", spec.Name, Description];

    public static IReadOnlyList<string> Failure(WorkerServiceSpec spec) =>
        ["failure", spec.Name, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000"];

    public static IReadOnlyList<string> FailureFlag(WorkerServiceSpec spec) => ["failureflag", spec.Name, "1"];

    /// <summary>Actualizarea si revenirea: doar calea binarului (comutarea pe alta versiune).</summary>
    public static IReadOnlyList<string> SetBinaryPath(WorkerServiceSpec spec) => ["config", spec.Name, "binPath=", spec.BinaryPath];

    private static IEnumerable<string> Settings(WorkerServiceSpec spec)
    {
        yield return "binPath=";
        yield return spec.BinaryPath;
        yield return "start=";
        yield return "delayed-auto";
        yield return "obj=";
        yield return spec.Account;

        // Un gMSA nu are parola; sc.exe o cere totusi explicit, goala.
        if (spec.GmsaAccount is not null)
        {
            yield return "password=";
            yield return string.Empty;
        }
    }
}

/// <summary>Creeaza sau actualizeaza serviciul (la reluare si „Repară” se foloseste sc.exe config, 5.11).</summary>
public sealed class WorkerServiceInstaller(IProcessRunner processes, Func<string, bool> serviceExists)
{
    public void Install(WorkerServiceSpec spec)
    {
        Run(serviceExists(spec.Name) ? ServiceCommands.Config(spec) : ServiceCommands.Create(spec));
        Run(ServiceCommands.SetDescription(spec));
        Run(ServiceCommands.Failure(spec));
        Run(ServiceCommands.FailureFlag(spec));
    }

    public void SetBinaryPath(WorkerServiceSpec spec) => Run(ServiceCommands.SetBinaryPath(spec));

    private void Run(IReadOnlyList<string> arguments)
    {
        var result = processes.Run(ProcessRunner.System32("sc.exe"), arguments);
        if (result.ExitCode != 0)
        {
            throw new WindowsSetupException($"sc.exe {arguments[0]} {arguments[1]} a eșuat (cod {result.ExitCode}): {result.Output}");
        }
    }
}

/// <summary>Eroare IIS, serviciu, certificat, firewall sau fisiere (cod 50, 6.3).</summary>
public sealed class WindowsSetupException(string message, Exception? inner = null) : Exception(message, inner)
{
    public int ExitCode => Core.ExitCodes.IisServiceOrFileError;
}
