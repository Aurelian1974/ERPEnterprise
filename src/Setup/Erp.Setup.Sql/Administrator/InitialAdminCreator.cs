using System.Data;
using System.Text.RegularExpressions;
using Erp.Security.Passwords;
using Erp.Setup.Core;
using Erp.Setup.Core.Secrets;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Administrator;

public sealed record InitialAdminRequest(
    string UserName,
    string DisplayName,
    string? Email,
    string SetupVersion,
    string MachineName);

/// <summary>Rezultatul pasului I-13. Parola temporara exista doar in memorie, pentru afisarea unica (I-24).</summary>
public sealed record InitialAdminResult(bool Created, string UserName, string? TemporaryPassword, bool PasswordGenerated)
{
    public override string ToString() => $"InitialAdminResult {{ Created = {Created}, UserName = {UserName} }}";
}

/// <summary>
/// Pasul I-13 (8.8): exact un administrator initial (R5). Daca exista deja un administrator activ, pasul e sarit si nu
/// se afiseaza nicio parola (R8). Parola in clar nu ajunge in SQL si nici in jurnal (R6).
/// </summary>
public sealed partial class InitialAdminCreator(SqlOperator sql)
{
    public const string SkippedMessage = "Administratorul există deja; nu se creează altul.";

    public async Task<InitialAdminResult> CreateAsync(SqlConnection migrator, InitialAdminRequest request, CancellationToken ct)
    {
        if (!UserName().IsMatch(request.UserName))
        {
            throw new SqlSetupException($"Numele administratorului „{request.UserName}” este invalid (doar litere mici, cifre, . _ -).", ExitCodes.InvalidAnswersFile);
        }

        if (await ActiveAdministratorExistsAsync(migrator, ct).ConfigureAwait(false))
        {
            sql.Log.Information("I-13: {Message}", SkippedMessage);
            return new InitialAdminResult(false, request.UserName, null, false);
        }

        // 08, 5.10: parola temporara se genereaza intotdeauna; operatorul nu o alege.
        const bool generated = true;
        var password = InitialPasswordGenerator.Generate();

        var subjectId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await migrator.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var account = Procedure(migrator, transaction, "ids.usp_UserAccount_CreateInitialAdmin"))
            {
                account.Parameters.Add(new SqlParameter("@SubjectId", SqlDbType.UniqueIdentifier) { Value = subjectId });
                account.Parameters.Add(new SqlParameter("@UserName", SqlDbType.NVarChar, 50) { Value = request.UserName });
                account.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256) { Value = (object?)request.Email ?? DBNull.Value });
                account.Parameters.Add(new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 500) { Value = IdentityPasswordHasher.Hash(password) });
                account.Parameters.Add(new SqlParameter("@SecurityStamp", SqlDbType.NVarChar, 64) { Value = AccountStamps.NewSecurityStamp() });
                account.Parameters.Add(new SqlParameter("@ConcurrencyStamp", SqlDbType.NVarChar, 64) { Value = AccountStamps.NewConcurrencyStamp() });
                await account.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var user = Procedure(migrator, transaction, "core.usp_User_CreateInitialAdmin"))
            {
                user.Parameters.Add(new SqlParameter("@SubjectId", SqlDbType.UniqueIdentifier) { Value = subjectId });
                user.Parameters.Add(new SqlParameter("@UserName", SqlDbType.NVarChar, 50) { Value = request.UserName });
                user.Parameters.Add(new SqlParameter("@DisplayName", SqlDbType.NVarChar, 200) { Value = request.DisplayName });
                user.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256) { Value = (object?)request.Email ?? DBNull.Value });
                user.Parameters.Add(new SqlParameter("@SetupVersion", SqlDbType.NVarChar, 20) { Value = request.SetupVersion });
                user.Parameters.Add(new SqlParameter("@MachineName", SqlDbType.NVarChar, 128) { Value = request.MachineName });
                await user.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            sql.Log.Error("I-13: crearea administratorului {UserName} a eșuat: eroarea SQL {Number}: {Message}", request.UserName, ex.Number, ex.Message);
            throw new SqlSetupException($"Crearea administratorului a eșuat: eroarea SQL {ex.Number}: {ex.Message}", inner: ex);
        }

        sql.Log.Information("I-13: administratorul {UserName} a fost creat (parolă {Source}, schimbare obligatorie, 2FA obligatoriu)",
            request.UserName, generated ? "generată" : "dată de operator");
        return new InitialAdminResult(true, request.UserName, password, generated);
    }

    public static async Task<bool> ActiveAdministratorExistsAsync(SqlConnection connection, CancellationToken ct)
    {
        const string query = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM core.UserRoleAssignment AS a
                JOIN core.[User] AS u ON u.Id = a.UserId
                JOIN core.Role AS r ON r.Id = a.RoleId
                WHERE r.Code = N'ADMIN' AND r.IsSystem = 1 AND u.IsActive = 1) THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(query, connection) { CommandTimeout = SqlOperator.CommandTimeoutSeconds };
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static SqlCommand Procedure(SqlConnection connection, SqlTransaction transaction, string name) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = SqlOperator.CommandTimeoutSeconds };

    [GeneratedRegex("^[a-z][a-z0-9._-]{2,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserName();
}
