using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Erp.Core.Api;
using Erp.Core.Application;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Security.Passwords;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Erp.Auth.Tests;

public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "AuthSqlServer";
}

/// <summary>
/// Baza ERP_IT_AUTH: administratorul creat ca la instalare (parola temporara, 2FA obligatoriu) si conturi obisnuite
/// create direct, cu hash compatibil ASP.NET Core Identity.
/// </summary>
public sealed class AuthDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public const string UserPassword = "Contabil#2026x";

    public string ConnectionString { get; private set; } = string.Empty;

    public string AdminTemporaryPassword { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_AUTH", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var admin = await new InitialAdminCreator(sql.Operator())
            .CreateAsync(connection, new InitialAdminRequest("administrator", "Administrator ERP", null, "test", "IT"), CancellationToken.None);
        AdminTemporaryPassword = admin.TemporaryPassword!;
        foreach (var user in new[] { "operator.it", "blocat.it", "revocat.it", "inactiv.it" })
        {
            await CreateUserAsync(connection, user);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<T> ScalarAsync<T>(string query, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return (await connection.ExecuteScalarAsync<T>(query, parameters))!;
    }

    public async Task ExecuteAsync(string statement, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(statement, parameters);
    }

    private static Task CreateUserAsync(SqlConnection connection, string userName) => connection.ExecuteAsync("""
        DECLARE @SubjectId uniqueidentifier = NEWID();
        INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
        VALUES (@SubjectId, @UserName, @UserName, 1, 1, 1);
        INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
        SELECT SCOPE_IDENTITY(), Id, NULL, 1 FROM core.Role WHERE Code = N'STANDARD';
        INSERT INTO ids.UserAccount (SubjectId, UserName, NormalizedUserName, PasswordHash, PasswordChangedAt, SecurityStamp, ConcurrencyStamp, CreatedBy)
        VALUES (@SubjectId, @UserName, UPPER(@UserName), @PasswordHash, SYSUTCDATETIME(), @SecurityStamp, @ConcurrencyStamp, 1);
        """, new
    {
        UserName = userName,
        PasswordHash = IdentityPasswordHasher.Hash(UserPassword),
        SecurityStamp = AccountStamps.NewSecurityStamp(),
        ConcurrencyStamp = AccountStamps.NewConcurrencyStamp(),
    });
}

/// <summary>
/// Autentificarea cap-coada pe SQL Server real, prin aceleasi middleware-uri ca Erp.Host (08 8.7, 8.8; 02.01 11.2):
/// parola temporara schimbata obligatoriu, 2FA cu cod TOTP si coduri de rezerva, blocarea dupa 5 incercari (A20),
/// antiforgery pe API si revocarea sesiunilor.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthFlowIntegrationTests(AuthDatabase database) : IClassFixture<AuthDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Criteriu", "08-IA-06")]
    public async Task Administrator_logs_in_with_any_case_changes_the_temporary_password_then_enables_2fa()
    {
        await using var host = await TestHost.StartAsync(database.ConnectionString);
        var browser = host.Browser();

        var login = await browser.PostFormAsync("/account/login", "/account/login",
            new() { ["UserName"] = "ADMINISTRATOR", ["Password"] = database.AdminTemporaryPassword });
        Assert.Equal("/account/password", login.Headers.Location?.OriginalString);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthRegistration.ApplicationCookie + "=", StringComparison.Ordinal)
            && c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("IDS_PASSWORD_CHANGE_REQUIRED", await browser.ProblemCodeAsync("/api/core/me", HttpStatusCode.Forbidden));
        Assert.Equal("/account/password", (await browser.GetAsync("/account/2fa")).Headers.Location?.OriginalString);

        var reused = await browser.PostFormAsync("/account/password", "/account/password",
            new() { ["NewPassword"] = database.AdminTemporaryPassword, ["ConfirmPassword"] = database.AdminTemporaryPassword });
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Contains("diferită de ultimele 5", await reused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var weak = await browser.PostFormAsync("/account/password", "/account/password",
            new() { ["NewPassword"] = "administrator1A", ["ConfirmPassword"] = "administrator1A" });
        Assert.Contains("numele de utilizator", await weak.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        const string newPassword = "Noua#Parola2026";
        var changed = await browser.PostFormAsync("/account/password", "/account/password",
            new() { ["NewPassword"] = newPassword, ["ConfirmPassword"] = newPassword });
        Assert.Equal("/account/login?Message=changed", changed.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/core/me")).StatusCode);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ids.UserSession s JOIN ids.UserAccount a ON a.Id = s.UserAccountId WHERE a.UserName = N'administrator' AND s.RevokedAt IS NULL;"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CAST(MustChangePassword AS int) FROM ids.UserAccount WHERE UserName = N'administrator';"));

        var again = await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "administrator", ["Password"] = newPassword });
        Assert.Equal("/account/2fa", again.Headers.Location?.OriginalString);
        Assert.Equal("IDS_TWO_FACTOR_SETUP_REQUIRED", await browser.ProblemCodeAsync("/api/core/me", HttpStatusCode.Forbidden));

        var setup = await (await browser.GetAsync("/account/2fa")).Content.ReadAsStringAsync(Ct);
        var key = Regex.Match(setup, "class=\"key\">([A-Z2-7 ]+)<").Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.NotEmpty(key);
        var wrong = await browser.PostFormAsync("/account/2fa", "/account/2fa", new() { ["Code"] = "000000", ["Password"] = newPassword });
        Assert.Contains("nu este corect", await wrong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var enabled = await browser.PostFormAsync("/account/2fa", "/account/2fa", new() { ["Code"] = Totp.Compute(key, DateTimeOffset.UtcNow), ["Password"] = newPassword });
        var recoveryCodes = Regex.Matches(await enabled.Content.ReadAsStringAsync(Ct), "<span>([A-Z0-9-]{8,})</span>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(10, recoveryCodes.Count);

        var me = await browser.GetAsync("/api/core/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains("Core.User.Manage", await me.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A doua autentificare cere al doilea pas; un cod de rezerva merge o singura data.
        var logout = await browser.PostFormAsync("/account/logout", "/account/password", []);
        Assert.Equal("/account/login?Message=signedout", logout.Headers.Location?.OriginalString);
        var withTwoFactor = await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "Administrator", ["Password"] = newPassword });
        Assert.StartsWith("/account/login-2fa", withTwoFactor.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var recovered = await browser.PostFormAsync("/account/login-2fa?UseRecoveryCode=true", "/account/login-2fa?UseRecoveryCode=true",
            new() { ["Code"] = recoveryCodes[0].ToLowerInvariant(), ["UseRecoveryCode"] = "true" });
        Assert.Equal("/", recovered.Headers.Location?.OriginalString);

        await browser.PostFormAsync("/account/logout", "/account/login", []);
        await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "administrator", ["Password"] = newPassword });
        var reusedCode = await browser.PostFormAsync("/account/login-2fa?UseRecoveryCode=true", "/account/login-2fa?UseRecoveryCode=true",
            new() { ["Code"] = recoveryCodes[0], ["UseRecoveryCode"] = "true" });
        Assert.Equal(HttpStatusCode.OK, reusedCode.StatusCode);
        Assert.Contains("nu este corect", await reusedCode.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(9, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ids.UserRecoveryCode r JOIN ids.UserAccount a ON a.Id = r.UserAccountId WHERE a.UserName = N'administrator' AND r.UsedAt IS NULL;"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM ids.UserAccount WHERE UserName = N'administrator' AND TwoFactorEnabled = 1 AND AuthenticatorKeyEncrypted IS NOT NULL;"));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.LoginEvent WHERE UserName = N'administrator' AND Success = 1 AND UserId IS NULL;"));
    }

    [Fact]
    [Trait("Criteriu", "02.01-A20")]
    public async Task Five_wrong_passwords_lock_the_account_log_every_attempt_and_queue_one_admin_notification()
    {
        await using var host = await TestHost.StartAsync(database.ConnectionString);
        var browser = host.Browser();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "blocat.it", ["Password"] = "Gresita#" + attempt + "xyz" });
            var html = await response.Content.ReadAsStringAsync(Ct);
            Assert.Contains(attempt < 5 ? "nu sunt corecte" : "blocat temporar", html, StringComparison.Ordinal);
        }

        var correctButLocked = await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "blocat.it", ["Password"] = AuthDatabase.UserPassword });
        Assert.Contains("blocat temporar", await correctButLocked.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM core.LoginEvent WHERE UserName = N'blocat.it' AND FailureReason = 'InvalidCredentials';"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM core.LoginEvent WHERE UserName = N'blocat.it' AND FailureReason = 'LockedOut' AND UserId IS NOT NULL;"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType = N'Erp.Auth.UtilizatorBlocat' AND JSON_VALUE(Payload, '$.userName') = N'blocat.it';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ids.UserAccount WHERE UserName = N'blocat.it' AND LockoutEndAt > SYSUTCDATETIME();"));
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_the_same_message_and_are_both_logged()
    {
        await using var host = await TestHost.StartAsync(database.ConnectionString);
        var browser = host.Browser();

        var unknown = await (await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "nimeni.it", ["Password"] = "Oarecare#2026x" })).Content.ReadAsStringAsync(Ct);
        var wrong = await (await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "operator.it", ["Password"] = "Oarecare#2026x" })).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Utilizatorul sau parola nu sunt corecte.", unknown, StringComparison.Ordinal);
        Assert.Contains("Utilizatorul sau parola nu sunt corecte.", wrong, StringComparison.Ordinal);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM core.LoginEvent WHERE UserName = N'nimeni.it' AND UserId IS NULL AND Success = 0;"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT AccessFailedCount FROM ids.UserAccount WHERE UserName = N'operator.it';"));
    }

    [Fact]
    public async Task Api_writes_with_the_cookie_require_the_antiforgery_header()
    {
        await using var host = await TestHost.StartAsync(database.ConnectionString);
        var browser = host.Browser();
        var login = await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "operator.it", ["Password"] = AuthDatabase.UserPassword });
        Assert.Equal("/", login.Headers.Location?.OriginalString);

        Assert.Equal(ApiAntiforgeryMiddleware.InvalidCode, await browser.ProblemCodeAsync("/api/test/echo", HttpStatusCode.Forbidden, HttpMethod.Post));

        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/antiforgery")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/test/echo");
        request.Headers.Add(AuthRegistration.AntiforgeryHeader, browser.Cookie(AuthRegistration.AntiforgeryRequestCookie));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Revoked_session_and_deactivated_user_lose_access()
    {
        await using var host = await TestHost.StartAsync(database.ConnectionString, validateEveryRequest: true);
        var browser = host.Browser();
        await browser.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "revocat.it", ["Password"] = AuthDatabase.UserPassword });
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/core/me")).StatusCode);

        await database.ExecuteAsync("UPDATE s SET RevokedAt = SYSUTCDATETIME() FROM ids.UserSession s JOIN ids.UserAccount a ON a.Id = s.UserAccountId WHERE a.UserName = N'revocat.it';");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/core/me")).StatusCode);

        var inactive = host.Browser();
        await inactive.PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "inactiv.it", ["Password"] = AuthDatabase.UserPassword });
        Assert.Equal(HttpStatusCode.OK, (await inactive.GetAsync("/api/core/me")).StatusCode);
        await database.ExecuteAsync("UPDATE core.[User] SET IsActive = 0 WHERE UserName = N'inactiv.it';");
        Assert.Equal(HttpStatusCode.Unauthorized, (await inactive.GetAsync("/api/core/me")).StatusCode);

        var refused = await host.Browser().PostFormAsync("/account/login", "/account/login", new() { ["UserName"] = "inactiv.it", ["Password"] = AuthDatabase.UserPassword });
        Assert.Contains("Contul nu este activ", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}

