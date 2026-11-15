using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Erp.Setup.Core.Checks;
using Microsoft.Web.Administration;
using Microsoft.Win32;

namespace Erp.Setup.Windows.Probes;

/// <summary>Citirile de pe serverul de aplicatie pentru C-01…C-23; nu modifica nimic.</summary>
public sealed partial class WindowsProbe : IWindowsProbe
{
    private const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(90);
    private static readonly string ApplicationHostConfig = Path.Combine(Environment.SystemDirectory, "inetsrv", "config", "applicationHost.config");

    public int LogicalProcessorCount => Environment.ProcessorCount;

    public long TotalPhysicalMemoryBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public OsInfo GetOperatingSystem()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
        var productType = key?.GetValue("ProductType") as string ?? "necunoscut";
        // Environment.OSVersion foloseste RtlGetVersion in .NET 5+, deci build-ul real.
        return new OsInfo(Environment.OSVersion.Version.Build, productType, Environment.Is64BitOperatingSystem);
    }

    public long? GetFreeSpaceBytes(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var drive = new DriveInfo(root);
        return drive.IsReady ? drive.AvailableFreeSpace : null;
    }

    public IReadOnlySet<string>? GetInstalledWindowsFeatures()
    {
        var output = RunProcess(
            Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command",
                "Get-WindowsFeature -Name 'Web-*' | Where-Object Installed | ForEach-Object Name"]);
        return output is { ExitCode: 0 }
            ? output.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : null;
    }

    public IReadOnlyList<Version> GetAspNetCoreRuntimeVersions()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateDirectories(folder)
            .Select(Path.GetFileName)
            .Select(name => Version.TryParse(name, out var version) ? version : null)
            .OfType<Version>()
            .ToList();
    }

    public bool IsAspNetCoreModuleInstalled() =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", "aspnetcorev2.dll"));

    public CertificateInfo? FindMachineCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        try
        {
            if (found.Count == 0)
            {
                return null;
            }

            var certificate = found[0];
            var names = new List<string>();
            foreach (var extension in certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>())
            {
                names.AddRange(extension.EnumerateDnsNames());
            }

            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!string.IsNullOrEmpty(commonName))
            {
                names.Add(commonName);
            }

            var serverAuth = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == ServerAuthenticationEku));

            return new CertificateInfo(certificate.HasPrivateKey, new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero), names, serverAuth);
        }
        finally
        {
            foreach (var certificate in found)
            {
                certificate.Dispose();
            }
        }
    }

    public IReadOnlyList<IisBinding> GetIisBindings()
    {
        if (!File.Exists(ApplicationHostConfig))
        {
            return [];
        }

        using var manager = new ServerManager(readOnly: true, ApplicationHostConfig);
        var bindings = new List<IisBinding>();
        foreach (var site in manager.Sites)
        {
            foreach (var binding in site.Bindings)
            {
                if (binding.Protocol is "http" or "https" && binding.EndPoint is not null)
                {
                    bindings.Add(new IisBinding(site.Name, binding.Protocol, binding.EndPoint.Port, binding.Host ?? string.Empty));
                }
            }
        }

        return bindings;
    }

    public string? GetSniBindingThumbprint(string hostName, int port)
    {
        var output = RunProcess(Path.Combine(Environment.SystemDirectory, "netsh.exe"),
            ["http", "show", "sslcert", $"hostnameport={hostName}:{port}"]);
        if (output is not { ExitCode: 0 })
        {
            return null;
        }

        // Etichetele netsh sunt traduse dupa limba sistemului; amprenta e singurul sir de 40 de caractere hexazecimale.
        var match = Thumbprint().Match(output.Text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    public bool IsTls12ClientDisabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client");
        if (key is null)
        {
            return false;
        }

        return key.GetValue("Enabled") is int enabled && enabled == 0
            || key.GetValue("DisabledByDefault") is int disabledByDefault && disabledByDefault == 1;
    }

    public bool IisSiteExists(string siteName)
    {
        if (!File.Exists(ApplicationHostConfig))
        {
            return false;
        }

        using var manager = new ServerManager(readOnly: true, ApplicationHostConfig);
        return manager.Sites.Any(s => string.Equals(s.Name, siteName, StringComparison.OrdinalIgnoreCase));
    }

    public bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key is not null;
    }

    private static ProcessOutput? RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(fileName))
        {
            return null;
        }

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

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        return new ProcessOutput(process.ExitCode, stdout.GetAwaiter().GetResult());
    }

    private sealed record ProcessOutput(int ExitCode, string Text);

    [GeneratedRegex(@"\b[0-9A-Fa-f]{40}\b")]
    private static partial Regex Thumbprint();
}
