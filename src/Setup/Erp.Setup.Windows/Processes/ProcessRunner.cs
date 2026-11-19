using System.Diagnostics;
using Serilog;

namespace Erp.Setup.Windows.Processes;

public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>Lansarea programelor externe (sc.exe, netsh) cu ArgumentList, fara shell si fara text concatenat (12).</summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}

public sealed class ProcessRunner(ILogger log) : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} nu a pornit.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(fileName)} nu s-a terminat în {Timeout.TotalSeconds:F0} s.");
        }

        var text = (output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult()).Trim();
        log.Information("{Program} {Command}: cod {ExitCode} ({ElapsedMs} ms)", Path.GetFileName(fileName), arguments.Count > 0 ? arguments[0] : string.Empty,
            process.ExitCode, watch.ElapsedMilliseconds);
        return new ProcessResult(process.ExitCode, text);
    }

    public static string System32(string program) => Path.Combine(Environment.SystemDirectory, program);
}