/// <summary>Aplicatia de test: aceleasi servicii si aceeasi ordine a middleware-urilor ca Erp.Host.</summary>
internal sealed class TestHost(WebApplication app) : IAsyncDisposable
{
    public static async Task<TestHost> StartAsync(string connectionString, bool validateEveryRequest = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Instance"] = "IT" });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddErpMediator();
        builder.Services.AddCoreInfrastructure(connectionString, new LicenseOptions(Development: true));
        builder.Services.AddErpApi();
        builder.Services.AddErpAuth(builder.Configuration);
        if (validateEveryRequest)
        {
            builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        }

        var app = builder.Build();
        app.UseAuthentication();
        app.UseErpApi();
        app.UseErpAccountGate();
        app.MapCoreApi();
        app.MapErpAuth();
        app.MapGet("/", () => Results.Content("<html><body>ERP</body></html>", "text/html"));
        app.MapPost("/api/test/echo", () => Results.NoContent()).RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new TestHost(app);
    }

    public Browser Browser() => new(app.GetTestServer());

    public ValueTask DisposeAsync() => app.DisposeAsync();
}

/// <summary>Un „browser” minimal: cookie-uri, fara redirectari automate, formulare cu token antiforgery.</summary>
internal sealed class Browser
{
    private static readonly Uri Origin = new("https://localhost");
    private readonly CookieContainer cookies = new();
    private readonly HttpClient client;

