using System.Text;
using Erp.DevDatabase.Sync;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;

// Baza de dezvoltare, fara installer, cu contul Windows al dezvoltatorului: aceeasi sincronizare ca Erp.Host in Development
// (Erp.DevDatabase.Sync, 07 „Mediul de dezvoltare”) — creeaza baza daca lipseste, aplica scripturile schimbate, o reface cand
// o migrare aplicata s-a modificat —, apoi optional administratorul, utilizatorii E2E si datele demonstrative. Doar pentru
// dezvoltare: nu creeaza loginurile aplicatiei, joburile SQL Agent si nu incarca licenta. Pornire: build/dev-database.ps1.
Console.OutputEncoding = Encoding.UTF8;
var options = DevOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Utilizare: Erp.DevDatabase --server <server> --database <bază> --repo <rădăcina depozitului> [--instance DEV] [--create-admin] [--e2e-user e2e.<nume> --password-file <fișier> [--e2e-role STANDARD|ADMIN]] [--sample-data <script>]");
    return 3;
}

using var log = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();
var ct = CancellationToken.None;
var target = new SqlTarget(options.Server, options.Database, options.Instance, TrustServerCertificate: true);
var sql = new SqlOperator(target, log);

try
{
    // Aceeasi sincronizare ca Erp.Host: o baza creata aici nu mai e refacuta la prima pornire a aplicatiei.
    var sync = new DevDatabaseSync(sql, new DevSyncOptions(options.Database, options.Instance, options.Repo, ReadAdminPasswordSecret(),
        Path.Combine(options.Repo, "build", "dev-sample-data.sql")));
    var result = await sync.SyncAsync(ct);
    if (!result.Succeeded)
    {
        return 30;
    }

    await sync.ApplyAdministratorPasswordAsync(ct);

    var connection = SqlServerConnector.BuildOperatorConnectionString(target, options.Database);

    if (options.CreateAdmin)
    {
        await using var database = new SqlConnection(connection.ConnectionString);
        await database.OpenAsync(ct);
        var admin = await new InitialAdminCreator(sql).CreateAsync(database,
            new InitialAdminRequest("administrator", "Administrator ERP", null, "dev", Environment.MachineName), ct);
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
        await ResetE2EUserAsync(database, e2eUser, password, options.E2ERole, ct);
        // Parola temporara ajunge doar in fisierul cerut de testele E2E (Playwright), nu in jurnal.
        await File.WriteAllTextAsync(options.PasswordFile!, password, ct);
        log.Information("Utilizatorul E2E {User} are o parolă temporară nouă (schimbare obligatorie).", e2eUser);
    }

    if (options.SampleData is { } sampleData)
    {
        await using var database = new SqlConnection(connection.ConnectionString);
        await database.OpenAsync(ct);
        await using var command = new SqlCommand(await File.ReadAllTextAsync(sampleData, ct), database) { CommandTimeout = 120 };
        await using var counts = await command.ExecuteReaderAsync(ct);
        if (await counts.ReadAsync(ct))
        {
            log.Information("Date demonstrative: {Groups} grupuri, {Companies} firme, {Branches} puncte de lucru, {Locations} locații, {Accounts} conturi bancare, {Series} serii",
                counts.GetInt32(0), counts.GetInt32(1), counts.GetInt32(2), counts.GetInt32(3), counts.GetInt32(4), counts.GetInt32(5));
        }
    }

    log.Information("Baza de dezvoltare {Database} pe {Server} este gata.", options.Database, options.Server);
    return 0;
}
catch (Exception ex) when (ex is SqlException or SqlSetupException or InvalidOperationException or IOException)
{
    log.Error(ex, "Pregătirea bazei de dezvoltare a eșuat");
    return 30;
}

// Aceleasi user-secrets ca Erp.Host (UserSecretsId erp-development): DevDatabase:AdminPassword.
static string? ReadAdminPasswordSecret()
{
    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "erp-development", "secrets.json");
    if (!File.Exists(path))
    {
        return null;
    }

    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.TryGetProperty("DevDatabase:AdminPassword", out var value) ? value.GetString() : null;
}

// Utilizatorul testelor E2E: cont de sistem (angajatii si externii vin in etapele 6 si 8, CK_User_Kind), rolul cerut (STANDARD sau
// ADMIN, la nivel de instalare), parola temporara noua, schimbare obligatorie, fara 2FA (exceptie doar pentru baza de dezvoltare),
// deblocat, sesiuni inchise.
static async Task ResetE2EUserAsync(SqlConnection database, string userName, string password, string role, CancellationToken ct)
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
            INSERT INTO ids.UserAccount (SubjectId, UserName, NormalizedUserName, PasswordHash, PasswordChangedAt, SecurityStamp, ConcurrencyStamp, CreatedBy)
            VALUES (@SubjectId, @UserName, UPPER(@UserName), @PasswordHash, SYSUTCDATETIME(), @SecurityStamp, @ConcurrencyStamp, 1);
        END;
        DECLARE @UserId int = (SELECT Id FROM core.[User] WHERE SubjectId = @SubjectId);
        DELETE FROM core.UserRoleAssignment WHERE UserId = @UserId;
        INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
        SELECT @UserId, Id, NULL, 1 FROM core.Role WHERE Code = @Role;
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
    command.Parameters.AddWithValue("@Role", role);
    command.Parameters.AddWithValue("@PasswordHash", Erp.Security.Passwords.IdentityPasswordHasher.Hash(password));
    command.Parameters.AddWithValue("@SecurityStamp", Erp.Security.Passwords.AccountStamps.NewSecurityStamp());
    command.Parameters.AddWithValue("@ConcurrencyStamp", Erp.Security.Passwords.AccountStamps.NewConcurrencyStamp());
    await command.ExecuteNonQueryAsync(ct);
}

internal sealed record DevOptions(string Server, string Database, string Repo, string Instance, bool CreateAdmin, string? E2EUser, string? PasswordFile,
    string E2ERole, string? SampleData)
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
        var repo = Value("--repo");
        var e2eUser = Value("--e2e-user");
        var passwordFile = Value("--password-file");
        var e2eRole = Value("--e2e-role") ?? "STANDARD";
        if (e2eUser is not null && (passwordFile is null || !System.Text.RegularExpressions.Regex.IsMatch(e2eUser, "^e2e\\.[a-z0-9.]+$")
            || e2eRole is not ("STANDARD" or "ADMIN")))
        {
            return null;
        }

        return server is null || database is null || repo is null
            ? null
            : new DevOptions(server, database, repo, Value("--instance") ?? "DEV", args.Contains("--create-admin"), e2eUser, passwordFile, e2eRole, Value("--sample-data"));
    }
}
