using System.Data;
using Erp.Security.Passwords;
using Erp.Setup.Core.Secrets;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Administrator;

/// <summary>
/// „Resetează accesul administratorului” (08, 11.3): dovada autoritatii e contul Windows al operatorului (administrator local si
/// sysadmin). Parola temporara noua exista doar in memorie si se afiseaza o singura data; nu are varianta neasistata.
/// </summary>
public sealed class AdministratorReset(SqlOperator sql)
{
    public async Task<string> ResetAsync(string database, string userName, string setupVersion, string machineName, CancellationToken ct)
    {
        var password = InitialPasswordGenerator.Generate();
        await using var connection = await sql.OpenAsync(database, ct).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            Guid subjectId;
            await using (var account = Procedure(connection, transaction, "ids.usp_UserAccount_EmergencyReset"))
            {
                account.Parameters.Add(new SqlParameter("@UserName", SqlDbType.NVarChar, 50) { Value = userName });
                account.Parameters.Add(new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 500) { Value = IdentityPasswordHasher.Hash(password) });
                account.Parameters.Add(new SqlParameter("@SecurityStamp", SqlDbType.NVarChar, 64) { Value = AccountStamps.NewSecurityStamp() });
                account.Parameters.Add(new SqlParameter("@ConcurrencyStamp", SqlDbType.NVarChar, 64) { Value = AccountStamps.NewConcurrencyStamp() });
                subjectId = (Guid)(await account.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            await using (var user = Procedure(connection, transaction, "core.usp_User_EmergencyReset"))
            {
                user.Parameters.Add(new SqlParameter("@SubjectId", SqlDbType.UniqueIdentifier) { Value = subjectId });
                user.Parameters.Add(new SqlParameter("@SetupVersion", SqlDbType.NVarChar, 20) { Value = setupVersion });
                user.Parameters.Add(new SqlParameter("@MachineName", SqlDbType.NVarChar, 128) { Value = machineName });
                await user.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            sql.Log.Error("Resetarea administratorului {UserName} a eșuat: eroarea SQL {Number}: {Message}", userName, ex.Number, ex.Message);
            throw new SqlSetupException($"Resetarea administratorului a eșuat: eroarea SQL {ex.Number}: {ex.Message}", inner: ex);
        }

        sql.Log.Information("Accesul administratorului {UserName} a fost resetat (parolă temporară nouă, 2FA cerut din nou, sesiuni închise)", userName);
        return password;
    }

    private static SqlCommand Procedure(SqlConnection connection, SqlTransaction transaction, string name) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = SqlOperator.CommandTimeoutSeconds };
}
