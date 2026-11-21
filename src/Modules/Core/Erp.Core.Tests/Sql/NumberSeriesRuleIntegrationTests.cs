using Erp.Core.Application.NumberSeries;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Data;

namespace Erp.Core.Tests.Sql;

/// <summary>Serii alternative si reguli de alegere pe SQL Server real (02.01, 3.6 punctul 16).</summary>
public sealed partial class OrganizationIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Series_comes_from_the_user_choice_then_the_matching_rule_then_the_default()
    {
        var (companyId, _) = await SeriesAsync();
        var export = await Send(new SaveNumberSeries(null, companyId, null, "Sales.Invoice", "EXP", 2026, 1, 6, true, false, false, null), companyId);
        var date = new DateOnly(2026, 6, 1);

        var noCondition = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveNumberSeriesRule(null, companyId, "Sales.Invoice", 10, null, null, null, null, "EXP", true, null), companyId));
        Assert.NotEmpty(noCondition.Errors);
        var unknownPrefix = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveNumberSeriesRule(null, companyId, "Sales.Invoice", 10, null, null, null, 3, "NUEXISTA", true, null), companyId));
        Assert.Equal("CORE_INVALID_REFERENCE", unknownPrefix.Code);
        await Send(new SaveNumberSeriesRule(null, companyId, "Sales.Invoice", 10, null, null, null, (byte)PartnerScope.NonEu, "exp", true, null), companyId);

        Assert.Equal("EXD-2026-000001", (await NextInTransactionAsync(companyId, date)).Formatted);
        Assert.Equal("EXP-2026-000001", (await NextInTransactionAsync(companyId, date, choice: new NumberSeriesChoice(PartnerScope: PartnerScope.NonEu))).Formatted);
        Assert.Equal("EXD-2026-000002", (await NextInTransactionAsync(companyId, date, choice: new NumberSeriesChoice(PartnerScope: PartnerScope.Romania))).Formatted);
        Assert.Equal("EXP-2026-000002", (await NextInTransactionAsync(companyId, date, choice: new NumberSeriesChoice(SeriesId: export.Id))).Formatted);

        var wrongYear = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            NextInTransactionAsync(companyId, new DateOnly(2027, 1, 5), choice: new NumberSeriesChoice(SeriesId: export.Id)));
        Assert.Equal(50021, wrongYear.Number);

        await using var session = new DbSession(database.ConnectionString);
        var options = await new Infrastructure.Organization.NumberSeriesService(session).GetOptionsAsync(companyId, "Sales.Invoice", date, null,
            new NumberSeriesChoice(PartnerScope: PartnerScope.NonEu), Ct);
        Assert.Equal(["EXD-2026-000003", "EXP-2026-000003"], options.Series.Select(o => o.NextFormatted).Order());
        Assert.Equal(export.Id, options.ProposedSeriesId);
        var rules = await Send(new ListNumberSeriesRules(companyId), companyId);
        Assert.Equal(("exp", (byte?)3), (rules.Rules.Single().Prefix, rules.Rules.Single().PartnerScope));
    }
}
