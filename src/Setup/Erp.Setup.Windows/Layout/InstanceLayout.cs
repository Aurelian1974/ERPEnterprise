using System.Security.AccessControl;
using System.Security.Principal;
using Erp.Configuration;
using Erp.Setup.Windows.Security;

namespace Erp.Setup.Windows.Layout;

/// <summary>Folderele instantei (3.2): binarele pe versiune in InstallRoot, datele in DataRoot.</summary>
public sealed record InstanceLayout(string InstallRoot, string DataRoot, string Instance)
{
    public string InstallDirectory => Path.Combine(InstallRoot, Instance);

    public string VersionsDirectory => Path.Combine(InstallDirectory, "versions");

    public InstanceDataPaths Data => InstanceDataPaths.For(DataRoot, Instance);

    public string HostDirectory(string version) => Path.Combine(VersionsDirectory, version, "host");

    public string WorkerDirectory(string version) => Path.Combine(VersionsDirectory, version, "worker");

    /// <summary>Pasul I-05: folderele din DataRoot (idempotent).</summary>
    public void CreateDataFolders()
    {
        var data = Data;
        foreach (var folder in new[]
        {
            data.InstanceDirectory, data.ConfigDirectory, data.LicenseDirectory, data.KeysDirectory, data.HostLogs, data.WorkerLogs,
            data.SetupLogs, data.MaintenanceDirectory, data.UpgradeDirectory, data.InstallDirectory,
        })
        {
            Directory.CreateDirectory(folder);
        }
    }
}

/// <summary>
/// Pasul I-17 (3.2): drepturile NTFS. DataRoot\&lt;Instanta&gt; e protejat (fara mostenire) si apartine Administrators si SYSTEM;
/// pool-ul Host si serviciul primesc doar ce le trebuie. Folderele de versiune mostenesc drepturile din Program Files
/// (Administrators, SYSTEM) si primesc citire pentru componenta lor. Reaplicarea nu dubleaza regulile (R8).
/// </summary>
public static class NtfsPermissions
{
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private const FileSystemRights Read = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
    private const FileSystemRights Write = FileSystemRights.Modify | FileSystemRights.Synchronize;

    public static readonly IReadOnlyList<SecurityIdentifier> DefaultOwners =
    [
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new(WellKnownSidType.LocalSystemSid, null),
    ];

    /// <param name="owners">Administrators si SYSTEM; testele adauga utilizatorul curent ca sa poata sterge folderul.</param>
    public static void ApplyData(InstanceLayout layout, ComponentIdentities identities, IReadOnlyList<SecurityIdentifier>? owners = null)
    {
        var data = layout.Data;
        var full = owners ?? DefaultOwners;

        var root = new DirectoryInfo(data.InstanceDirectory);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var owner in full)
        {
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        }

        root.SetAccessControl(security);

        foreach (var folder in new[] { data.ConfigDirectory, data.LicenseDirectory, data.MaintenanceDirectory })
        {
            Grant(new DirectoryInfo(folder), identities.HostPool, Read);
            Grant(new DirectoryInfo(folder), identities.Worker, Read);
        }

        Grant(new DirectoryInfo(data.KeysDirectory), identities.HostPool, Write);
        Grant(new DirectoryInfo(data.HostLogs), identities.HostPool, Write);
        Grant(new DirectoryInfo(data.WorkerLogs), identities.Worker, Write);

        if (File.Exists(data.InstanceJson))
        {
            GrantFile(data.InstanceJson, identities.HostPool, Read);
            GrantFile(data.InstanceJson, identities.Worker, Read);
        }
    }

    public static void ApplyBinaries(InstanceLayout layout, string version, ComponentIdentities identities)
    {
        Grant(new DirectoryInfo(layout.HostDirectory(version)), identities.HostPool, Read);
        Grant(new DirectoryInfo(layout.WorkerDirectory(version)), identities.Worker, Read);
    }

    private static void Grant(DirectoryInfo folder, SecurityIdentifier identity, FileSystemRights rights)
    {
        var security = folder.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(identity, rights, Inherit, PropagationFlags.None, AccessControlType.Allow));
        folder.SetAccessControl(security);
    }

    private static void GrantFile(string path, SecurityIdentifier identity, FileSystemRights rights)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(identity, rights, AccessControlType.Allow));
        file.SetAccessControl(security);
    }
}
