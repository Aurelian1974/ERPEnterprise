using System.Text;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;

// Baza de dezvoltare, fara installer: aceiasi pasi SQL ca la instalare (I-06, I-07, I-09…I-11, optional I-13), cu contul Windows
// al dezvoltatorului. Doar pentru dezvoltare: nu creeaza loginurile _app/_mig, joburile SQL Agent si nu incarca licenta.
// Pornire: build/dev-database.ps1 (aseaza scripturile din depozit ca in kit, apoi ruleaza acest program).
Console.OutputEncoding = Encoding.UTF8;
var options = DevOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Utilizare: Erp.DevDatabase --server <server> --database <bază> --scripts <folder database> [--instance DEV] [--create-admin] [--e2e-user e2e.<nume> --password-file <fișier>]");
    return 3;
}

using var log = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();
var ct = CancellationToken.None;
var target = new SqlTarget(options.Server, options.Database, SqlAuthentication.Windows, null, SqlEncrypt.Mandatory, true, options.Instance);
var sql = new SqlOperator(target, null, log);

try
{
    var installId = await ReadInstallIdAsync(target, options.Database, ct) ?? Guid.NewGuid();
    var outcome = await new DatabaseCreator(sql).CreateAsync(
        new DatabaseCreateOptions(options.Database, options.Instance, installId, null, null, 256, 256), ct);
    log.Information("Baza {Database}: {Outcome}", options.Database, outcome);

    var bootstrap = await new BootstrapRunner(sql).RunAsync(options.Database, Path.Combine(options.Scripts, "bootstrap"), ct);
    log.Information("Bootstrap: {Count} scripturi", bootstrap);

    var connection = SqlServerConnector.BuildOperatorConnectionString(target, null);
    connection.InitialCatalog = options.Database;
    var scripts = KitScripts.Load(options.Scripts);
    var runner = new MigrationRunner(log);
    log.Information("Migrații: {Count} noi", runner.RunMigrations(connection.ConnectionString, scripts).ScriptsExecuted);
    log.Information("Obiecte programabile: {Count}", runner.RunProgrammability(connection.ConnectionString, scripts).ScriptsExecuted);
    log.Information("Date livrate: {Count} scripturi", runner.RunSeed(connection.ConnectionString, scripts).ScriptsExecuted);

    if (options.CreateAdmin)
    {
        await using var database = new SqlConnection(connection.ConnectionString);
        await database.OpenAsync(ct);
        var admin = await new InitialAdminCreator(sql).CreateAsync(database,
            new InitialAdminRequest("administrator", "Administrator ERP", null, null, "dev", Environment.MachineName), ct);
        if (admin.Created)
        {
            // Afisata o singura data, doar pe ecran (R6); in jurnal nu ajunge.
            Console.WriteLine();
            Console.WriteLine($"Administrator: {admin.UserName}, parola temporară: {admin.TemporaryPassword}");
        }
    }

    if (options.E2EUser is { } e2eUser)
    {
        await using var database = new SqlConnection(connection.ConnectionString);
        await database.OpenAsync(ct);
        var password = Erp.Setup.Core.Secrets.InitialPasswordGenerator.Generate();
        await ResetE2EUserAsync(database, e2eUser, password, ct);
        // Parola temporara ajunge doar in fisierul cerut de testele E2E (Playwright), nu in jurnal.
        await File.WriteAllTextAsync(options.PasswordFile!, password, ct);
        log.Information("Utilizatorul E2E {User} are o parolă temporară nouă (schimbare obligatorie).", e2eUser);
    }

    log.Information("Baza de dezvoltare {Database} pe {Server} este gata.", options.Database, options.Server);
    return 0;
}
catch (Exception ex) when (ex is SqlException or SqlSetupException or InvalidOperationException or IOException)
{
    log.Error(ex, "Pregătirea bazei de dezvoltare a eșuat");
    return 30;
}

