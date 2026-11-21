using System.Data;
using Dapper;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Organization;

/// <summary>Regulile de alegere a seriei (core.usp_NumberSeriesRule_*, NET-06).</summary>
public sealed class NumberSeriesRuleStore(IDbSession session) : INumberSeriesRuleStore
{
    public async Task<NumberSeriesRulePageDto> ListAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_NumberSeriesRule_List", new { CompanyId = companyId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var rules = (await grid.ReadAsync<NumberSeriesRuleDto>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<Application.Partners.UserOptionDto>().ConfigureAwait(false)).ToList();
        return new NumberSeriesRulePageDto(rules, users);
    }

    public Task<SavedEntity> SaveAsync(SaveNumberSeriesRule rule, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(new CommandDefinition("core.usp_NumberSeriesRule_Save", new
        {
            rule.Id, rule.CompanyId, rule.DocumentTypeCode, rule.Priority, rule.UserId, rule.BranchId, rule.LocationId, rule.PartnerScope, rule.Prefix,
            rule.IsActive, rule.RowVersion, ModifiedBy = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}
