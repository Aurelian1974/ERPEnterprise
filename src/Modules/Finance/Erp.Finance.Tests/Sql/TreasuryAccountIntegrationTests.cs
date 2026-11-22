using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Conturile de trezorerie pe SQL Server real (02.02, F1 3.1, 3.2, 8; macheta f1-01), pe baza case sensitive creata pe un server case
/// insensitive (F1-A16): cod unic pe firma, referinte ale firmei, casieri, concurenta, catalogul livrat.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TreasuryAccountIntegrationTests(FinanceDatabase database) : FinanceTestBase(database), IClassFixture<FinanceDatabase>
{
    private SaveTreasuryAccount Cash(string code, int? branchId = null, IReadOnlyList<TreasuryCashierInput>? cashiers = null) =>
        new(null, TreasuryAccountKind.Cash, code, "Casa " + code, branchId ?? Database.HeadOfficeId, "RON", null, null, Database.CashierId, null, null,
            null, "40000", CashCloseMode.AutomaticAtMidnight, new DateOnly(2026, 1, 1), true, cashiers, null);

    [Fact]
    [Trait("Criteriu", "02.02-F1-A16")]
    public async Task A_cash_account_gets_the_proposed_gl_account_its_cashiers_and_a_company_unique_code()
    {
        var code = Unique("CASA-");
        var saved = await Send(Cash(code, cashiers: [new TreasuryCashierInput(Database.CashierId, true, null, null)]));

        var detail = await Send(new GetTreasuryAccount(saved.Id));
        Assert.Equal(("5311", CashCloseMode.AutomaticAtMidnight, "40000.00", Database.CashierId), (detail.GlAccountCode, detail.CloseMode,
            detail.MaxClosingBalance, detail.ResponsibleUserId));
        Assert.Equal("Elena Dumitru", Assert.Single(detail.Cashiers).DisplayName);

        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(Cash(code.ToLowerInvariant())));
        Assert.Equal("FIN_DUPLICATE_CODE", duplicate.Code);
        // Acelasi cod in alta firma e permis (codul e unic pe firma).
        var other = await Send(Cash(code) with { BranchId = null, Kind = TreasuryAccountKind.Transit }, companyId: Database.OtherCompanyId);
        Assert.True(other.Id > 0);

        var page = await Send(new GetTreasuryAccounts(code));
        var row = Assert.Single(page.Accounts);
        Assert.Equal((TreasuryAccountKind.Cash, "Sediu București", 1), (row.Kind, row.BranchName, row.CashierCount));
        Assert.Contains(page.Lookups.Branches, b => b.Id == Database.ClujId);
        Assert.Contains(page.Lookups.BankAccounts, b => b.Id == Database.BcrRonId && b.CurrencyCode == "RON");
        Assert.Contains("RON", page.Lookups.Currencies);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.1")]
    public async Task Bank_accounts_need_the_companys_bank_account_in_the_same_currency()
    {
        var withoutIban = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Bank,
            Unique("BCR-"), "BCR lei", null, "RON", null, null, null, null, null, null, null, CashCloseMode.OnDemand, null, true, null, null)));
        Assert.Contains("companyBankAccountId", withoutIban.Errors.Keys);

        var wrongCurrency = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Bank, Unique("BCR-"),
            "BCR euro", null, "EUR", Database.BcrRonId, null, null, null, null, null, null, CashCloseMode.OnDemand, null, true, null, null)));
        Assert.Equal("FIN_INVALID_REFERENCE", wrongCurrency.Code);

        var bank = await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Bank, Unique("BCR-"), "BCR lei", null, "RON", Database.BcrRonId,
            null, Database.CashierId, null, null, null, "1", CashCloseMode.AutomaticAtMidnight, null, true,
            [new TreasuryCashierInput(Database.CashierId, true, null, null)], null));
        var detail = await Send(new GetTreasuryAccount(bank.Id));
        // Campurile de casa nu se pastreaza pe banca.
        Assert.Equal(("5121", (int?)null, (string?)null, CashCloseMode.OnDemand, 0), (detail.GlAccountCode, detail.ResponsibleUserId,
            detail.MaxClosingBalance, detail.CloseMode, detail.Cashiers.Count));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.1")]
    public async Task The_cashier_list_replaces_the_assignments_and_a_stale_version_is_refused()
    {
        var saved = await Send(Cash(Unique("CASA-"), Database.ClujId, [new TreasuryCashierInput(Database.CashierId, true, null, null)]));
        var detail = await Send(new GetTreasuryAccount(saved.Id));

        var updated = await Send(Cash(detail.Code, Database.ClujId, [new TreasuryCashierInput(Database.AccountantId, false, new DateOnly(2026, 11, 1), null)])
            with { Id = saved.Id, Name = "Casa PL Cluj", RowVersion = detail.RowVersion });
        var after = await Send(new GetTreasuryAccount(saved.Id));
        Assert.Equal("Casa PL Cluj", after.Name);
        var cashier = Assert.Single(after.Cashiers);
        Assert.Equal((Database.AccountantId, false, new DateOnly(2026, 11, 1)), (cashier.UserId, cashier.CanValidate, cashier.ValidFrom));

        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(Cash(detail.Code, Database.ClujId) with { Id = saved.Id, RowVersion = detail.RowVersion }));
        Assert.Equal("FIN_CONCURRENCY_CONFLICT", stale.Code);
        var kindChange = await Assert.ThrowsAsync<ErpException>(() => Send(Cash(detail.Code, Database.ClujId)
            with { Id = saved.Id, Kind = TreasuryAccountKind.Transit, RowVersion = updated.RowVersion }));
        Assert.Equal("FIN_INVALID_REFERENCE", kindChange.Code);

        // Contul altei firme nu se vede si nu se modifica din firma curenta.
        var notFound = await Assert.ThrowsAsync<ErpException>(() => Send(new GetTreasuryAccount(saved.Id), companyId: Database.OtherCompanyId));
        Assert.Equal(ErpErrorKind.NotFound, notFound.Kind);
        var foreignTransit = await Assert.ThrowsAsync<ErpException>(() => Send(Cash(detail.Code) with { Id = saved.Id, BranchId = null,
            Kind = TreasuryAccountKind.Transit, RowVersion = updated.RowVersion }, companyId: Database.OtherCompanyId));
        Assert.Equal("FIN_TREASURY_ACCOUNT_NOT_FOUND", foreignTransit.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-8")]
    public async Task Saving_requires_the_manage_permission_and_is_audited()
    {
        Permissions.Remove(Application.FinancePermissions.TreasuryAccountManage);
        var forbidden = await Assert.ThrowsAsync<ErpException>(() => Send(Cash(Unique("CASA-"))));
        Assert.Equal(ErpErrorKind.Forbidden, forbidden.Kind);

        Permissions.Add(Application.FinancePermissions.TreasuryAccountManage);
        var saved = await Send(Cash(Unique("CASA-")));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Fin.TreasuryAccount.Save' AND EntityType = N'Fin.TreasuryAccount' AND EntityId = @Id
            """, new { saved.Id }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.2")]
    public async Task The_shipped_catalog_has_the_cash_operation_types_permissions_and_roles()
    {
        Assert.Equal(13, await Query<int>("SELECT COUNT(*) FROM fin.TreasuryOperationType WHERE IsSystem = 1 AND AppliesToCash = 1"));
        Assert.Equal(2, await Query<byte>("SELECT Direction FROM fin.TreasuryOperationType WHERE Code = N'PLATA-AVANS-DECONTARE'"));
        Assert.Equal(15, await Query<int>("SELECT COUNT(*) FROM core.Permission WHERE ModuleCode = N'Fin'"));
        Assert.Equal(4, await Query<int>("""
            SELECT COUNT(*) FROM core.RolePermission AS rp JOIN core.Role AS r ON r.Id = rp.RoleId JOIN core.Permission AS p ON p.Id = rp.PermissionId
            WHERE r.Code = N'CASIER' AND p.ModuleCode = N'Fin'
            """));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.DocumentType WHERE Code = N'Finance.CashIn'"));
    }
}
