using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Erp.Setup.Windows.Security;

/// <summary>
/// initial-admin.txt (8.8): parola generata in modul --unattended, in DataRoot\&lt;Instanta&gt;\install\, cu drepturi NTFS doar
/// pentru Administrators si SYSTEM (fara mostenire), plus dreptul Delete pentru pool-ul Host (adaugat dupa crearea pool-ului).
/// Fisierul se creeaza direct cu ACL-ul restrans, ca sa nu existe niciun moment cu drepturile mostenite.
/// </summary>
public static class InitialAdminFile
{
    public const string FileName = "initial-admin.txt";

    public static string PathFor(string dataRoot, string instance) => Path.Combine(dataRoot, instance, "install", FileName);

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));

        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.WriteData | FileSystemRights.Synchronize,
            FileShare.None, 4096, FileOptions.None, security);
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes);
    }

    /// <summary>Dreptul Delete pentru identitatea pool-ului Host, ca Erp.Host sa stearga fisierul dupa prima schimbare a parolei.</summary>
    public static void GrantDelete(string path, IdentityReference identity)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Delete, AccessControlType.Allow));
        file.SetAccessControl(security);
    }
}
