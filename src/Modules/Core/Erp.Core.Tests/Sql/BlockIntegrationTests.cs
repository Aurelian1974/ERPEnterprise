using Erp.Core.Application;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Blocarea partiala si limita de credit pe SQL Server real (02.01, 3.6 punctul 6).</summary>
public sealed partial class PartnerIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Partner_blocked_for_payments_can_still_be_sold_to_and_the_credit_limit_warns_or_blocks()
    {
        var partner = await Send(CityPartner("Alba Iulia", "AB"));
        Assert.Equal(0, (await Send(new GetPartnerBlock(partner.Id))).BlockedFor);

        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SetPartnerBlock(partner.Id, BlockFlags.Payments, " ", null)));
        Assert.Contains("reason", noReason.Errors.Keys);

        var blocked = await Send(new SetPartnerBlock(partner.Id, BlockFlags.Payments, "Litigiu în instanță", null));
        var state = await Send(new GetPartnerBlock(partner.Id));
        Assert.Equal((BlockFlags.Payments, "Litigiu în instanță"), (state.BlockedFor, state.Reason));
        Assert.NotNull(state.BlockedAt);
        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(new SetPartnerBlock(partner.Id, 0, null, new byte[8])));
        Assert.Equal(CoreErrorCodes.ConcurrencyConflict, stale.Code);

        var payments = await Use<IMasterDataBlockCheck, BlockCheckResult>(c => c.CheckPartnerAsync(database.CompanyId, partner.Id, BlockPurpose.Payments, 0, Ct));
        Assert.Equal((BlockLevel.Blocked, "CORE_PARTNER_BLOCKED"), (payments.Level, payments.Code));
        Assert.Equal(BlockLevel.None, (await Use<IMasterDataBlockCheck, BlockCheckResult>(c =>
            c.CheckPartnerAsync(database.CompanyId, partner.Id, BlockPurpose.Sales, 5000, Ct))).Level);
        Assert.Contains((await Send(new SearchPartners(Blocked: true, PageSize: 200))).Items, p => p.Id == partner.Id && p.BlockedFor == BlockFlags.Payments);

        var credit = await Send(new SetPartnerCreditLimit(partner.Id, "1000.00", 2, null));
        var over = await Use<IMasterDataBlockCheck, BlockCheckResult>(c => c.CheckPartnerAsync(database.CompanyId, partner.Id, BlockPurpose.Sales, 1500, Ct));
        Assert.Equal((BlockLevel.Blocked, "CORE_CREDIT_LIMIT_EXCEEDED"), (over.Level, over.Code));
        await Send(new SetPartnerCreditLimit(partner.Id, "1000.00", 1, credit.RowVersion));
        Assert.Equal(BlockLevel.Warning, (await Use<IMasterDataBlockCheck, BlockCheckResult>(c =>
            c.CheckPartnerAsync(database.CompanyId, partner.Id, BlockPurpose.Sales, 1500, Ct))).Level);
        Assert.Equal(BlockLevel.None, (await Use<IMasterDataBlockCheck, BlockCheckResult>(c =>
            c.CheckPartnerAsync(database.CompanyId, partner.Id, BlockPurpose.Sales, 999, Ct))).Level);
        Assert.Equal("1000.00", (await Send(new GetPartnerBlock(partner.Id))).CreditLimit);

        await Send(new SetPartnerBlock(partner.Id, 0, "ignorat", blocked.RowVersion));
        var cleared = await Send(new GetPartnerBlock(partner.Id));
        Assert.Equal((0, (string?)null, (DateTime?)null), (cleared.BlockedFor, cleared.Reason, cleared.BlockedAt));
        var noCredit = await Assert.ThrowsAsync<ErpException>(() => Send(new SetPartnerCreditLimit(partner.Id, "10.00", 1, null), without: CorePermissions.PartnerManageCredit));
        Assert.Equal(ErpErrorKind.Forbidden, noCredit.Kind);
    }
}
