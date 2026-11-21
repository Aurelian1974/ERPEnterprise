using Dapper;
using Erp.Core.Application;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Data;
using Erp.Core.Infrastructure.Licensing;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Baza ERP_IT_ORG: administratorul la nivel de instalare si un utilizator STANDARD, cu rol doar pe firma creata in test.</summary>
public sealed class OrgDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int StandardId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_ORG", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        var ids = (await connection.QueryAsync<int>("""
            DECLARE @Ids TABLE (Id int, UserName nvarchar(50));
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            OUTPUT inserted.Id, inserted.UserName INTO @Ids
            VALUES (NEWID(), N'admin.org', N'Admin Org', 1, 1, 1), (NEWID(), N'standard.org', N'Standard Org', 1, 1, 1);
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT i.Id, r.Id, NULL, 1 FROM @Ids AS i JOIN core.Role AS r ON r.Code = N'ADMIN' WHERE i.UserName = N'admin.org';
            SELECT Id FROM @Ids ORDER BY UserName;
            """)).ToList();
        (AdminId, StandardId) = (ids[0], ids[1]);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Structura organizatorica si seriile pe SQL Server real (02.01, 3.1, 4.2): numerotare concurenta fara goluri (A2),
/// rollback fara gol (A3), coduri unice fara diferenta de majuscule (A21), concurenta optimista si accesul pe firma.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed partial class OrganizationIntegrationTests(OrgDatabase database) : IClassFixture<OrgDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (ISender Sender, IServiceScope Scope) Scope(int companyId, int? userId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(OrganizationIntegrationTests).Assembly);
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(Development: true));
        var context = new FakeContext { UserId = userId ?? database.AdminId, CompanyId = companyId };
        context.Permissions.UnionWith([CorePermissions.CompanyView, CorePermissions.CompanyManage, CorePermissions.NumberSeriesManage]);
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Erp.Core.Application.Me.ICurrentUserProfile>(context);
        services.AddScoped<HandlerCalls>();
        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<ISender>(), scope);
    }

    private async Task<T> Send<T>(IRequest<T> request, int companyId = 0)
    {
        var (sender, scope) = Scope(companyId);
        using (scope)
        {
            return await sender.Send(request, Ct);
        }
    }

    private async Task<int> PolicyId()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT Id FROM core.PrecisionPolicy WHERE Code = N'STANDARD';");
    }

    private static int nextFiscal = 100;

    /// <summary>Un CUI valid (cifra de control calculata), diferit la fiecare apel.</summary>
    private static string NewFiscalCode()
    {
        for (var body = Interlocked.Increment(ref nextFiscal) * 1000; ; body++)
        {
            for (var control = 0; control <= 9; control++)
            {
                var candidate = $"{body}{control}";
                if (Erp.Core.Domain.RomanianFiscalCode.IsValid(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    private async Task<SavedEntity> CreateCompanyAsync(string code, int? groupId = null) =>
        await Send(new SaveCompany(null, groupId, code, "Firma " + code, NewFiscalCode(), true, false, "J40/1234/2015", "200.00",
            "Str. Exemplului 10", "București", "B", "RO", "010101", await PolicyId(), true, true, null));

    private static string Code() => "C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    [Trait("Criteriu", "02.01-A21")]
    public async Task Company_codes_are_unique_without_regard_to_case_and_fiscal_codes_are_unique()
    {
        var code = Code();
        var company = await CreateCompanyAsync(code);

        var duplicateCode = await Assert.ThrowsAsync<ErpException>(() => CreateCompanyAsync(code.ToLowerInvariant()));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicateCode.Code);

        var detail = await Send(new GetCompany(company.Id));
        var duplicateFiscal = await Assert.ThrowsAsync<ErpException>(async () => await Send(new SaveCompany(null, null, Code(), "Alta", detail.FiscalCode,
            false, false, "J2024000123401", "10", "Str. 1", "Cluj-Napoca", "CJ", "RO", null, await PolicyId(), true, true, null)));
        Assert.Equal("CORE_COMPANY_DUPLICATE_FISCAL_CODE", duplicateFiscal.Code);
        Assert.Equal(("200.00", "RO"), (detail.ShareCapital, detail.CountryCode));
    }

    [Fact]
    public async Task Invalid_fiscal_code_iban_and_trade_register_are_field_errors()
    {
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(async () => await Send(new SaveCompany(null, null, Code(), "Firma", "RO12345679",
            true, false, "J40-1234-2015", "abc", "Str. 1", "București", "B", "RO", null, await PolicyId(), true, true, null)));
        Assert.Contains("fiscalCode", invalid.Errors.Keys);
        Assert.Contains("tradeRegisterNo", invalid.Errors.Keys);
        Assert.Contains("shareCapital", invalid.Errors.Keys);

        var company = await CreateCompanyAsync(Code());
        var iban = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveCompanyBankAccount(null, company.Id, "RO49AAAA1B31007593840001", "Banca", "RON", true, true, null), company.Id));
        Assert.Contains("iban", iban.Errors.Keys);
    }

    [Fact]
    public async Task Structure_rules_children_parents_and_concurrency()
    {
        var group = await Send(new SaveCompanyGroup(null, Code(), "Grup", null, null, true, null));
        var company = await CreateCompanyAsync(Code(), group.Id);
        var other = await CreateCompanyAsync(Code());
        var branch = await Send(new SaveBranch(null, company.Id, "PL-CJ", "Punct de lucru Cluj", "Str. 2", "Cluj-Napoca", "CJ", "RO", null, true, null), company.Id);
        await Send(new SaveLocation(null, company.Id, branch.Id, "DEP-CJ", "Depozit Cluj", 2, null, null, null, null, null, true, null), company.Id);

        // Locatia nu poate fi sub punctul de lucru al altei firme.
        var wrongParent = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveLocation(null, other.Id, branch.Id, "L1", "Locație", 1, null, null, null, null, null, true, null), other.Id));
        Assert.Equal("CORE_INVALID_REFERENCE", wrongParent.Code);

        // Codul punctului de lucru e unic in firma, fara diferenta de majuscule.
        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveBranch(null, company.Id, "pl-cj", "Alt punct", "Str. 3", "Cluj-Napoca", "CJ", "RO", null, true, null), company.Id));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        // Un nod cu copii activi nu se dezactiveaza.
        var withChildren = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveBranch(branch.Id, company.Id, "PL-CJ", "Punct de lucru Cluj", "Str. 2", "Cluj-Napoca", "CJ", "RO", null, false, branch.RowVersion), company.Id));
        Assert.Equal("CORE_NODE_HAS_ACTIVE_CHILDREN", withChildren.Code);

        // Grupul nu poate fi mutat sub el insusi; firma-mama trebuie sa fie in grup.
        var cycle = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveCompanyGroup(group.Id, "G", "Grup", group.Id, null, true, group.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", cycle.Code);
        var foreignParent = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveCompanyGroup(group.Id, "G", "Grup", null, other.Id, true, group.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", foreignParent.Code);

        // RowVersion: a doua salvare cu versiunea veche e refuzata.
        var renamed = await Send(new SaveBranch(branch.Id, company.Id, "PL-CJ", "Cluj", "Str. 2", "Cluj-Napoca", "CJ", "RO", null, true, branch.RowVersion), company.Id);
        Assert.NotEqual(branch.RowVersion, renamed.RowVersion);
        var stale = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveBranch(branch.Id, company.Id, "PL-CJ", "Cluj 2", "Str. 2", "Cluj-Napoca", "CJ", "RO", null, true, branch.RowVersion), company.Id));
        Assert.Equal(CoreErrorCodes.ConcurrencyConflict, stale.Code);
    }

    [Fact]
    public async Task Company_work_is_limited_to_the_current_company_and_new_companies_to_the_installation()
    {
        var company = await CreateCompanyAsync(Code());
        var other = await CreateCompanyAsync(Code());

        var foreign = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveBranch(null, other.Id, "B1", "Punct", "Str. 1", "Iași", "IS", "RO", null, true, null), company.Id));
        Assert.Equal(CoreErrorCodes.Forbidden, foreign.Code);

        var newCompany = await Assert.ThrowsAsync<ErpException>(async () => await Send(new SaveCompany(null, null, Code(), "Nouă", NewFiscalCode(), false, false,
            "J40/1/2020", "1", "Str. 1", "Iași", "IS", "RO", null, await PolicyId(), true, true, null), company.Id));
        Assert.Equal(CoreErrorCodes.Forbidden, newCompany.Code);
    }

    [Fact]
    public async Task Tree_and_company_list_show_only_companies_where_the_user_has_a_role()
    {
        var visible = await CreateCompanyAsync(Code());
        var hidden = await CreateCompanyAsync(Code());
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
                SELECT @UserId, Id, @CompanyId, 1 FROM core.Role WHERE Code = N'STANDARD';
                """, new { UserId = database.StandardId, CompanyId = visible.Id });
        }

        await using var session = new DbSession(database.ConnectionString);
        var store = new Infrastructure.Organization.OrganizationStore(session);
        var tree = await store.GetTreeAsync(database.StandardId, Ct);
        var companies = await store.GetUserCompaniesAsync(database.StandardId, Ct);

        Assert.Equal([visible.Id], tree.Companies.Select(c => c.Id));
        Assert.Equal([visible.Id], companies.Select(c => c.Id));
        Assert.Contains(hidden.Id, (await store.GetTreeAsync(database.AdminId, Ct)).Companies.Select(c => c.Id));
    }

    [Fact]
    public async Task Only_one_default_bank_account_per_currency()
    {
        var company = await CreateCompanyAsync(Code());
        var first = await Send(new SaveCompanyBankAccount(null, company.Id, "RO49 AAAA 1B31 0075 9384 0000", "Banca A", "RON", true, true, null), company.Id);
        await Send(new SaveCompanyBankAccount(null, company.Id, "RO09BCYP0000001234567890", "Banca B", "RON", true, true, null), company.Id);

        var detail = await Send(new GetCompany(company.Id), company.Id);
        Assert.Single(detail.BankAccounts, a => a.IsDefault);
        Assert.False(detail.BankAccounts.Single(a => a.Id == first.Id).IsDefault);
        Assert.Equal("RO49AAAA1B31007593840000", detail.BankAccounts.Single(a => a.Id == first.Id).Iban);

        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveCompanyBankAccount(null, company.Id, "RO49AAAA1B31007593840000", "Banca C", "RON", false, true, null), company.Id));
        Assert.Equal("CORE_BANK_ACCOUNT_DUPLICATE_IBAN", duplicate.Code);
    }

    [Fact]
    public async Task Company_has_several_typed_addresses_with_one_default_per_type()
    {
        var company = await CreateCompanyAsync(Code());
        var first = await Send(new SaveCompanyAddress(null, company.Id, 1, "Depozit", "Str. 1", "Cluj-Napoca", "CJ", "RO", null, true, true, null), company.Id);
        await Send(new SaveCompanyAddress(null, company.Id, 1, null, "Str. 2", "Iași", "IS", "RO", null, true, true, null), company.Id);
        await Send(new SaveCompanyAddress(null, company.Id, 2, null, "Str. 3", "Brașov", "BV", "RO", null, true, true, null), company.Id);

        var detail = await Send(new GetCompany(company.Id), company.Id);
        Assert.Equal(3, detail.Addresses.Count);
        Assert.Equal(2, detail.Addresses.Count(a => a.IsDefault));
        Assert.False(detail.Addresses.Single(a => a.Id == first.Id).IsDefault);
        Assert.False(string.IsNullOrEmpty(detail.Address));

        var stale = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveCompanyAddress(first.Id, company.Id, 1, "Depozit", "Str. 1", "Cluj-Napoca", "CJ", "RO", null, false, true, [1, 2, 3, 4, 5, 6, 7, 8]), company.Id));
        Assert.Equal(ErpErrorKind.Conflict, stale.Kind);

        var other = await CreateCompanyAsync(Code());
        await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveCompanyAddress(first.Id, other.Id, 1, "Depozit", "Str. 1", "Cluj-Napoca", "CJ", "RO", null, false, true, first.RowVersion), other.Id));
    }

    [Fact]
    public async Task Address_types_are_configurable_and_an_inactive_type_cannot_be_chosen()
    {
        var company = await CreateCompanyAsync(Code());
        var code = $"TIP_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var type = await Send(new SaveAddressType(null, code, "Punct de ridicare", 50, true, null), company.Id);
        await Assert.ThrowsAsync<ErpException>(() => Send(new SaveAddressType(null, code, "Duplicat", 60, true, null), company.Id));

        await Send(new SaveCompanyAddress(null, company.Id, type.Id, null, "Str. 1", "Cluj-Napoca", "CJ", "RO", null, false, true, null), company.Id);

        var inactive = await Send(new SaveAddressType(type.Id, code, "Punct de ridicare", 50, false, type.RowVersion), company.Id);
        await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveCompanyAddress(null, company.Id, type.Id, null, "Str. 2", "Iași", "IS", "RO", null, false, true, null), company.Id));
        Assert.NotEmpty(inactive.RowVersion);

        var system = (await Send(new GetOrgLookups(), company.Id)).AddressTypes.First(t => t.IsSystem);
        var protectedType = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveAddressType(system.Id, system.Code, system.Name, system.SortOrder, false, system.RowVersion), company.Id));
        Assert.Equal("CORE_ADDRESS_TYPE_PROTECTED", protectedType.Code);
    }

    private async Task<(int CompanyId, SavedEntity Series)> SeriesAsync(string documentType = "Sales.Invoice", short year = 2026, bool autoRollover = false)
    {
        var company = await CreateCompanyAsync(Code());
        var series = await Send(new SaveNumberSeries(null, company.Id, null, documentType, "EXD", year, 1, 6, true, true, autoRollover, null), company.Id);
        return (company.Id, series);
    }

    private async Task<DocumentNumber> NextInTransactionAsync(int companyId, DateOnly date, bool commit = true, string documentType = "Sales.Invoice",
        NumberSeriesChoice? choice = null)
    {
        await using var session = new DbSession(database.ConnectionString);
        var unitOfWork = (Application.Abstractions.IUnitOfWork)session;
        await unitOfWork.BeginAsync(Ct);
        var number = await new Infrastructure.Organization.NumberSeriesService(session).NextAsync(companyId, documentType, date, Ct, choice: choice);
        if (commit)
        {
            await unitOfWork.CommitAsync(Ct);
        }
        else
        {
            await unitOfWork.RollbackAsync(Ct);
        }

        return number;
    }

    [Fact]
    [Trait("Criteriu", "02.01-A2")]
    public async Task Two_users_validating_1000_documents_on_the_same_fiscal_series_get_unique_consecutive_numbers()
    {
        var (companyId, _) = await SeriesAsync();
        var date = new DateOnly(2026, 11, 15);

        async Task<List<long>> UserAsync()
        {
            var numbers = new List<long>();
            for (var i = 0; i < 500; i++)
            {
                numbers.Add((await NextInTransactionAsync(companyId, date)).Number);
            }

            return numbers;
        }

        var results = await Task.WhenAll(Task.Run(UserAsync, Ct), Task.Run(UserAsync, Ct));
        var all = results.SelectMany(n => n).Order().ToList();

        Assert.Equal(Enumerable.Range(1, 1000).Select(n => (long)n), all);
        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(1001, await connection.ExecuteScalarAsync<long>("SELECT NextNumber FROM core.NumberSeries WHERE CompanyId = @companyId;", new { companyId }));
    }

    [Fact]
    [Trait("Criteriu", "02.01-A3")]
    public async Task Rollback_after_allocating_a_number_leaves_no_gap()
    {
        var (companyId, _) = await SeriesAsync();
        var date = new DateOnly(2026, 3, 1);

        var first = await NextInTransactionAsync(companyId, date);
        var rolledBack = await NextInTransactionAsync(companyId, date, commit: false);
        var next = await NextInTransactionAsync(companyId, date);

        Assert.Equal(("EXD-2026-000001", 2L, 2L), (first.Formatted, rolledBack.Number, next.Number));
    }

    [Fact]
    public async Task New_year_creates_the_series_only_with_auto_rollover_otherwise_reports_missing_series()
    {
        var (withRollover, _) = await SeriesAsync(autoRollover: true);
        var (without, _) = await SeriesAsync();

        var january = await NextInTransactionAsync(withRollover, new DateOnly(2027, 1, 2));
        var missing = await Assert.ThrowsAsync<SqlException>(() => NextInTransactionAsync(without, new DateOnly(2027, 1, 2)));

        Assert.Equal("EXD-2027-000001", january.Formatted);
        Assert.Equal(50001, missing.Number);
    }

    [Fact]
    public async Task Used_fiscal_series_cannot_change_its_numbering_and_closed_series_are_frozen()
    {
        var (companyId, series) = await SeriesAsync();
        await NextInTransactionAsync(companyId, new DateOnly(2026, 5, 5));
        var current = (await Send(new ListNumberSeries(companyId, 2026), companyId)).Single();

        var locked = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveNumberSeries(series.Id, companyId, null, "Sales.Invoice", "EXX", 2026, current.NextNumber, 6, true, true, false, current.RowVersion), companyId));
        Assert.Equal("CORE_NUMBER_SERIES_LOCKED", locked.Code);

        // Setarile de utilizare se pot schimba si dupa folosire.
        var saved = await Send(new SaveNumberSeries(series.Id, companyId, null, "Sales.Invoice", "EXD", 2026, current.NextNumber, 6, true, true, true,
            current.RowVersion), companyId);
        var closed = await Send(new CloseNumberSeries(series.Id, companyId, saved.RowVersion), companyId);
        var frozen = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveNumberSeries(series.Id, companyId, null, "Sales.Invoice", "EXD", 2026, current.NextNumber, 6, true, false, true, closed.RowVersion), companyId));
        Assert.Equal("CORE_NUMBER_SERIES_CLOSED", frozen.Code);

        var noSeries = await Assert.ThrowsAsync<SqlException>(() => NextInTransactionAsync(companyId, new DateOnly(2026, 6, 1)));
        Assert.Equal(50001, noSeries.Number);
    }
}
