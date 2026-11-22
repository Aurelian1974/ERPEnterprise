using Dapper;
using Erp.Core.Application;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Finance.Application;
using Erp.Finance.Infrastructure;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Containerul SQL comun (Erp.Testing.Sql), ca fixture de colectie al acestui assembly.</summary>
public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "FinanceSqlServer";
}

/// <summary>
/// Baza ERP_IT_FINANCE, creata ca la client (bootstrap, migratiile, programabilitatea si seed-ul tuturor modulelor), cu datele demo ale
/// machetelor: Exemplu Distributie SRL cu sediul si punctul de lucru Cluj, contul BCR in lei, administratorul si casiera Elena Dumitru.
/// </summary>
public sealed class FinanceDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int CashierId { get; private set; }

    public int AccountantId { get; private set; }

    public int CompanyId { get; private set; }

    public int OtherCompanyId { get; private set; }

    public int HeadOfficeId { get; private set; }

    public int ClujId { get; private set; }

    public int BcrRonId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_FINANCE", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        CompanyId = await NewCompany(connection, "EXDIST", "Exemplu Distribuție SRL", "18547290");
        AdminId = await NewUser(connection, "administrator", "Administrator", "ADMIN", null);
        CashierId = await NewUser(connection, "elena.dumitru", "Elena Dumitru", "CASIER", CompanyId);
        AccountantId = await NewUser(connection, "ion.popescu", "Ion Popescu", "CONTABIL_SEF", CompanyId);
        OtherCompanyId = await NewCompany(connection, "EXPROD", "Exemplu Producție SRL", "14399840");
        HeadOfficeId = await NewBranch(connection, CompanyId, "SEDIU", "Sediu București", "București", "B");
        ClujId = await NewBranch(connection, CompanyId, "PL-CLUJ", "PL Cluj", "Cluj-Napoca", "CJ");
        BcrRonId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.CompanyBankAccount (CompanyId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@CompanyId, 'RO49RNCB0082004530120001', N'BCR', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Administratorul integrat (utilizator de sistem) sau un angajat al firmei cu cont de utilizator (CK_User_Kind).</summary>
    private static Task<int> NewUser(SqlConnection connection, string userName, string displayName, string role, int? employeeCompanyId) =>
        connection.ExecuteScalarAsync<int>("""
            DECLARE @EmployeeId int = NULL;
            IF @CompanyId IS NOT NULL
            BEGIN
                INSERT INTO core.Employee (CompanyId, EmployeeNo, FirstName, LastName, IsActive, CreatedBy)
                VALUES (@CompanyId, @UserName, LEFT(@DisplayName, CHARINDEX(N' ', @DisplayName) - 1),
                        SUBSTRING(@DisplayName, CHARINDEX(N' ', @DisplayName) + 1, 100), 1, 1);
                SET @EmployeeId = SCOPE_IDENTITY();
            END;
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, IsEmployee, EmployeeId, CreatedBy)
            VALUES (NEWID(), @UserName, @DisplayName, 1, CASE WHEN @EmployeeId IS NULL THEN 1 ELSE 0 END,
                    CASE WHEN @EmployeeId IS NULL THEN 0 ELSE 1 END, @EmployeeId, 1);
            DECLARE @Id int = SCOPE_IDENTITY();
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT @Id, r.Id, NULL, 1 FROM core.Role AS r WHERE r.Code = @Role;
            SELECT @Id;
            """, new { UserName = userName, DisplayName = displayName, Role = role, CompanyId = employeeCompanyId });

    private static Task<int> NewCompany(SqlConnection connection, string code, string name, string fiscalCode) =>
        connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
            SELECT @Code, @Name, @FiscalCode, 1, 0, N'J40/1234/2015', N'Str. Exemplu 1', N'București', N'B', 'RO', Id, 1, 1, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = code, Name = name, FiscalCode = fiscalCode });

    private static Task<int> NewBranch(SqlConnection connection, int companyId, string code, string name, string city, string county) =>
        connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Branch (CompanyId, Code, Name, Address, City, CountyCode, CountryCode, CreatedBy)
            VALUES (@CompanyId, @Code, @Name, N'Str. Exemplu 2', @City, @County, 'RO', 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId = companyId, Code = code, Name = name, City = city, County = county });
}

/// <summary>Trimiterea cererilor prin pipeline-ul comun (licenta, permisiuni, validare, tranzactie, audit), ca in Erp.Host.</summary>
public abstract class FinanceTestBase(FinanceDatabase database)
{
    protected FinanceDatabase Database { get; } = database;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Permisiunile Finantelor date utilizatorului de test; testele le pot restrange.</summary>
    protected HashSet<string> Permissions { get; } = new(StringComparer.Ordinal)
    {
        FinancePermissions.TreasuryAccountView, FinancePermissions.TreasuryAccountManage,
    };

    protected async Task<T> Send<T>(IRequest<T> request, int? userId = null, int? companyId = null)
    {
        await using var provider = Services(userId, companyId);
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    protected async Task<T> Query<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    protected static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private ServiceProvider Services(int? userId, int? companyId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(FinanceApplication.Assembly);
        services.AddCoreInfrastructure(Database.ConnectionString, new LicenseOptions(Development: true));
        services.AddFinanceInfrastructure();
        services.AddSingleton<ILicenseInfo>(new FakeLicense());
        var context = new FakeContext { UserId = userId ?? Database.AdminId, CompanyId = companyId ?? Database.CompanyId };
        context.Permissions.UnionWith(Permissions);
        services.AddSingleton<ICurrentContext>(context);
        return services.BuildServiceProvider();
    }
}
