using Dapper;
using Erp.Core.Application;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Core.Infrastructure.Users;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Containerul SQL comun (Erp.Testing.Sql), ca fixture de colectie al acestui assembly.</summary>
public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "CoreSqlServer";
}

/// <summary>Baza ERP_IT_CORE, cu utilizatorii de test: administrator la nivel de instalare, standard pe firma 5, inactiv, extern expirat.</summary>
public sealed class CoreDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public Guid Admin { get; } = Guid.NewGuid();

    public Guid Standard { get; } = Guid.NewGuid();

    public Guid Inactive { get; } = Guid.NewGuid();

    public Guid Expired { get; } = Guid.NewGuid();

    public int AdminId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_CORE", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        AdminId = await connection.ExecuteScalarAsync<int>("""
            DECLARE @Ids TABLE (Id int, UserName nvarchar(50));
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            OUTPUT inserted.Id, inserted.UserName INTO @Ids
            VALUES (@Admin, N'admin.it', N'Admin IT', 1, 1, 1), (@Standard, N'standard.it', N'Standard IT', 1, 1, 1),
                   (@Inactive, N'inactiv.it', N'Inactiv IT', 0, 1, 1);
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, SponsorUserId, AccessReason, ValidUntil, CreatedBy)
            OUTPUT inserted.Id, inserted.UserName INTO @Ids
            VALUES (@Expired, N'extern.it', N'Extern IT', 1, 0, 1, N'Audit extern', DATEADD(day, -1, CAST(SYSUTCDATETIME() AS date)), 1);
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT i.Id, r.Id, a.CompanyId, 1
            FROM (VALUES (N'admin.it', N'ADMIN', NULL), (N'standard.it', N'STANDARD', 5), (N'inactiv.it', N'ADMIN', NULL), (N'extern.it', N'AUDITOR', 5))
                AS a (UserName, RoleCode, CompanyId)
            JOIN @Ids AS i ON i.UserName = a.UserName
            JOIN core.Role AS r ON r.Code = a.RoleCode;
            SELECT Id FROM @Ids WHERE UserName = N'admin.it';
            """, new { Admin, Standard, Inactive, Expired });

        // Permisiunea unui modul livrat dupa Nucleu, fara legatura in core.RolePermission (ca dupa un upgrade).
        await connection.ExecuteAsync("""
            INSERT INTO core.Permission (Code, ModuleCode, Description, CreatedBy)
            VALUES (N'Sales.Invoice.View', N'Sales', N'Vizualizare facturi (test)', 1);
            """);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Pipeline-ul pe SQL Server real (02.01, 4.1): idempotenta (A16), atomicitatea document-outbox-audit (P8), permisiunile efective.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CorePlatformIntegrationTests(CoreDatabase database) : IClassFixture<CoreDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (ISender Sender, FakeContext Context, IServiceScope Scope) Scope(string? idempotencyKey = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(CorePlatformIntegrationTests).Assembly);
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(null, Development: true));
        var context = new FakeContext { UserId = database.AdminId, CompanyId = 0, IdempotencyKey = idempotencyKey };
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Erp.Core.Application.Me.ICurrentUserProfile>(context);
        services.AddScoped<HandlerCalls>();
        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<ISender>(), context, scope);
    }

    private async Task<T> Scalar<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException(sql);
    }

    private Task<int> OutboxRows(string text) =>
        Scalar<int>("SELECT COUNT(*) FROM core.Outbox WHERE JSON_VALUE(Payload, '$.text') = @text;", new { text });

    [Fact]
    [Trait("Criteriu", "02.01-A16")]
    public async Task Resending_with_the_same_idempotency_key_creates_one_effect_and_returns_the_same_result()
    {
        var key = "form-" + Guid.NewGuid().ToString("N");
        var text = "once-" + Guid.NewGuid().ToString("N")[..10];

        var (first, context, scope1) = Scope(key);
        var result1 = await first.Send(new PingCommand(text), Ct);
        var (second, _, scope2) = Scope(key);
        var result2 = await second.Send(new PingCommand(text), Ct);
        scope1.Dispose();
        scope2.Dispose();

        Assert.Equal(result1, result2);
        Assert.Equal(1, await OutboxRows(text));
        Assert.Equal(1, await Scalar<int>("SELECT COUNT(*) FROM core.IdempotencyKey WHERE [Key] = @key AND ResultJson IS NOT NULL;", new { key }));
        Assert.Equal(1, await Scalar<int>("""
            SELECT COUNT(*) FROM core.AuditEntry
            WHERE Action = N'Core.Test.Ping' AND EntityType = N'Core.Test' AND EntityId = 42
              AND CorrelationId = @CorrelationId AND UserId = @UserId AND JSON_VALUE(Changes, '$.text') = @text;
            """, new { context.CorrelationId, context.UserId, text }));
        Assert.Equal(context.CorrelationId, await Scalar<Guid>(
            "SELECT CorrelationId FROM core.Outbox WHERE JSON_VALUE(Payload, '$.text') = @text;", new { text }));
    }

    [Fact]
    [Trait("Criteriu", "02.01-A16")]
    public async Task Concurrent_resends_with_the_same_key_create_a_single_effect()
    {
        var key = "dblclick-" + Guid.NewGuid().ToString("N");
        var text = "conc-" + Guid.NewGuid().ToString("N")[..10];
        var scopes = Enumerable.Range(0, 5).Select(_ => Scope(key)).ToList();

        var results = await Task.WhenAll(scopes.Select(s => s.Sender.Send(new PingCommand(text), Ct)));
        scopes.ForEach(s => s.Scope.Dispose());

        Assert.Single(results.Distinct());
        Assert.Equal(1, await OutboxRows(text));
    }

    [Fact]
    public async Task Failure_after_writing_rolls_back_outbox_audit_and_idempotency_key_together()
    {
        var key = "fail-" + Guid.NewGuid().ToString("N");
        var text = "fail-" + Guid.NewGuid().ToString("N")[..10];
        var (sender, _, scope) = Scope(key);

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand(text, Fail: true), Ct));
        scope.Dispose();

        Assert.Equal("CORE_TEST_FAILED", ex.Code);
        Assert.Equal(0, await OutboxRows(text));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM core.AuditEntry WHERE JSON_VALUE(Changes, '$.text') = @text;", new { text }));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM core.IdempotencyKey WHERE [Key] = @key;", new { key }));
    }

    [Fact]
    public async Task Effective_permissions_follow_role_assignments_company_and_account_validity()
    {
        await using var session = new Infrastructure.Data.DbSession(database.ConnectionString);
        var loader = new UserContextLoader(session);
        var catalog = await Scalar<int>("SELECT COUNT(*) FROM core.Permission;", new { });

        var adminAnyCompany = await loader.LoadAsync(database.Admin, 99, Ct);
        var adminNoCompany = await loader.LoadAsync(database.Admin, null, Ct);
        var standardOwn = await loader.LoadAsync(database.Standard, 5, Ct);
        var standardOther = await loader.LoadAsync(database.Standard, 6, Ct);

        // ADMIN are drepturi depline: tot catalogul, inclusiv permisiunile modulelor adaugate ulterior.
        Assert.Equal(catalog, adminAnyCompany!.Permissions.Count);
        Assert.Contains("Sales.Invoice.View", adminAnyCompany.Permissions);
        Assert.Equal((99, catalog), (adminAnyCompany.CompanyId, adminNoCompany!.Permissions.Count));
        Assert.Equal(0, adminNoCompany.CompanyId);
        Assert.Equal(["Core.Company.View", "Core.Item.View", "Core.Partner.View"], standardOwn!.Permissions.Order(StringComparer.Ordinal));
        Assert.Empty(standardOther!.Permissions);
        Assert.Null(await loader.LoadAsync(database.Inactive, null, Ct));
        Assert.Null(await loader.LoadAsync(database.Expired, 5, Ct));
        Assert.Null(await loader.LoadAsync(Guid.NewGuid(), null, Ct));
    }
}
