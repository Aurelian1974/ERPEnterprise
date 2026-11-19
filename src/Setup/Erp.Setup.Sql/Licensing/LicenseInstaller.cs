using System.Data;
using System.Globalization;
using System.Text.Json;
using Erp.Licensing;
using Erp.Setup.Core;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Licensing;

/// <summary>Fisierul de licenta verificat: octetii (se salveaza ca atare in baza, 08, 4.1) si rezultatul verificarii.</summary>
public sealed record VerifiedLicense(byte[] File, LicenseReadResult Result)
{
    public License License => Result.License!;
}

/// <summary>
/// Pasul de licenta al installer-ului (08, 4.1, decizia D18): licenta se verifica (08, 4.2 si 4.5) si se salveaza in
/// core.InstallationLicense cu octetii fisierului; nu se mai copiaza pe disc. Rulat din nou: fara efect (R10).
/// Continutul licentei nu se jurnalizeaza (R8).
/// </summary>
public sealed class LicenseInstaller(ILicenseReader reader, SqlOperator sql)
{
    public const int SystemUserId = 1;
    public const int InvalidDatesError = 50017;

    /// <summary>
    /// Verifica fisierul; arunca <see cref="SqlSetupException"/> cu codul 40 daca licenta nu e valida si cu codul 41 daca nu
    /// include actualizari pentru kitul cu data de lansare data (C-17).
    /// </summary>
    public VerifiedLicense Verify(string sourcePath, DateOnly? kitReleaseDate = null, string? kitVersion = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new SqlSetupException($"Fișierul de licență nu există: {sourcePath}", ExitCodes.InvalidLicense);
        }

        var file = File.ReadAllBytes(sourcePath);
        var result = reader.Read(file);
        if (!result.IsValid)
        {
            throw new SqlSetupException(result.Message, ExitCodes.InvalidLicense);
        }

        if (kitReleaseDate is { } release && LicensePolicy.CheckKitRelease(result.License!, release, kitVersion ?? "acesta") is { } refused)
        {
            throw new SqlSetupException(refused, ExitCodes.LicenseExcludesKit);
        }

        return new VerifiedLicense(file, result);
    }

    /// <summary>Salveaza licenta in core.InstallationLicense, intr-o tranzactie pe conexiunea data (contul operatorului).</summary>
    public async Task<int> SaveAsync(SqlConnection connection, VerifiedLicense verified, CancellationToken ct)
    {
        var license = verified.License;
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new SqlCommand("core.usp_InstallationLicense_Save", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = SqlOperator.CommandTimeoutSeconds,
        };
        command.Parameters.Add(new SqlParameter("@LicenseFile", SqlDbType.VarBinary, -1) { Value = verified.File });
        command.Parameters.Add(new SqlParameter("@LicenseId", SqlDbType.NVarChar, 50) { Value = license.LicenseId });
        command.Parameters.Add(new SqlParameter("@KeyId", SqlDbType.NVarChar, 50) { Value = verified.Result.KeyId.ToString(CultureInfo.InvariantCulture) });
        command.Parameters.Add(new SqlParameter("@CustomerName", SqlDbType.NVarChar, 200) { Value = license.CustomerName });
        command.Parameters.Add(new SqlParameter("@FiscalCodes", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(license.FiscalCodeList) });
        command.Parameters.Add(new SqlParameter("@Modules", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(license.Modules) });
        command.Parameters.Add(new SqlParameter("@LimitCompanies", SqlDbType.Int) { Value = license.Companies });
        command.Parameters.Add(new SqlParameter("@LimitUsers", SqlDbType.Int) { Value = license.Limits.Users });
        command.Parameters.Add(new SqlParameter("@LimitPayrollEmployees", SqlDbType.Int) { Value = license.Limits.PayrollEmployees });
        command.Parameters.Add(new SqlParameter("@LimitPosTerminals", SqlDbType.Int) { Value = license.Limits.PosTerminals });
        command.Parameters.Add(new SqlParameter("@IssuedDate", SqlDbType.Date) { Value = license.IssuedAt.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@ValidUntil", SqlDbType.Date) { Value = (object?)license.ValidUntil?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UpdatesUntil", SqlDbType.Date) { Value = (object?)license.UpdatesUntil?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = SystemUserId });

        try
        {
            var id = (int)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            sql.Log.Information("Licența {LicenseId} salvată în core.InstallationLicense (Id {Id})", license.LicenseId, id);
            return id;
        }
        catch (SqlException ex)
        {
            sql.Log.Error("Salvarea licenței {LicenseId} a eșuat: eroarea SQL {Number}: {Message}", license.LicenseId, ex.Number, ex.Message);
            throw new SqlSetupException($"Salvarea licenței a eșuat: eroarea SQL {ex.Number}: {ex.Message}",
                ex.Number == InvalidDatesError ? ExitCodes.InvalidLicense : ExitCodes.SqlInstallError, ex);
        }
    }
}
