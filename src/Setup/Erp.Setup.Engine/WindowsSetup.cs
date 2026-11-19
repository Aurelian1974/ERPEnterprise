using Erp.Setup.Core;
using System.Runtime.Versioning;
using Erp.Configuration;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Health;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Logging;
using Erp.Setup.Windows.Certificates;
using Erp.Setup.Windows.Iis;
using Erp.Setup.Windows.Layout;
using Erp.Setup.Windows.Prerequisites;
using Erp.Setup.Windows.Probes;
using Erp.Setup.Windows.Processes;
using Erp.Setup.Windows.Security;
using Erp.Setup.Windows.Services;
using Serilog;

namespace Erp.Setup.Engine;

/// <summary>Implementarea reala a <see cref="IWindowsSetup"/>, peste Erp.Setup.Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSetup(ILogger log, TimeProvider time) : IWindowsSetup
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);
    private readonly WindowsProbe probe = new();
    private readonly ProcessRunner processes = new(log);

    public void CreateDataFolders(InstallRequest request) => Layout(request).CreateDataFolders();

    public IReadOnlyList<string> MissingIisFeatures()
    {
        var enabled = probe.GetEnabledWindowsFeatures() ?? throw new WindowsSetupException("Funcțiile Windows nu au putut fi interogate (dism).");
        return [.. Core.Checks.RequirementsChecker.IisFeatures.Where(f => !enabled.Contains(f))];
    }

    public bool EnableIisFeatures(IReadOnlyList<string> missing) =>
        new PrerequisitesInstaller(processes).EnableIisFeatures(missing) == PrerequisiteResult.RestartRequired;

    public bool IsHostingBundleInstalled(string minimumVersion)
    {
        var required = Version.Parse(minimumVersion);
        return probe.GetAspNetCoreRuntimeVersions().Any(v => v.Major == required.Major && v >= required) && probe.IsAspNetCoreModuleInstalled();
    }

    public bool InstallHostingBundle(string kitRoot) =>
        new PrerequisitesInstaller(processes).InstallHostingBundle(kitRoot) == PrerequisiteResult.RestartRequired;

    public string CreateInternalHttpsCertificate(InstallRequest request) =>
        InternalHttpsCertificate.CreateInMachineStore(request.Instance, request.Site.HostName, probe.MachineName,
            Paths(request).InternalCertificate, time.GetUtcNow());

    public CertificateInfo? FindMachineCertificate(string thumbprint) => probe.FindMachineCertificate(thumbprint);

    public int DeployBinaries(string kitRoot, KitManifest manifest, InstallRequest request) =>
        BinaryDeployer.Deploy(kitRoot, manifest, Layout(request), replaceExisting: false);

    public void CreateSiteAndService(InstallRequest request, string version, string certificateThumbprint)
    {
        var gmsa = Gmsa(request);
        var site = SiteSpec(request, version, certificateThumbprint);
        var existed = probe.IisSiteExists(site.SiteName);
        IisConfigurator.Apply(site);
        if (!existed)
        {
            // Un site nou ar porni imediat (serverAutoStart, AlwaysRunning), inainte ca baza sa existe; ramane oprit pana la I-17.
            IisConfigurator.Stop(site);
        }

        new WorkerServiceInstaller(processes, probe.ServiceExists).Install(ServiceSpec(request, version));
        if (gmsa is not null)
        {
            ServiceLogonRight.Grant(ComponentIdentities.For(request.Instance, gmsa).Worker);
        }
    }

    public void WriteConfiguration(string kitRoot, InstallRequest request, string version, InstallationSettingsValues values)
    {
        var layout = Layout(request);
        ConfigurationWriter.Write(kitRoot, layout, version, values);
        var identities = ComponentIdentities.For(request.Instance, Gmsa(request));
        NtfsPermissions.ApplyData(layout, identities);
        NtfsPermissions.ApplyBinaries(layout, version, identities);
    }

    public string EnsureDataProtectionCertificate(InstallRequest request, string? existingThumbprint)
    {
        string thumbprint;
        if (existingThumbprint is not null && DataProtectionCertificate.Find(existingThumbprint) is { } existing)
        {
            existing.Dispose();
            thumbprint = existingThumbprint;
        }
        else
        {
            thumbprint = DataProtectionCertificate.CreateInMachineStore(request.Instance, time.GetUtcNow());
        }

        var identities = ComponentIdentities.For(request.Instance, Gmsa(request));
        DataProtectionCertificate.GrantPrivateKeyRead(thumbprint, [identities.HostPool, identities.Worker]);
        return thumbprint;
    }

    public string ComputerAccount()
    {
        var domain = probe.GetComputerDomain() ?? throw new WindowsSetupException("Calculatorul nu face parte dintr-un domeniu Active Directory.");
        return $@"{domain}\{probe.MachineName}$";
    }

    public void ApplyFirewall(InstallRequest request) =>
        new FirewallRules(processes).Apply(request.Instance, request.Site.HttpsPort, request.Site.HttpRedirect);

    public void StartApplication(InstallRequest request, string version)
    {
        ServiceControl.Start(request.ServiceName, ServiceTimeout);
        IisConfigurator.EnsureStarted(SiteSpec(request, version, certificateThumbprint: string.Empty));
    }

    public async Task<HealthCheckOutcome> CheckHealthAsync(InstallRequest request, string version, string? lastMigration, string? pinnedThumbprint,
        CancellationToken ct)
    {
        using var health = new InstallerHealthCheck(request.Site.HostName, request.Site.HttpsPort, pinnedThumbprint, time);
        return await health.WaitUntilHealthyAsync(version, lastMigration, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<string> ComponentLogTail(InstallRequest request)
    {
        var paths = Paths(request);
        var lines = new List<string>();
        foreach (var (component, folder) in new[] { ("Host", paths.HostLogs), ("Worker", paths.WorkerLogs) })
        {
            lines.Add($"Ultimele rânduri din jurnalul {component}:");
            lines.AddRange(LogTail.Read(folder).Select(line => "  " + line));
        }

        return lines;
    }

    public string WriteInitialAdminFile(InstallRequest request, string content)
    {
        var path = Paths(request).InitialAdmin;
        InitialAdminFile.Write(path, content);
        InitialAdminFile.GrantDelete(path, ComponentIdentities.For(request.Instance, Gmsa(request)).HostPool);
        return path;
    }

    public void ExportRecoveryKey(string thumbprint, string pfxPath, string password) => DataProtectionCertificate.Export(thumbprint, pfxPath, password);

    public RecoveryKeyCheck VerifyRecoveryKey(string pfxPath, string password, string? expectedThumbprint) =>
        DataProtectionCertificate.Check(pfxPath, password, expectedThumbprint);

    private static string? Gmsa(InstallRequest request) => request.Sql.AppIdentity == AppIdentity.Gmsa ? request.Sql.GmsaAccount : null;

    private static InstanceLayout Layout(InstallRequest request) => new(request.InstallRoot, request.DataRoot, request.Instance);

    private static InstanceDataPaths Paths(InstallRequest request) => InstanceDataPaths.For(request.DataRoot, request.Instance);

    private static IisSiteSpec SiteSpec(InstallRequest request, string version, string certificateThumbprint) =>
        new(request.Instance, Layout(request).HostDirectory(version), request.Site.HostName, request.Site.HttpsPort, request.Site.HttpRedirect,
            certificateThumbprint, Gmsa(request));

    private static WorkerServiceSpec ServiceSpec(InstallRequest request, string version) =>
        new(request.Instance, Path.Combine(Layout(request).WorkerDirectory(version), "Erp.Worker.exe"), Paths(request).InstanceDirectory, Gmsa(request));
}
