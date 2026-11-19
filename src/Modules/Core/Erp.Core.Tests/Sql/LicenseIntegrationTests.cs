using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Data;
using Erp.Core.Infrastructure.Licensing;
using Erp.LicenseGen.Core;
using Erp.Licensing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Core.Tests.Sql;

/// <summary>Baza ERP_IT_LICENSE: o firma cu CUI-ul in licenta (10000008) si una fara (45678900).</summary>
public sealed class LicenseDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int LicensedCompany { get; private set; }

    public int UnlicensedCompany { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_LICENSE", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        var ids = (await connection.QueryAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, TradeRegisterNo, ShareCapital, Address, City, CountyCode,
                CountryCode, DefaultPrecisionPolicyId, CreatedBy)
            OUTPUT inserted.Id
            SELECT v.Code, v.Name, v.FiscalCode, 1, N'J40/1/2020', 200, N'Str. Test 1', N'București', N'B', 'RO', p.Id, 1
            FROM (VALUES (N'LIC1', N'Firma licențiată', N'10000008'), (N'LIC2', N'Firma nelicențiată', N'45678900')) AS v (Code, Name, FiscalCode)
            CROSS JOIN core.PrecisionPolicy AS p
            WHERE p.Code = N'STANDARD'
            ORDER BY v.Code;
            """)).ToList();
        LicensedCompany = ids.Min();
        UnlicensedCompany = ids.Max();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Licenta in baza (08, 4.1), reincarcarea ei (08, 4.3) si data de referinta (08, 4.4) pe SQL Server real.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LicenseIntegrationTests(LicenseDatabase database) : IClassFixture<LicenseDatabase>, IDisposable
{
    private readonly LicenseSigningKey key = LicenseSigningKey.Generate(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => key.Dispose();

    private static License Sample(string licenseId, DateOnly issuedAt) => new()
    {
        LicenseId = licenseId,
        Product = License.ProductCode,
        CustomerName = "Grupul Exemplu",
        FiscalCodes = "10000008:12345674",
        Modules = ["Core", "Finance"],
        Limits = new LicenseLimits(100, 300, 10),
        IssuedAt = issuedAt,
        UpdatesUntil = new DateOnly(2027, 6, 30),
    };

    private Task<int> SaveAsync(License license) => SaveAsync(LicenseWriter.Write(license, key), license);

    private async Task<int> SaveAsync(byte[] file, License license)
    {
        await using var session = new DbSession(database.ConnectionString);
        await session.BeginAsync(Ct);
        var id = await new LicenseStore(session).SaveAsync(file, license, key.KeyId, 1, Ct);
        await session.CommitAsync(Ct);
        return id;
    }

    private (LicenseInfo Info, LicenseRefresher Refresher) Refresher(TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var info = new LicenseInfo(new LicenseOptions(Development: false), time, NullLogger<LicenseInfo>.Instance);
        var reader = new LicenseReader(new LicenseKeyRing([key.ToPublicKey()]), time);
        return (info, new LicenseRefresher(database.ConnectionString, info, reader, time, NullLogger<LicenseRefresher>.Instance));
    }

    [Fact]
    public async Task Same_license_saved_twice_keeps_one_row_and_a_new_one_becomes_current()
    {
        var license = Sample("LIC-2026-000201", new DateOnly(2026, 11, 1));
        var file = LicenseWriter.Write(license, key);
        var first = await SaveAsync(file, license);
        var again = await SaveAsync(file, license);
        var second = await SaveAsync(Sample("LIC-2026-000202", new DateOnly(2026, 11, 2)));

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(first, again);
        Assert.NotEqual(first, second);
        Assert.Equal(second, await connection.ExecuteScalarAsync<int>("SELECT Id FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Null(await connection.ExecuteScalarAsync<DateTime?>($"SELECT ValidUntil FROM core.InstallationLicense WHERE Id = {second};"));
        Assert.Equal(new DateTime(2027, 6, 30), await connection.ExecuteScalarAsync<DateTime>($"SELECT UpdatesUntil FROM core.InstallationLicense WHERE Id = {second};"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>($"SELECT LimitCompanies FROM core.InstallationLicense WHERE Id = {second};"));

        await using var session = new DbSession(database.ConnectionString);
        var history = await new LicenseStore(session).ListAsync(Ct);
        Assert.Equal("LIC-2026-000202", history[0].LicenseId);
        Assert.True(history[0].IsCurrent);
        Assert.Single(history, h => h.LicenseId == "LIC-2026-000201" && !h.IsCurrent);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Refresh_loads_the_current_license_and_finds_companies_missing_from_it()
    {
        await SaveAsync(Sample("LIC-2026-000210", new DateOnly(2026, 11, 10)));
        var (info, refresher) = Refresher();

        await refresher.RefreshAsync(Ct);

        Assert.True(info.HasLicense);
        Assert.Equal("LIC-2026-000210", info.Current!.LicenseId);
        Assert.True(info.IsCompanyLicensed(database.LicensedCompany));
        Assert.False(info.IsCompanyLicensed(database.UnlicensedCompany));
        Assert.True(info.IsModuleActive("Finance"));
        Assert.False(info.IsModuleActive("Sales"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public async Task Clock_set_back_keeps_the_highest_date_and_is_audited()
    {
        await SaveAsync(Sample("LIC-2026-000220", new DateOnly(2026, 11, 12)));
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                EXEC core.usp_LicenseClock_Touch @Today = '2099-01-01', @UserId = 1;
                """);
        }

        var (info, refresher) = Refresher();
        await refresher.RefreshAsync(Ct);

        Assert.True(info.ClockBehind);
        Assert.Equal(new DateOnly(2099, 1, 1), info.ReferenceDate);
        await using var check = new SqlConnection(database.ConnectionString);
        Assert.Equal("2099-01-01", await check.ExecuteScalarAsync<string>(
            "SELECT Value FROM core.Setting WHERE Scope = 'Installation' AND ScopeId IS NULL AND [Key] = 'License.HighestSeenDate';"));
        Assert.True(await check.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Core.License.ClockBehind';") >= 1);
    }

    [Fact]
    public async Task Migration_removes_the_signature_column_and_seeds_the_setup_flag()
    {
        await using var connection = new SqlConnection(database.ConnectionString);

        Assert.Null(await connection.ExecuteScalarAsync<int?>("SELECT COL_LENGTH('core.InstallationLicense', 'Signature');"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('core.InstallationLicense') AND name = 'ValidUntil';"));
        Assert.Equal("false", await connection.ExecuteScalarAsync<string>(
            "SELECT Value FROM core.Setting WHERE Scope = 'Installation' AND ScopeId IS NULL AND [Key] = 'Installation.SetupCompleted';"));
    }
}
