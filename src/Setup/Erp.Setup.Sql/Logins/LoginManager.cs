using System.Text.RegularExpressions;
using Erp.Setup.Core.Answers;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Logins;

/// <summary>Numele loginurilor si utilizatorilor SQL ai unei instante (3.3, 8.4); loginurile cu litere mici (R4).</summary>
public sealed record SqlPrincipals(string AppLogin, string MigratorLogin)
{
    public const string AppUser = "erp_app";
    public const string MigratorUser = "erp_migrator";

    public static SqlPrincipals For(string instance, SqlAuthentication appAuthentication = SqlAuthentication.Sql, string? appWindowsAccount = null)
    {
        var prefix = $"erp_{instance.ToLowerInvariant()}";
        return new SqlPrincipals(
            appAuthentication == SqlAuthentication.Windows ? appWindowsAccount! : $"{prefix}_app",
            $"{prefix}_mig");
    }
}

/// <summary>Textul comenzilor de login (8.4). Contine parole: nu se jurnalizeaza niciodata (R6).</summary>
public static partial class LoginScripts
{
    public static string CreateOrAlterSqlLogin(string login, string password, string database) => $"""
        IF SUSER_ID({SqlText.SqlLiteral(login)}) IS NULL
            CREATE LOGIN {SqlText.QuoteName(login)} WITH PASSWORD = {SqlText.SqlLiteral(Password(password))},
                CHECK_POLICY = ON, CHECK_EXPIRATION = OFF,
                DEFAULT_DATABASE = {SqlText.QuoteName(database)}, DEFAULT_LANGUAGE = [us_english];
        ELSE
            ALTER LOGIN {SqlText.QuoteName(login)} WITH PASSWORD = {SqlText.SqlLiteral(Password(password))};
        ALTER LOGIN {SqlText.QuoteName(login)} ENABLE;
        """;

    public static string CreateWindowsLogin(string account, string database)
    {
        if (!WindowsAccount().IsMatch(account))
        {
            throw new SqlSetupException($"Contul Windows „{account}” este invalid (DOMENIU\\cont sau DOMENIU\\gmsa$).");
        }

        return $"""
            IF SUSER_ID({SqlText.SqlLiteral(account)}) IS NULL
                CREATE LOGIN {SqlText.QuoteName(account)} FROM WINDOWS WITH DEFAULT_DATABASE = {SqlText.QuoteName(database)};
            ALTER LOGIN {SqlText.QuoteName(account)} ENABLE;
            """;
    }

    public static string DisableLogin(string login) => $"""
        IF SUSER_ID({SqlText.SqlLiteral(login)}) IS NOT NULL
            ALTER LOGIN {SqlText.QuoteName(login)} DISABLE;
        """;

    /// <summary>Utilizatorul din baza, legat de login; refacut daca legatura s-a pierdut (de exemplu dupa restaurare).</summary>
    public static string CreateOrRemapUser(string user, string login, string? defaultSchema)
    {
        var schema = defaultSchema is null ? string.Empty : $" WITH DEFAULT_SCHEMA = {SqlText.QuoteName(defaultSchema)}";
        return $"""
            IF DATABASE_PRINCIPAL_ID({SqlText.SqlLiteral(user)}) IS NULL
                CREATE USER {SqlText.QuoteName(user)} FOR LOGIN {SqlText.QuoteName(login)}{schema};
            ELSE
                ALTER USER {SqlText.QuoteName(user)} WITH LOGIN = {SqlText.QuoteName(login)};
            """;
    }

    public static string AddRoleMember(string role, string user) =>
        $"ALTER ROLE {SqlText.QuoteName(role)} ADD MEMBER {SqlText.QuoteName(user)};";

    private static string Password(string password)
    {
        if (password.Length < 16 || password.Any(c => c is '\'' or '"' or ';' || char.IsControl(c)))
        {
            throw new SqlSetupException("Parola generată pentru loginul SQL nu respectă regulile din 8.4.");
        }

        return password;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,63}\\[A-Za-z0-9._-]{1,20}\$?$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAccount();
}

/// <summary>Pasii I-08 si I-14 (8.4): loginurile aplicatiei si de migrare, utilizatorii din baza ERP si din msdb.</summary>
public sealed class LoginManager(SqlOperator sql, SqlPrincipals principals)
{
    public SqlPrincipals Principals { get; } = principals;