// Reluarea pe aceeasi baza: DatabaseCreator accepta doar baza creata cu acelasi ErpInstallId (8.1).
static async Task<Guid?> ReadInstallIdAsync(SqlTarget target, string database, CancellationToken ct)
{
    await using var master = new SqlConnection(SqlServerConnector.BuildOperatorConnectionString(target, null).ConnectionString);
    await master.OpenAsync(ct);
    await using var exists = new SqlCommand("SELECT DB_ID(@name);", master);
    exists.Parameters.AddWithValue("@name", database);
    if (await exists.ExecuteScalarAsync(ct) is DBNull or null)
    {
        return null;
    }

    master.ChangeDatabase(database);
    await using var read = new SqlCommand(
        "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", master);
    return await read.ExecuteScalarAsync(ct) is string value && Guid.TryParse(value, out var id) ? id : null;
}

// Utilizatorul testelor E2E: cont de sistem (angajatii si externii vin in etapele 6 si 8, CK_User_Kind), rol STANDARD, parola temporara noua, schimbare obligatorie, fara 2FA, deblocat, sesiuni inchise.
static async Task ResetE2EUserAsync(SqlConnection database, string userName, string password, CancellationToken ct)
{
    await using var command = new SqlCommand("""
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @SubjectId uniqueidentifier = (SELECT SubjectId FROM ids.UserAccount WHERE UserName = @UserName);
        IF @SubjectId IS NULL
        BEGIN
            SET @SubjectId = NEWID();
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            VALUES (@SubjectId, @UserName, N'Utilizator E2E', 1, 1, 1);
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT SCOPE_IDENTITY(), Id, NULL, 1 FROM core.Role WHERE Code = N'STANDARD';
            INSERT INTO ids.UserAccount (SubjectId, UserName, NormalizedUserName, PasswordHash, PasswordChangedAt, SecurityStamp, ConcurrencyStamp, CreatedBy)
            VALUES (@SubjectId, @UserName, UPPER(@UserName), @PasswordHash, SYSUTCDATETIME(), @SecurityStamp, @ConcurrencyStamp, 1);
        END;
        UPDATE ids.UserAccount
        SET PasswordHash = @PasswordHash, PasswordChangedAt = SYSUTCDATETIME(), MustChangePassword = 1, TwoFactorRequired = 0,
            TwoFactorEnabled = 0, AuthenticatorKeyEncrypted = NULL, AccessFailedCount = 0, LockoutEndAt = NULL, IsActive = 1,
            SecurityStamp = @SecurityStamp, ConcurrencyStamp = @ConcurrencyStamp
        WHERE SubjectId = @SubjectId;
        DELETE FROM ids.PasswordHistory WHERE UserAccountId = (SELECT Id FROM ids.UserAccount WHERE SubjectId = @SubjectId);
        UPDATE core.[User] SET IsActive = 1 WHERE SubjectId = @SubjectId;
        COMMIT;
        """, database);
    command.Parameters.AddWithValue("@UserName", userName);
    command.Parameters.AddWithValue("@PasswordHash", Erp.Security.Passwords.IdentityPasswordHasher.Hash(password));
    command.Parameters.AddWithValue("@SecurityStamp", Erp.Security.Passwords.AccountStamps.NewSecurityStamp());
    command.Parameters.AddWithValue("@ConcurrencyStamp", Erp.Security.Passwords.AccountStamps.NewConcurrencyStamp());
    await command.ExecuteNonQueryAsync(ct);
}

internal sealed record DevOptions(string Server, string Database, string Scripts, string Instance, bool CreateAdmin, string? E2EUser, string? PasswordFile)
{
    public static DevOptions? Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var server = Value("--server");
        var database = Value("--database");
        var scripts = Value("--scripts");
        var e2eUser = Value("--e2e-user");
        var passwordFile = Value("--password-file");
        if (e2eUser is not null && (passwordFile is null || !System.Text.RegularExpressions.Regex.IsMatch(e2eUser, "^e2e\\.[a-z0-9.]+$")))
        {
            return null;
        }

        return server is null || database is null || scripts is null
            ? null
            : new DevOptions(server, database, scripts, Value("--instance") ?? "DEV", args.Contains("--create-admin"), e2eUser, passwordFile);
    }
}