    public Browser(TestServer server)
    {
        client = new HttpClient(new CookieHandler(cookies) { InnerHandler = server.CreateHandler() }) { BaseAddress = Origin };
    }

    public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(path, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => client.SendAsync(request, TestContext.Current.CancellationToken);

    public string Cookie(string name) => cookies.GetCookies(Origin)[name]?.Value ?? throw new InvalidOperationException($"Cookie-ul {name} lipsește.");

    /// <summary>Incarca pagina formularului (tokenul antiforgery), apoi trimite formularul.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string action, string formPage, Dictionary<string, string> fields)
    {
        var page = await (await GetAsync(formPage)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(action, new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);
    }

    public async Task<string?> ProblemCodeAsync(string path, HttpStatusCode expected, HttpMethod? method = null)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method ?? HttpMethod.Get, path), TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("code").GetString();
    }

    private sealed class CookieHandler(CookieContainer jar) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = new Uri(Origin, request.RequestUri!);
            var header = jar.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    jar.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}

/// <summary>Codul TOTP (RFC 6238: HMAC-SHA1, pas de 30 s, 6 cifre), ca aplicatia de autentificare de pe telefon.</summary>
internal static class Totp
{
    public static string Compute(string base32Key, DateTimeOffset now)
    {
        var counter = BitConverter.GetBytes(now.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

#pragma warning disable CA5350 // RFC 6238 cere HMAC-SHA1, la fel ca aplicatiile de autentificare.
        var hash = HMACSHA1.HashData(FromBase32(base32Key), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
