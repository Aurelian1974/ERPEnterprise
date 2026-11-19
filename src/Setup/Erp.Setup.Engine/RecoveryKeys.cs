using Erp.Setup.Core;
using Erp.Setup.Core.State;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Engine;

/// <summary>Cheia de recuperare a instalarii (08, 5.12, 11.4): exportul obligatoriu la instalare si reexportul ulterior.</summary>
public static class RecoveryKeys
{
    /// <summary>
    /// Exporta cheia si noteaza data exportului in instance.json (ecranul 0 o afiseaza); cu <paramref name="sql"/>, scrie si auditul
    /// Core.Installation.RecoveryKeyExported (11.4).
    /// </summary>
    public static InstanceState Export(IWindowsSetup windows, string statePath, string pfxPath, string password, TimeProvider time,
        Sql.SqlOperator? sql = null)
    {
        var state = InstanceStateStore.Read(statePath) ?? throw new InvalidOperationException("instance.json lipsește.");
        var thumbprint = state.DataProtectionCertificateThumbprint
            ?? throw new InvalidOperationException("Instalarea nu are certificat de protecție a datelor (I-08).");
        windows.ExportRecoveryKey(thumbprint, pfxPath, password);
        var updated = state with { RecoveryKeyExportedAt = time.GetUtcNow() };
        InstanceStateStore.Write(statePath, updated);
        if (sql is not null && state.DatabaseName is { } database)
        {
            Audit(sql, database, thumbprint).GetAwaiter().GetResult();
        }

        return updated;
    }

    private static async Task Audit(Sql.SqlOperator sql, string database, string thumbprint)
    {
        await using var connection = await sql.OpenAsync(database, CancellationToken.None).ConfigureAwait(false);
        await sql.ExecuteAsync(connection, "Auditul exportului cheii de recuperare", """
            INSERT INTO core.AuditEntry (OccurredAt, UserId, CompanyId, Action, EntityType, EntityId, Changes, IpAddress, CorrelationId)
            VALUES (SYSUTCDATETIME(), 1, NULL, N'Core.Installation.RecoveryKeyExported', N'Core.Installation', NULL,
                    (SELECT @Thumbprint AS thumbprint, @Machine AS machine FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), NULL, NEWID());
            """, CancellationToken.None,
            new Microsoft.Data.SqlClient.SqlParameter("@Thumbprint", thumbprint), new Microsoft.Data.SqlClient.SqlParameter("@Machine", Environment.MachineName))
            .ConfigureAwait(false);
    }

    public static RecoveryKeyCheck Verify(IWindowsSetup windows, InstanceState state, string pfxPath, string password) =>
        windows.VerifyRecoveryKey(pfxPath, password, state.DataProtectionCertificateThumbprint);
}
