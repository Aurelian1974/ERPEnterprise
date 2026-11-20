using System.Text.RegularExpressions;
using Erp.Setup.Core.Answers;

namespace Erp.Setup.Sql.Logins;

/// <summary>Un login Windows al aplicatiei si utilizatorul lui din baza ERP (08, 5.8).</summary>
public sealed record AppPrincipal(string Login, string User);

/// <summary>Identitatile aplicatiei pe SQL Server (08, 5.8, D3, D21). Nu exista parole.</summary>
public static class AppPrincipals
{
    public static string PoolName(string instance) => $"ERP-{instance}";

    public static string ServiceName(string instance) => $"ErpWorker-{instance}";

    /// <param name="account">Contul gMSA sau al calculatorului (DOMENIU\nume$), cand SQL e pe alt calculator.</param>
    public static IReadOnlyList<AppPrincipal> For(string instance, AppIdentity identity, string? account) => identity switch
    {
        AppIdentity.VirtualAccounts =>
        [
            new AppPrincipal($@"IIS APPPOOL\{PoolName(instance)}", "erp_web"),
            new AppPrincipal($@"NT SERVICE\{ServiceName(instance)}", "erp_worker"),
        ],
        _ => [new AppPrincipal(account ?? throw new ArgumentNullException(nameof(account)), "erp_app")],
    };
}

/// <summary>Textul comenzilor de login (08, 5.8). Numele trec prin QuoteName, dupa validare (abaterea de la SP-06, 08, 15).</summary>
public static partial class LoginScripts
{
    public static string CreateWindowsLogin(string account, string database)
    {
        if (!WindowsAccount().IsMatch(account))
        {
            throw new SqlSetupException($"Contul Windows „{account}” este invalid (DOMENIU\\cont, DOMENIU\\gmsa$, IIS APPPOOL\\pool sau NT SERVICE\\serviciu).");
        }

        return $"""
            IF SUSER_ID({SqlText.SqlLiteral(account)}) IS NULL
                CREATE LOGIN {SqlText.QuoteName(account)} FROM WINDOWS WITH DEFAULT_DATABASE = {SqlText.QuoteName(database)}, DEFAULT_LANGUAGE = [us_english];
            ALTER LOGIN {SqlText.QuoteName(account)} ENABLE;
            """;
    }

    public static string DisableLogin(string login) => $"""
        IF SUSER_ID({SqlText.SqlLiteral(login)}) IS NOT NULL
            ALTER LOGIN {SqlText.QuoteName(login)} DISABLE;
        """;

    /// <summary>
    /// Utilizatorul din baza, legat de login; refacut daca legatura s-a pierdut (dupa o restaurare sau o recuperare).
    /// Un utilizator existent al aceluiasi login, cu alt nume (o instalare anterioara), se redenumeste: un login are un singur utilizator pe baza.
    /// </summary>
    public static string CreateOrRemapUser(string user, string login, string? defaultSchema)
    {
        var schema = defaultSchema is null ? string.Empty : $" WITH DEFAULT_SCHEMA = {SqlText.QuoteName(defaultSchema)}";
        return $"""
            IF DATABASE_PRINCIPAL_ID({SqlText.SqlLiteral(user)}) IS NULL
            BEGIN
                DECLARE @existing sysname = (
                    SELECT TOP (1) dp.name FROM sys.database_principals AS dp
                    JOIN sys.server_principals AS sp ON sp.sid = dp.sid
                    WHERE sp.name = {SqlText.SqlLiteral(login)} AND dp.type IN (N'U', N'G', N'S'));
                DECLARE @rename nvarchar(max) = N'ALTER USER ' + QUOTENAME(@existing) + N' WITH NAME = ' + {SqlText.SqlLiteral(SqlText.QuoteName(user))};
                IF @existing IS NOT NULL
                    EXEC(@rename);
                ELSE
                    CREATE USER {SqlText.QuoteName(user)} FOR LOGIN {SqlText.QuoteName(login)}{schema};
            END
            ELSE
                ALTER USER {SqlText.QuoteName(user)} WITH LOGIN = {SqlText.QuoteName(login)};
            """;
    }

    public static string AddRoleMember(string role, string user) =>
        $"ALTER ROLE {SqlText.QuoteName(role)} ADD MEMBER {SqlText.QuoteName(user)};";

    [GeneratedRegex(@"^(IIS APPPOOL|NT SERVICE|[A-Za-z0-9][A-Za-z0-9.\-]{0,63})\\[A-Za-z0-9._\-]{1,64}\$?$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAccount();
}

/// <summary>
/// Pasul I-11 (08, 5.8): loginurile Windows ale aplicatiei, utilizatorii din baza ERP (membri in rolurile de modul, fara db_owner,
/// SP-19) si din msdb (ecranul „Ultimul backup”). Idempotent (R10).
/// </summary>
public sealed class LoginManager(SqlOperator sql)
{
    public async Task EnsureAsync(string database, IReadOnlyList<AppPrincipal> principals, CancellationToken ct)
    {
        await using (var master = await sql.OpenAsync("master", ct).ConfigureAwait(false))
        {
            foreach (var principal in principals)
            {
                await sql.ExecuteAsync(master, $"Login {principal.Login}", LoginScripts.CreateWindowsLogin(principal.Login, database), ct).ConfigureAwait(false);
            }
        }

        await EnsureUsersAsync(database, principals, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Utilizatorii loginurilor existente: in baza ERP (rolurile de modul) si in msdb. Separat de crearea loginurilor pentru ca
    /// „Recuperează” reface doar legatura utilizatorilor, iar testele din containerul Linux folosesc un login SQL in locul celui Windows.
    /// </summary>
    public async Task EnsureUsersAsync(string database, IReadOnlyList<AppPrincipal> principals, CancellationToken ct)
    {
        foreach (var principal in principals)
        {
            await using (var erp = await sql.OpenAsync(database, ct).ConfigureAwait(false))
            {
                await sql.ExecuteAsync(erp, $"Utilizator {principal.User} pentru {principal.Login}",
                    LoginScripts.CreateOrRemapUser(principal.User, principal.Login, "core"), ct).ConfigureAwait(false);
                foreach (var schema in ModuleOrder.Schemas)
                {
                    await sql.ExecuteAsync(erp, $"{principal.User} în role_{schema}",
                        LoginScripts.AddRoleMember($"role_{schema}", principal.User), ct).ConfigureAwait(false);
                }
            }

            await using var msdb = await sql.OpenAsync("msdb", ct).ConfigureAwait(false);
            await sql.ExecuteAsync(msdb, $"Utilizator {principal.Login} în msdb", LoginScripts.CreateOrRemapUser(principal.Login, principal.Login, null), ct)
                .ConfigureAwait(false);
            await sql.ExecuteAsync(msdb, $"Drept SELECT pe msdb.dbo.backupset pentru {principal.Login}",
                $"GRANT SELECT ON dbo.backupset TO {SqlText.QuoteName(principal.Login)};", ct).ConfigureAwait(false);
        }
    }

    /// <summary>„Dezinstaleaza” (08, 11.6): loginurile se dezactiveaza, nu se sterg.</summary>
    public async Task DisableAsync(IReadOnlyList<string> logins, CancellationToken ct)
    {
        await using var master = await sql.OpenAsync("master", ct).ConfigureAwait(false);
        foreach (var login in logins)
        {
            await sql.ExecuteAsync(master, $"Dezactivare login {login}", LoginScripts.DisableLogin(login), ct).ConfigureAwait(false);
        }
    }
}
