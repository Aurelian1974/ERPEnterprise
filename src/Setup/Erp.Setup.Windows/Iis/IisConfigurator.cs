using Microsoft.Web.Administration;

namespace Erp.Setup.Windows.Iis;

/// <summary>Site-ul si pool-ul unei instante (8.11).</summary>
public sealed record IisSiteSpec(
    string Instance,
    string HostPhysicalPath,
    string HostName,
    int HttpsPort,
    bool HttpRedirect,
    string CertificateThumbprint,
    string? GmsaAccount = null)
{
    public string SiteName => $"ERP-{Instance}";

    public string PoolName => $"ERP-{Instance}";

    public string HttpsBinding => $"*:{HttpsPort}:{HostName}";

    public string HttpBinding => $"*:80:{HostName}";
}

/// <summary>
/// Pasul I-06 (5.11) cu ServerManager: toate modificarile intr-un singur CommitChanges(); obiectele existente se actualizeaza,
/// nu se recreeaza (reluare, „Repară”), iar legarile adaugate de client pe site se pastreaza. Site-ul si pool-ul nou create
/// raman oprite pana la I-17.
/// </summary>
public static class IisConfigurator
{
    public const string WindowsAuthenticationSection = "system.webServer/security/authentication/windowsAuthentication";

    public static void Apply(IisSiteSpec spec)
    {
        using (var manager = new ServerManager())
        {
            ConfigurePool(manager.ApplicationPools[spec.PoolName] ?? manager.ApplicationPools.Add(spec.PoolName), spec);

            var site = manager.Sites[spec.SiteName] ?? manager.Sites.Add(spec.SiteName, spec.HostPhysicalPath, spec.HttpsPort);
            var root = site.Applications["/"];
            root.ApplicationPoolName = spec.PoolName;
            root.VirtualDirectories["/"].PhysicalPath = spec.HostPhysicalPath;
            site.ApplicationDefaults.ApplicationPoolName = spec.PoolName;
            site.ApplicationDefaults["preloadEnabled"] = true;
            root["preloadEnabled"] = true;
            site.ServerAutoStart = true;

            EnsureBindings(site, spec);

            var config = manager.GetApplicationHostConfiguration();
            config.GetSection(WindowsAuthenticationSection, spec.SiteName).OverrideMode = OverrideMode.Allow;

            manager.CommitChanges();
        }
    }

    /// <summary>„Dezinstalează” (11.6): sterge site-ul si pool-ul instalarii; celelalte site-uri raman neatinse.</summary>
    public static bool Remove(string siteName, string poolName)
    {
        using var manager = new ServerManager();
        var removed = false;
        if (manager.Sites[siteName] is { } site)
        {
            manager.Sites.Remove(site);
            removed = true;
        }

        if (manager.ApplicationPools[poolName] is { } pool)
        {
            manager.ApplicationPools.Remove(pool);
            removed = true;
        }

        manager.CommitChanges();
        return removed;
    }

    /// <summary>Opreste site-ul si pool-ul (I-06 la o instalare noua; actualizarea, 10.2).</summary>
    public static void Stop(IisSiteSpec spec)
    {
        using var manager = new ServerManager();
        var site = manager.Sites[spec.SiteName];
        if (site is not null && site.State is ObjectState.Started or ObjectState.Starting)
        {
            site.Stop();
        }

        var pool = manager.ApplicationPools[spec.PoolName];
        if (pool is not null && pool.State is ObjectState.Started or ObjectState.Starting)
        {
            pool.Stop();
        }
    }

    /// <summary>Actualizarea si revenirea (10.2): comutarea caii fizice a site-ului pe folderul altei versiuni.</summary>
    public static void SetPhysicalPath(string siteName, string physicalPath)
    {
        using var manager = new ServerManager();
        var site = manager.Sites[siteName] ?? throw new Services.WindowsSetupException($"Site-ul IIS {siteName} nu există.");
        site.Applications["/"].VirtualDirectories["/"].PhysicalPath = physicalPath;
        manager.CommitChanges();
    }

    private static void ConfigurePool(ApplicationPool pool, IisSiteSpec spec)
    {
        pool.ManagedRuntimeVersion = string.Empty;
        pool.ManagedPipelineMode = ManagedPipelineMode.Integrated;
        pool.Enable32BitAppOnWin64 = false;
        pool.StartMode = StartMode.AlwaysRunning;
        pool.AutoStart = true;
        if (spec.GmsaAccount is null)
        {
            pool.ProcessModel.IdentityType = ProcessModelIdentityType.ApplicationPoolIdentity;
        }
        else
        {
            // gMSA: fara parola (Windows o administreaza).
            pool.ProcessModel.IdentityType = ProcessModelIdentityType.SpecificUser;
            pool.ProcessModel.UserName = spec.GmsaAccount;
            pool.ProcessModel.Password = string.Empty;
        }

        pool.ProcessModel.IdleTimeout = TimeSpan.Zero;
        pool.ProcessModel.LoadUserProfile = true;
        pool.Recycling.PeriodicRestart.Time = TimeSpan.Zero;
    }

    private static void EnsureBindings(Site site, IisSiteSpec spec)
    {
        // Site.Add creeaza o legare http implicita fara nume de gazda; o inlocuim cu legarile noastre.
        foreach (var binding in site.Bindings.Where(b => b.Protocol == "http" && string.IsNullOrEmpty(b.Host) && b.EndPoint?.Port == spec.HttpsPort).ToList())
        {
            site.Bindings.Remove(binding);
        }

        var hash = Convert.FromHexString(spec.CertificateThumbprint);
        var https = site.Bindings.FirstOrDefault(b => b.Protocol == "https" && b.BindingInformation == spec.HttpsBinding);
        if (https is null)
        {
            site.Bindings.Add(spec.HttpsBinding, hash, "My", SslFlags.Sni);
        }
        else
        {
            https.CertificateHash = hash;
            https.CertificateStoreName = "My";
            https.SslFlags = SslFlags.Sni;
        }

        var http = site.Bindings.FirstOrDefault(b => b.Protocol == "http" && b.BindingInformation == spec.HttpBinding);
        if (spec.HttpRedirect && http is null)
        {
            site.Bindings.Add(spec.HttpBinding, "http");
        }
        else if (!spec.HttpRedirect && http is not null)
        {
            site.Bindings.Remove(http);
        }
    }

    /// <summary>Porneste pool-ul si site-ul daca sunt oprite si verifica starea Started (I-17).</summary>
    public static void EnsureStarted(IisSiteSpec spec)
    {
        using var manager = new ServerManager();
        var pool = manager.ApplicationPools[spec.PoolName];
        if (pool.State != ObjectState.Started)
        {
            pool.Start();
        }

        var site = manager.Sites[spec.SiteName];
        if (site.State != ObjectState.Started)
        {
            site.Start();
        }

        if (pool.State != ObjectState.Started || site.State != ObjectState.Started)
        {
            throw new Services.WindowsSetupException($"Site-ul {spec.SiteName} sau pool-ul {spec.PoolName} nu a pornit (site: {site.State}, pool: {pool.State}).");
        }
    }
}
