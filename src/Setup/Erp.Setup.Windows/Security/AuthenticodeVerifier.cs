using System.Runtime.InteropServices;

namespace Erp.Setup.Windows.Security;

/// <summary>Verificarea Authenticode a installer-ului (4.3) prin WinVerifyTrust.</summary>
public static class AuthenticodeVerifier
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrusted(string filePath)
    {
        var fileInfo = new WintrustFileInfo
        {
            CbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            PcwszFilePath = filePath,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WintrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                DwUiChoice = WtdUiNone,
                FdwRevocationChecks = WtdRevokeNone,
                DwUnionChoice = WtdChoiceFile,
                PFile = fileInfoPtr,
                DwStateAction = WtdStateActionVerify,
            };

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            data.DwStateAction = WtdStateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WintrustFileInfo>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionId, ref WintrustData pWvtData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint CbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint CbStruct;
        public IntPtr PPolicyCallbackData;
        public IntPtr PSipClientData;
        public uint DwUiChoice;
        public uint FdwRevocationChecks;
        public uint DwUnionChoice;
        public IntPtr PFile;
        public uint DwStateAction;
        public IntPtr HWvtStateData;
        public IntPtr PwszUrlReference;
        public uint DwProvFlags;
        public uint DwUiContext;
        public IntPtr PSignatureSettings;
    }
}
