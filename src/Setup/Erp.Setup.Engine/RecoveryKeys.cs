using Erp.Setup.Core;
using Erp.Setup.Core.State;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Engine;

/// <summary>Cheia de recuperare a instalarii (08, 5.12, 11.4): exportul obligatoriu la instalare si reexportul ulterior.</summary>
public static class RecoveryKeys
{
    /// <summary>Exporta cheia si noteaza data exportului in instance.json (ecranul 0 o afiseaza).</summary>
    public static InstanceState Export(IWindowsSetup windows, string statePath, string pfxPath, string password, TimeProvider time)
    {
        var state = InstanceStateStore.Read(statePath) ?? throw new InvalidOperationException("instance.json lipsește.");
        var thumbprint = state.DataProtectionCertificateThumbprint
            ?? throw new InvalidOperationException("Instalarea nu are certificat de protecție a datelor (I-08).");
        windows.ExportRecoveryKey(thumbprint, pfxPath, password);
        var updated = state with { RecoveryKeyExportedAt = time.GetUtcNow() };
        InstanceStateStore.Write(statePath, updated);
        return updated;
    }

    public static RecoveryKeyCheck Verify(IWindowsSetup windows, InstanceState state, string pfxPath, string password) =>
        windows.VerifyRecoveryKey(pfxPath, password, state.DataProtectionCertificateThumbprint);
}
