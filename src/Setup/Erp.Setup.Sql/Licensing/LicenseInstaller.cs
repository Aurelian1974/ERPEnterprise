using System.Data;
using System.Text.Json;
using Erp.Licensing;
using Erp.Setup.Core;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Licensing;

/// <summary>
/// Pasul I-12 (8.9): licenta se verifica din nou, se copiaza in DataRoot\&lt;Instanta&gt;\license\erp.lic (sursa de adevar
/// la pornire) si se salveaza cu core.usp_InstallationLicense_Save, pentru afisare si istoric. Rulat din nou: fara efect (R8).
/// Continutul licentei nu se jurnalizeaza (R6).
/// </summary>
public sealed class LicenseInstaller(ILicenseReader reader, SqlOperator sql)
{
    public const string FileName = "erp.lic";
    public const int SystemUserId = 1;
    public const int InvalidDatesError = 50017;

    public static string TargetPath(string dataRoot, string instance) => Path.Combine(dataRoot, instance, "license", FileName);

    /// <summary>Verifica si copiaza fisierul; arunca <see cref="SqlSetupException"/> cu codul 40 daca licenta nu e valida.</summary>
    public LicenseReadResult VerifyAndCopy(string sourcePath, string dataRoot, string instance)
    {
        var content = File.ReadAllBytes(sourcePath);
        var result = reader.Read(content);
        if (!result.IsValid)
        {
            throw new SqlSetupException(result.Message, ExitCodes.InvalidLicense);
        }

        var target = TargetPath(dataRoot, instance);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(content))
        {
            sql.Log.Information("Licența {LicenseId} este deja în {Target}", result.License!.LicenseId, target);
            return result;
        }

        var temporary = target + ".tmp";
        File.Copy(sourcePath, temporary, overwrite: true);
        File.Move(temporary, target, overwrite: true);
        sql.Log.Information("Licența {LicenseId} copiată în {Target}", result.License!.LicenseId, target);
        return result;
    }

    /// <summary>Salveaza licenta in core.InstallationLicense, intr-o tranzactie pe conexiunea data (loginul de migrare).</summary>
    public async Task<int> SaveAsync(SqlConnection connection, LicenseReadResult license, CancellationToken ct)
    {
        var payload = license.License ?? throw new ArgumentException("Licența nu este validă.", nameof(license));
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new SqlCommand("core.usp_InstallationLicense_Save", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = SqlOperator.CommandTimeoutSeconds,
        };
        command.Parameters.Add(new SqlParameter("@LicenseId", SqlDbType.NVarChar, 50) { Value = payload.LicenseId });
        command.Parameters.Add(new SqlParameter("@KeyId", SqlDbType.NVarChar, 50) { Value = license.KeyId.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        command.Parameters.Add(new SqlParameter("@CustomerName", SqlDbType.NVarChar, 200) { Value = payload.CustomerName });
        command.Parameters.Add(new SqlParameter("@FiscalCodes", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(payload.FiscalCodeList) });
        command.Parameters.Add(new SqlParameter("@Modules", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(payload.Modules) });
        command.Parameters.Add(new SqlParameter("@LimitCompanies", SqlDbType.Int) { Value = payload.Companies });
        command.Parameters.Add(new SqlParameter("@LimitUsers", SqlDbType.Int) { Value = payload.Limits.Users });
        command.Parameters.Add(new SqlParameter("@LimitPayrollEmployees", SqlDbType.Int) { Value = payload.Limits.PayrollEmployees });
        command.Parameters.Add(new SqlParameter("@LimitPosTerminals", SqlDbType.Int) { Value = payload.Limits.PosTerminals });
        command.Parameters.Add(new SqlParameter("@IssuedDate", SqlDbType.Date) { Value = payload.IssuedAt.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@ValidUntil", SqlDbType.Date) { Value = (payload.ValidUntil ?? DateOnly.MaxValue).ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@Signature", SqlDbType.VarChar, 200) { Value = payload.LicenseId });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = SystemUserId });

        try
        {
            var id = (int)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            sql.Log.Information("Licența {LicenseId} salvată în core.InstallationLicense (Id {Id})", payload.LicenseId, id);
            return id;
        }
        catch (SqlException ex)
        {
            sql.Log.Error("Salvarea licenței {LicenseId} a eșuat: eroarea SQL {Number}: {Message}", payload.LicenseId, ex.Number, ex.Message);
            throw new SqlSetupException($"Salvarea licenței a eșuat: eroarea SQL {ex.Number}: {ex.Message}",
                ex.Number == InvalidDatesError ? ExitCodes.InvalidLicense : ExitCodes.SqlInstallError, ex);
        }
    }
}
