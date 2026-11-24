using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Accounts;
using Erp.Finance.Contracts.Treasury;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Planul de conturi romanesc (02.02, C1 3.1–3.3, 4.1): planul livrat, analiticele, frunzele, rolurile si validarea codurilor din F1.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ChartOfAccountsIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    public ChartOfAccountsIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.AccountManage, FinancePermissions.TreasuryConfigure]);

    [Fact]
    [Trait("Criteriu", "02.02-C1-A1")]
    public async Task The_installation_has_exactly_one_complete_statutory_chart_with_types_and_saft_codes()
    {
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.ChartOfAccounts WHERE Kind = 1"));
        var accounts = await Send(new GetGlAccounts());
        Assert.True(accounts.Count >= 640, $"Planul livrat are {accounts.Count} conturi.");
        Assert.Equal(9, accounts.Count(a => a.Level == 1));
        // Fiecare cont frunza din bilant si din conturile de rezultat (clasele 1–7) are tipul si contul SAF-T (analiticele il mostenesc).
        var leaves = accounts.Where(a => a.IsPostable && a.Code[0] is >= '1' and <= '7').ToList();
        Assert.All(leaves, a => Assert.True(a.AccountType is >= 1 and <= 3 && a.SaftAccountCode == a.Code.Split('.')[0], a.Code));
        Assert.Equal(((byte?)1, (byte?)2, (byte?)3), (accounts.Single(a => a.Code == "5121").AccountType, accounts.Single(a => a.Code == "401").AccountType,
            accounts.Single(a => a.Code == "121").AccountType));
        Assert.Equal((byte)1, accounts.Single(a => a.Code == "607").ClosingTarget);
        Assert.False(accounts.Single(a => a.Code == "4111").ManualPosting);
        Assert.True(accounts.Single(a => a.Code == "8011").IsOffBalance);
        Assert.DoesNotContain(accounts, a => a.Code == "5126");
        var roles = await Send(new GetGlAccountRoles());
        Assert.Equal("4427", roles.Single(r => r.Role == GlAccountRoles.VatCollected && r.CompanyId is null).AccountCode);
    }

    [Fact]
    [Trait("Criteriu", "02.02-C1-A2")]
    public async Task An_account_with_analytics_takes_no_postings_while_a_synthetic_without_analytics_does()
    {
        var parent = (await Send(new GetGlAccounts())).Single(a => a.Code == "5125");
        Assert.True(parent.IsPostable);
        Assert.True((await Validate("5125")).IsValid);

        var analytic = await Send(new SaveGlAccount(null, parent.Id, "5125.01", "Sume în curs - card BCR", true, 0, null, 0));
        Assert.False((await Send(new GetGlAccount(parent.Id))).IsPostable);
        var check = await Validate("5125");
        Assert.False(check.IsValid);
        Assert.Contains("analitice", check.Reason, StringComparison.Ordinal);
        Assert.True((await Validate("5125.01")).IsValid);
        // Analiticul restrans la firma nu e vazut de alta firma.
        Assert.False((await InTransaction(sp => sp.GetRequiredService<IGlAccountValidator>().ValidateAsync(Database.OtherCompanyId, "5125.01", Today, Ct),
            Database.OtherCompanyId)).IsValid);
        var role = await InTransaction(sp => sp.GetRequiredService<IChartOfAccountsQueries>().FindByRoleAsync(Database.CompanyId, GlAccountRoles.CashInTransit,
            null, null, Today, Ct));
        Assert.Equal("581", role?.Code);

        // Dezactivarea analiticului face parintele din nou frunza.
        var detail = await Send(new GetGlAccount(analytic.Id));
        await Send(new SetGlAccountActive(analytic.Id, false, detail.RowVersion));
        Assert.True((await Validate("5125")).IsValid);
    }

    [Fact]
    [Trait("Criteriu", "02.02-C1-A3")]
    public async Task Shipped_accounts_keep_code_and_name_codes_are_unique_and_parents_with_analytics_stay_active()
    {
        var accounts = await Send(new GetGlAccounts());
        var bank = await Send(new GetGlAccount(accounts.Single(a => a.Code == "5124").Id));
        var renamed = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveGlAccount(bank.Id, null, "5124", "Bancă valută", false, 0, null, 0,
            RowVersion: bank.RowVersion)));
        Assert.Equal("FIN_ACCOUNT_SYSTEM_READONLY", renamed.Code);
        var wrongParent = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveGlAccount(null, bank.Id, "5121.09", "Greșit", false, 0, null, 0)));
        Assert.Equal("FIN_ACCOUNT_PARENT_INVALID", wrongParent.Code);

        var euro = await Send(new SaveGlAccount(null, bank.Id, "5124.01", "BCR EUR", false, 2, "EUR", 0));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveGlAccount(null, bank.Id, "5124.01", "Dublură", false, 0, null, 0)));
        Assert.Equal("FIN_ACCOUNT_CODE_DUPLICATE", duplicate.Code);
        var bankVersion = (await Send(new GetGlAccount(bank.Id))).RowVersion;
        var withChildren = await Assert.ThrowsAsync<ErpException>(() => Send(new SetGlAccountActive(bank.Id, false, bankVersion)));
        Assert.Equal("FIN_ACCOUNT_HAS_ACTIVE_CHILDREN", withChildren.Code);

        // Redenumirea unui analitic pastreaza denumirea veche in istoric.
        var created = await Send(new GetGlAccount(euro.Id));
        await Send(new SaveGlAccount(euro.Id, null, "5124.01", "BCR EUR principal", false, 2, "EUR", 0, RowVersion: created.RowVersion));
        Assert.Equal("BCR EUR", Assert.Single((await Send(new GetGlAccount(euro.Id))).NameHistory).Name);

        // Regulile de dimensiuni nu se fac mai largi decat cele mostenite: 607 cere centrul de cost de la clasa 6.
        var goods = accounts.Single(a => a.Code == "607");
        Assert.Contains((await Send(new GetGlAccount(goods.Id))).Dimensions, d => d.DimensionType == "COST_CENTER" && d.Mode == 2 && d.IsInherited);
        var looser = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveGlAccountDimensionRules(goods.Id, [new("COST_CENTER", 1)])));
        Assert.Equal("FIN_ACCOUNT_DIMENSION_RULE_LOOSER", looser.Code);
    }

    [Fact]
    public async Task Treasury_settings_accept_only_postable_accounts_of_the_statutory_chart()
    {
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveExpenseType(null, Unique("CH-"), "Cheltuială test", false, null, null,
            false, Today, null, "999", true, null)));
        Assert.Equal("FIN_ACCOUNT_NOT_POSTABLE", refused.Code);
    }

    private Task<GlAccountCheck> Validate(string code) =>
        InTransaction(sp => sp.GetRequiredService<IGlAccountValidator>().ValidateAsync(Database.CompanyId, code, Today, Ct));
}
