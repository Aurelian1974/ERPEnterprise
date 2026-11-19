using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Erp.Setup.Windows.Services;

/// <summary>
/// Dreptul „Log on as a service” (SeServiceLogonRight) pentru contul gMSA (I-06, 5.8, 5.11), prin LsaAddAccountRights.
/// Conturile virtuale NT SERVICE\… il primesc automat de la Service Control Manager.
/// </summary>
public static partial class ServiceLogonRight
{
    private const string Right = "SeServiceLogonRight";
    private const int PolicyCreateAccount = 0x00000010;
    private const int PolicyLookupNames = 0x00000800;

    public static void Grant(SecurityIdentifier account)
    {
        var attributes = default(LsaObjectAttributes);
        var status = LsaOpenPolicy(IntPtr.Zero, ref attributes, PolicyCreateAccount | PolicyLookupNames, out var policy);
        ThrowOnError(status, "LsaOpenPolicy");
        try
        {
            var sid = new byte[account.BinaryLength];
            account.GetBinaryForm(sid, 0);
            var right = Marshal.StringToHGlobalUni(Right);
            try
            {
                var unicode = new LsaUnicodeString
                {
                    Length = (ushort)(Right.Length * 2),
                    MaximumLength = (ushort)((Right.Length + 1) * 2),
                    Buffer = right,
                };
                status = LsaAddAccountRights(policy, sid, [unicode], 1);
                ThrowOnError(status, "LsaAddAccountRights");
            }
            finally
            {
                Marshal.FreeHGlobal(right);
            }
        }
        finally
        {
            _ = LsaClose(policy);
        }
    }

    private static void ThrowOnError(uint status, string function)
    {
        if (status != 0)
        {
            var error = LsaNtStatusToWinError(status);
            throw new WindowsSetupException($"{function} a eșuat (eroarea Windows {error}): dreptul „Log on as a service” nu a putut fi acordat.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public int Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes attributes, int access, out IntPtr policy);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaAddAccountRights(IntPtr policy, [In] byte[] accountSid, [In] LsaUnicodeString[] rights, uint count);

    [LibraryImport("advapi32.dll")]
    private static partial uint LsaClose(IntPtr policy);

    [LibraryImport("advapi32.dll")]
    private static partial int LsaNtStatusToWinError(uint status);
}