    /// <summary>
    /// Loginul aplicatiei: membru in toate rolurile de modul, fara db_owner si fara drepturi pe server (SP-19).
    /// La autentificare SQL, parola data inlocuieste parola existenta (instalare reluata sau repair --rotate-secrets).
    /// </summary>
    public async Task EnsureAppLoginAsync(string database, SqlAuthentication appAuthentication, string? password, CancellationToken ct)
    {
        var login = Principals.AppLogin;
        await using (var master = await sql.OpenAsync("master", ct).ConfigureAwait(false))
        {
            var script = appAuthentication == SqlAuthentication.Sql
                ? LoginScripts.CreateOrAlterSqlLogin(login, password ?? throw new ArgumentNullException(nameof(password)), database)
                : LoginScripts.CreateWindowsLogin(login, database);
            await sql.ExecuteAsync(master, $"Creare login {login}", script, ct).ConfigureAwait(false);
        }

        await using (var erp = await sql.OpenAsync(database, ct).ConfigureAwait(false))
        {
            await sql.ExecuteAsync(erp, $"Utilizator {SqlPrincipals.AppUser} pentru {login}",
                LoginScripts.CreateOrRemapUser(SqlPrincipals.AppUser, login, "core"), ct).ConfigureAwait(false);
            foreach (var schema in ModuleOrder.Schemas)
            {
                await sql.ExecuteAsync(erp, $"{SqlPrincipals.AppUser} în role_{schema}",
                    LoginScripts.AddRoleMember($"role_{schema}", SqlPrincipals.AppUser), ct).ConfigureAwait(false);
            }
        }

        // msdb: ecranul „Ultimul backup” din aplicatie.
        await using var msdb = await sql.OpenAsync("msdb", ct).ConfigureAwait(false);
        await sql.ExecuteAsync(msdb, $"Utilizator {login} în msdb", LoginScripts.CreateOrRemapUser(login, login, null), ct).ConfigureAwait(false);
        await sql.ExecuteAsync(msdb, $"Drept SELECT pe msdb.dbo.backupset pentru {login}",
            $"GRANT SELECT ON dbo.backupset TO {SqlText.QuoteName(login)};", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Activeaza loginul de migrare cu o parola noua, nesalvata, ruleaza lucrul cu el si il dezactiveaza in <c>finally</c>,
    /// inclusiv la esec (8.4, IA-14).
    /// </summary>
    public async Task RunAsMigratorAsync(string database, Func<SqlConnectionStringBuilder, CancellationToken, Task> work, CancellationToken ct)
    {
        var password = SqlPasswordGenerator.Generate();
        var failed = false;
        try
        {
            await EnableMigratorAsync(database, password, ct).ConfigureAwait(false);
            await work(sql.ConnectionStringFor(database, Principals.MigratorLogin, password), ct).ConfigureAwait(false);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            try
            {
                // Dezactivarea nu se anuleaza odata cu lucrul.
                await DisableMigratorAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (failed)
            {
                // Eroarea initiala ramane cea raportata; esecul dezactivarii trebuie vazut in jurnal.
                sql.Log.Error(ex, "Loginul {Login} NU a putut fi dezactivat după eșec; dezactivați-l manual: ALTER LOGIN [{Login}] DISABLE;",
                    Principals.MigratorLogin, Principals.MigratorLogin);
            }
        }
    }

    public async Task DisableMigratorAsync(CancellationToken ct)
    {
        await using var master = await sql.OpenAsync("master", ct).ConfigureAwait(false);
        await sql.ExecuteAsync(master, $"Dezactivare login {Principals.MigratorLogin}", LoginScripts.DisableLogin(Principals.MigratorLogin), ct).ConfigureAwait(false);
    }

    /// <summary>I-08: activeaza loginul de migrare cu o parola noua, nesalvata; intoarce conexiunea cu care ruleaza I-09…I-13.</summary>
    public async Task<SqlConnectionStringBuilder> EnableMigratorAsync(string database, CancellationToken ct)
    {
        var password = SqlPasswordGenerator.Generate();
        await EnableMigratorAsync(database, password, ct).ConfigureAwait(false);
        return sql.ConnectionStringFor(database, Principals.MigratorLogin, password);
    }

    private async Task EnableMigratorAsync(string database, string password, CancellationToken ct)
    {
        var login = Principals.MigratorLogin;
        await using (var master = await sql.OpenAsync("master", ct).ConfigureAwait(false))
        {
            await sql.ExecuteAsync(master, $"Creare login {login}", LoginScripts.CreateOrAlterSqlLogin(login, password, database), ct).ConfigureAwait(false);
        }

        await using var erp = await sql.OpenAsync(database, ct).ConfigureAwait(false);
        await sql.ExecuteAsync(erp, $"Utilizator {SqlPrincipals.MigratorUser} pentru {login}",
            LoginScripts.CreateOrRemapUser(SqlPrincipals.MigratorUser, login, "core"), ct).ConfigureAwait(false);
        await sql.ExecuteAsync(erp, $"{SqlPrincipals.MigratorUser} în db_owner",
            LoginScripts.AddRoleMember("db_owner", SqlPrincipals.MigratorUser), ct).ConfigureAwait(false);
    }
}
