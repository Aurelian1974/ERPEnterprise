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

    /// <summary>Un client persoana juridica (plafoanele PJ).</summary>
    public int LegalPartnerId { get; private set; }

    /// <summary>Firma testelor de plafon, cu plafoanele proprii: IncasarePJ 2.000 lei din 15.11.2026, IncasarePF 100 lei avertizare din 20.11.2026.</summary>
    public int LimitCompanyId { get; private set; }

    public int LimitBranchId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        // O baza per clasa de teste (fixture de clasa), ca testele claselor sa nu se vada intre ele.
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_FIN_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        CompanyId = await NewCompany(connection, "EXDIST", "Exemplu Distribuție SRL", "18547290");
        AdminId = await NewUser(connection, "administrator", "Administrator", "ADMIN", null);
        CashierId = await NewUser(connection, "elena.dumitru", "Elena Dumitru", "CASIER", CompanyId);
        AccountantId = await NewUser(connection, "ion.popescu", "Ion Popescu", "CONTABIL_SEF", CompanyId);
        OtherCompanyId = await NewCompany(connection, "EXPROD", "Exemplu Producție SRL", "14399840");
        HeadOfficeId = await NewBranch(connection, CompanyId, "SEDIU", "Sediu București", "București", "B");
        ClujId = await NewBranch(connection, CompanyId, "PL-CLUJ", "PL Cluj", "Cluj-Napoca", "CJ");
        LimitCompanyId = await NewCompany(connection, "EXLIM", "Exemplu Plafoane SRL", "33445566");
        LimitBranchId = await NewBranch(connection, LimitCompanyId, "SEDIU", "Sediu Iași", "Iași", "IS");
        foreach (var company in new[] { CompanyId, LimitCompanyId })
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, Padding, IsFiscal, IsDefault, FirstNumber,
                    CreatedBy)
                SELECT @CompanyId, NULL, t.Id, v.Prefix, 2026, 1, 6, v.IsFiscal, 1, 1, 1
                FROM (VALUES (N'Finance.Receipt', N'CH', 1), (N'Finance.CashIn', N'DI', 0), (N'Finance.Payment', N'DP', 0), (N'Finance.ExpenseReport', N'DC', 0), (N'Finance.PaymentOrder', N'OP', 0), (N'Finance.Compensation', N'PVC', 0), (N'Finance.BalanceConfirmation', N'CS', 0), (N'Finance.PenaltyCalculation', N'PEN', 0)) AS v (Code, Prefix, IsFiscal)
                JOIN core.DocumentType AS t ON t.Code = v.Code;
                """, new { CompanyId = company });
        }

        await connection.ExecuteAsync("""
            INSERT INTO fin.CashLimitRule (CompanyId, Category, CurrencyCode, Amount, Action, ValidFrom, CreatedBy)
            VALUES (@CompanyId, 'IncasarePJ', 'RON', 2000, 1, '20261115', 1), (@CompanyId, 'IncasarePF', 'RON', 100, 2, '20261120', 1);
            """, new { CompanyId = LimitCompanyId });
        LegalPartnerId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode,
                IsActive, CreatedBy)
            VALUES (N'AGRONORD', N'Agro Nord Distribuție SRL', 1, N'14399840', 1, 0, 1, N'Str. Gării 21', N'Suceava', N'SV', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
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
            DECLARE @Id int = SCOPE_IDENTITY();
            -- Datele firmei pentru documente (02.01, core.CompanyVersion), ca la salvarea firmei din ecran.
            INSERT INTO core.CompanyVersion (CompanyId, ValidFrom, Name, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                ModifiedAt, ModifiedBy)
            VALUES (@Id, '2020-01-01', @Name, 1, 0, N'J40/1234/2015', N'Str. Exemplu 1', N'București', N'B', 'RO', SYSUTCDATETIME(), 1);
            SELECT @Id;
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

    /// <summary>Ruleaza un serviciu intern (ISettlementService, consumatori) in tranzactia proprie, ca apelantul lui real (F1, F2, Worker).</summary>
    protected async Task<T> InTransaction<T>(Func<IServiceProvider, Task<T>> work, int? companyId = null)
    {
        await using var provider = Services(null, companyId);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<Erp.Core.Application.Abstractions.IUnitOfWork>();
        await unit.BeginAsync(Ct);
        var result = await work(scope.ServiceProvider);
        await unit.CommitAsync(Ct);
        return result;
    }

    protected async Task<T> Query<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    /// <summary>Zilele din perioade contabile inchise (CORE_PERIOD_CLOSED), pentru testele pe pipeline.</summary>
    protected HashSet<DateOnly> ClosedDays { get; } = [];

    protected static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private ServiceProvider Services(int? userId, int? companyId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(FinanceApplication.Assembly);
        services.AddCoreInfrastructure(Database.ConnectionString, new LicenseOptions(Development: true));
        services.AddFinanceInfrastructure(Database.ConnectionString);
        services.AddSingleton<Application.Treasury.IPersonIdProtector>(new Api.PersonIdProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        services.AddSingleton<ILicenseInfo>(new FakeLicense());
        services.AddSingleton<IPeriodLock>(new FakePeriodLock(ClosedDays));
        var context = new FakeContext { UserId = userId ?? Database.AdminId, CompanyId = companyId ?? Database.CompanyId };
        context.Permissions.UnionWith(Permissions);
        services.AddSingleton<ICurrentContext>(context);
        return services.BuildServiceProvider();
    }
}
