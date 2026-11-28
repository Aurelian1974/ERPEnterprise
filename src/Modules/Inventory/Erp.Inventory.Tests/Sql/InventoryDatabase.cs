using Dapper;
using Erp.Core.Application;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Inventory.Application;
using Erp.Inventory.Infrastructure;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>Containerul SQL comun (Erp.Testing.Sql), ca fixture de colectie al acestui assembly.</summary>
public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "InventorySqlServer";
}

/// <summary>
/// Baza ERP_IT_INV_*, creata ca la client (bootstrap, migratiile, programabilitatea si seed-ul tuturor modulelor), cu datele demo ale
/// machetelor Stocuri: Exemplu Distributie SRL cu locatiile Depozit central Ploiesti, Depozit Cluj si Magazin Cluj, seful de depozit
/// Gheorghe Ilie, partenerul Logistic Alfa (bunuri la terti) si a doua firma a grupului.
/// </summary>
public sealed class InventoryDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int KeeperUserId { get; private set; }

    public int CompanyId { get; private set; }

    public int OtherCompanyId { get; private set; }

    public int PloiestiId { get; private set; }

    public int ClujDepotId { get; private set; }

    public int ClujStoreId { get; private set; }

    public int OtherCompanyLocationId { get; private set; }

    public int InactiveLocationId { get; private set; }

    public int LogisticPartnerId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_INV_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        CompanyId = await NewCompany(connection, "EXDIST", "Exemplu Distribuție SRL", "18547290");
        OtherCompanyId = await NewCompany(connection, "EXPROD", "Exemplu Producție SRL", "14399840");
        AdminId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, IsEmployee, CreatedBy)
            VALUES (NEWID(), N'administrator', N'Administrator', 1, 1, 0, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        KeeperUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Employee (CompanyId, EmployeeNo, FirstName, LastName, IsActive, CreatedBy)
            VALUES (@CompanyId, N'gheorghe.ilie', N'Gheorghe', N'Ilie', 1, 1);
            DECLARE @EmployeeId int = SCOPE_IDENTITY();
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, IsEmployee, EmployeeId, CreatedBy)
            VALUES (NEWID(), N'gheorghe.ilie', N'Gheorghe Ilie', 1, 0, 1, @EmployeeId, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId });
        PloiestiId = await NewLocation(connection, CompanyId, "DC-PL", "Depozit central Ploiești", 2, true);
        ClujDepotId = await NewLocation(connection, CompanyId, "DEP-CJ", "Depozit Cluj", 2, true);
        ClujStoreId = await NewLocation(connection, CompanyId, "MAG-CJ", "Magazin Cluj", 3, true);
        InactiveLocationId = await NewLocation(connection, CompanyId, "VECHI", "Depozit închis", 2, false);
        OtherCompanyLocationId = await NewLocation(connection, OtherCompanyId, "FAB-BV", "Fabrica Brașov", 4, true);
        // Seriile documentelor de stoc pe anul curent (si pe anul trecut, pentru documentele datate in urma), in ambele firme.
        foreach (var company in new[] { CompanyId, OtherCompanyId })
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, Padding, IsFiscal, IsDefault, FirstNumber, CreatedBy)
                SELECT @CompanyId, NULL, t.Id, CONCAT(N'INV', t.Id), y.[Year], 1, 6, 0, 1, 1, 1
                FROM core.DocumentType AS t
                CROSS JOIN (VALUES (YEAR(SYSDATETIME())), (YEAR(SYSDATETIME()) - 1)) AS y ([Year])
                WHERE t.ModuleCode = N'Inventory';
                """, new { CompanyId = company });
        }

        LogisticPartnerId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode,
                IsActive, CreatedBy)
            VALUES (N'LA-LOG', N'Logistic Alfa SRL', 1, N'14399840', 0, 1, 1, N'Str. Depozitelor 3', N'Ploiești', N'PH', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Task<int> NewCompany(SqlConnection connection, string code, string name, string fiscalCode) =>
        connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
            SELECT @Code, @Name, @FiscalCode, 1, 0, N'J40/1234/2015', N'Str. Exemplu 1', N'București', N'B', 'RO', Id, 1, 1, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = code, Name = name, FiscalCode = fiscalCode });

    private static Task<int> NewLocation(SqlConnection connection, int companyId, string code, string name, byte type, bool active) =>
        connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Location (CompanyId, Code, Name, LocationType, IsActive, CreatedBy)
            VALUES (@CompanyId, @Code, @Name, @Type, @Active, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId = companyId, Code = code, Name = name, Type = type, Active = active });
}

/// <summary>Trimiterea cererilor prin pipeline-ul comun (licenta, permisiuni, validare, tranzactie, audit), ca in Erp.Host.</summary>
public abstract class InventoryTestBase(InventoryDatabase database)
{
    protected InventoryDatabase Database { get; } = database;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Permisiunile Stocurilor date utilizatorului de test; testele le pot restrange.</summary>
    protected HashSet<string> Permissions { get; } = new(StringComparer.Ordinal)
    {
        InventoryPermissions.WarehouseView, InventoryPermissions.WarehouseManage, InventoryPermissions.WarehouseBlock,
        InventoryPermissions.WarehouseKeeper, InventoryPermissions.SettingsManage, InventoryPermissions.ItemSettingsManage,
        InventoryPermissions.ValuationMethodChange, InventoryPermissions.MovementBackdate, InventoryPermissions.DocumentView,
        InventoryPermissions.TransferEdit, InventoryPermissions.TransferValidate, InventoryPermissions.LoadUnloadEdit, InventoryPermissions.LoadUnloadValidate,
        InventoryPermissions.ConsumptionEdit, InventoryPermissions.ConsumptionValidate, InventoryPermissions.ProductionNoteEdit,
        InventoryPermissions.ProductionNoteValidate, InventoryPermissions.CustodyEdit, InventoryPermissions.CustodyValidate, InventoryPermissions.DocumentReverse,
        InventoryPermissions.ReasonManage, InventoryPermissions.CostView, InventoryPermissions.LandedCostEdit,
        InventoryPermissions.LandedCostValidate, InventoryPermissions.RetailPriceEdit, InventoryPermissions.RetailPriceValidate,
        InventoryPermissions.RetailDischargeValidate, InventoryPermissions.LotView, InventoryPermissions.LotEdit, InventoryPermissions.LotRelease,
        InventoryPermissions.LotBlock,
    };

    /// <summary>Scope-ul pe gestiuni al utilizatorului de test (null = toate).</summary>
    protected HashSet<int>? WarehouseScope { get; set; }

    protected HashSet<DateOnly> ClosedDays { get; } = [];

    protected async Task<T> Send<T>(IRequest<T> request, int? userId = null, int? companyId = null)
    {
        await using var provider = Services(userId, companyId);
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    /// <summary>Ruleaza un serviciu intern (consumatori) in tranzactia proprie, ca Worker-ul.</summary>
    protected async Task InTransaction(Func<IServiceProvider, Task> work, int? companyId = null)
    {
        await using var provider = Services(null, companyId);
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<Erp.Core.Application.Abstractions.IUnitOfWork>();
        await unit.BeginAsync(Ct);
        await work(scope.ServiceProvider);
        await unit.CommitAsync(Ct);
    }

    /// <summary>Un serviciu intern intr-o tranzactie proprie, cu rezultat (portul de stoc, ca documentele apelantului).</summary>
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

    protected async Task Execute(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>Un model (stocabil sau nu) cu variantele date, ca in Nucleu (core.Item, core.ItemVariant).</summary>
    protected async Task<(int ItemId, int[] VariantIds)> NewItem(string code, bool stockable, params string[] variants)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        var itemId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Item (Code, Name, ItemType, UomSetId, BaseUomId, DefaultVatRateId, IsStockable, CreatedBy)
            SELECT TOP (1) @Code, N'Articol ' + @Code, CASE WHEN @Stockable = 1 THEN 1 ELSE 6 END, l.UomSetId, l.UomId,
                (SELECT TOP (1) Id FROM core.VatRate ORDER BY Id), @Stockable, 1
            FROM core.UomSetLine AS l WHERE l.IsBase = 1 ORDER BY l.UomSetId;
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = code, Stockable = stockable });
        var ids = new List<int>();
        foreach (var variant in variants)
        {
            ids.Add(await connection.ExecuteScalarAsync<int>("""
                INSERT INTO core.ItemVariant (ItemId, Code, Name, CreatedBy) VALUES (@ItemId, @Code, @Code, 1);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, new { ItemId = itemId, Code = code + "-" + variant }));
        }

        return (itemId, ids.ToArray());
    }

    /// <summary>Un angajat al firmei (gestionar).</summary>
    protected Task<int> NewEmployee(string firstName, string lastName, int? companyId = null) =>
        Query<int>("""
            INSERT INTO core.Employee (CompanyId, EmployeeNo, FirstName, LastName, IsActive, CreatedBy)
            VALUES (@CompanyId, @No, @FirstName, @LastName, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId = companyId ?? Database.CompanyId, No = Unique("E"), FirstName = firstName, LastName = lastName });

    /// <summary>Un serviciu al modulului rezolvat ca in cererea curenta (firma si utilizatorul testului).</summary>
    protected async Task<T> WithService<TService, T>(Func<TService, Task<T>> work, int? companyId = null) where TService : notnull
    {
        await using var provider = Services(null, companyId);
        await using var scope = provider.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private ServiceProvider Services(int? userId, int? companyId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(InventoryApplication.Assembly);
        services.AddCoreInfrastructure(Database.ConnectionString, new LicenseOptions(Development: true));
        services.AddInventoryInfrastructure();
        // C2 si C3 (Finantele): contarea evenimentelor Stocurilor, lista de inchidere si concordanta (S3 3.5, 3.6).
        Erp.Finance.Infrastructure.FinanceInfrastructureRegistration.AddFinanceInfrastructure(services, Database.ConnectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILicenseInfo>(new FakeLicense());
        services.AddSingleton<IPeriodLock>(new FakePeriodLock(ClosedDays));
        var context = new FakeContext { UserId = userId ?? Database.AdminId, CompanyId = companyId ?? Database.CompanyId, WarehouseScope = WarehouseScope };
        context.Permissions.UnionWith(Permissions);
        services.AddSingleton<ICurrentContext>(context);
        return services.BuildServiceProvider();
    }
}
