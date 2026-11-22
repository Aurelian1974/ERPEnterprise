using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Configurarea trezoreriei pe SQL Server real (02.02, F1 3.2, 3.6, 3.7; machetele cf-01…cf-03): tipurile livrate isi pastreaza sensul,
/// tipurile de cheltuieli se schimba prin versiuni cu data de vigoare, plafoanele legale nu se modifica din ecran.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TreasurySettingsIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public TreasurySettingsIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.TreasuryConfigure, FinancePermissions.CashLimitManage]);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.2")]
    public async Task Shipped_operation_types_keep_their_direction_and_client_types_get_unique_codes()
    {
        var types = await Send(new GetOperationTypes());
        var shipped = types.Single(t => t.Code == "PLATA-FURNIZOR");
        var flipped = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveOperationType(shipped.Id, shipped.Code, shipped.Name, CashDirection.In,
            shipped.RequiresPartner, shipped.RequiresEmployee, shipped.RequiresCounterAccount, shipped.SettlementKind, shipped.CounterGlAccountCode,
            null, true, false, true, shipped.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", flipped.Code);

        var renamed = await Send(new SaveOperationType(shipped.Id, shipped.Code, "Plată furnizor în numerar", shipped.Direction, shipped.RequiresPartner,
            shipped.RequiresEmployee, shipped.RequiresCounterAccount, shipped.SettlementKind, "4011", shipped.CashLimitCategory, true, false, true,
            shipped.RowVersion));
        Assert.True(renamed.Id == shipped.Id);

        var code = Unique("INC-SPONS-");
        var created = await Send(new SaveOperationType(null, code, "Sponsorizare primită", CashDirection.In, true, false, false, 0, "7582", "Incasare",
            true, true, true, null));
        Assert.Contains((await Send(new GetOperationTypes())), t => t.Id == created.Id && !t.IsSystem);
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveOperationType(null, code.ToLowerInvariant(), "Dublură", CashDirection.In,
            false, false, false, 0, null, null, true, false, true, null)));
        Assert.Equal("FIN_DUPLICATE_CODE", duplicate.Code);
        var wrongLimit = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveOperationType(null, Unique("PL-"), "Plată", CashDirection.Out,
            false, false, false, 0, null, "Incasare", true, false, true, null)));
        Assert.Equal("FIN_INVALID_REFERENCE", wrongLimit.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.7")]
    public async Task Per_diem_amounts_change_through_a_new_version_with_its_own_valid_from()
    {
        var perDiem = (await Send(new GetExpenseTypes())).First(t => t.Code == "DIURNA-INT");
        var changed = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveExpenseType(perDiem.Id, perDiem.Code, perDiem.Name, true, "60", "57.50",
            false, perDiem.ValidFrom, null, perDiem.CounterGlAccountCode, true, perDiem.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", changed.Code);

        await Send(new SaveExpenseType(null, "DIURNA-INT", "Diurnă internă", true, "60", "70", false, new DateOnly(2027, 1, 1), null, "625", true, null));
        var versions = (await Send(new GetExpenseTypes())).Where(t => t.Code == "DIURNA-INT").ToList();
        Assert.Equal(["60.00", "50.00"], versions.Select(v => v.DailyAmount));
        var sameDate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveExpenseType(null, "diurna-int", "Diurnă", true, "61", "70", false,
            new DateOnly(2027, 1, 1), null, null, true, null)));
        Assert.Equal("FIN_DUPLICATE_CODE", sameDate.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.6")]
    public async Task Legal_limits_are_read_only_and_company_limits_are_saved_per_company()
    {
        var rules = await Send(new GetCashLimitRules());
        var legal = rules.First(r => r.IsLegal && r.Category == "IncasarePJ");
        Assert.Equal("5000.00", legal.Amount);
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveCashLimitRule(legal.Id, legal.Category, "100000", CashLimitAction.Block,
            legal.ValidFrom, null, legal.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", refused.Code);

        var own = await Send(new SaveCashLimitRule(null, "PlataPJ", "3000", CashLimitAction.Warn, new DateOnly(2026, 12, 1), null, null));
        Assert.Contains(await Send(new GetCashLimitRules()), r => r.Id == own.Id && !r.IsLegal && r.Action == CashLimitAction.Warn);
        Assert.DoesNotContain(await Send(new GetCashLimitRules(), companyId: Database.OtherCompanyId), r => r.Id == own.Id);
    }
}
