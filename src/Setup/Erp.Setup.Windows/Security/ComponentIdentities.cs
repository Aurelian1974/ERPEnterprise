using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Erp.Setup.Windows.Security;

/// <summary>
/// Identitatile componentelor (3.3). SID-urile conturilor virtuale se calculeaza determinist din nume (SHA-1 peste nume,
/// UTF-16LE: NT SERVICE = S-1-5-80-… cu majuscule, IIS AppPool = S-1-5-82-… cu litere mici), deci drepturile NTFS (I-17) se pot da
/// inainte ca pool-ul (I-18) si serviciul (I-19) sa existe.
/// </summary>
public sealed record ComponentIdentities(SecurityIdentifier HostPool, SecurityIdentifier Worker)
{
    public static string PoolName(string instance) => $"ERP-{instance}-Host";

    public static string ServiceName(string instance) => $"ErpWorker-{instance}";

    /// <param name="appWindowsAccount">Contul de domeniu sau gMSA (appAuthentication = Windows); altfel conturile virtuale.</param>
    public static ComponentIdentities For(string instance, string? appWindowsAccount = null)
    {
        if (appWindowsAccount is not null)
        {
            var account = (SecurityIdentifier)new NTAccount(appWindowsAccount).Translate(typeof(SecurityIdentifier));
            return new ComponentIdentities(account, account);
        }

        return new ComponentIdentities(VirtualAccountSid(82, PoolName(instance)), VirtualAccountSid(80, ServiceName(instance)));
    }

    public static SecurityIdentifier VirtualAccountSid(int authority, string name)
    {
        // NT SERVICE (80) foloseste numele cu majuscule, IIS AppPool (82) numele cu litere mici.
        var normalized = authority == 82 ? name.ToLowerInvariant() : name.ToUpperInvariant();
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(normalized));
        var parts = Enumerable.Range(0, 5).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(i * 4, 4)).ToString(CultureInfo.InvariantCulture));
        return new SecurityIdentifier($"S-1-5-{authority}-{string.Join('-', parts)}");
    }
}
