using System.Data;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Organization;

/// <summary>
/// Accesul pe proprietarul articolelor si partenerilor prin core.usp_OwnerAccess_Check / usp_OwnerOptions_Get (21.11.2026). La nivelul
/// instalarii (firma 0) nu exista noduri: inregistrarile se vad toate, iar cele noi sunt doar comune.
/// </summary>
public sealed class OwnerAccess(IDbSession session, ICurrentContext context, ISqlErrorTranslator errors) : IOwnerAccess
{
    public Task EnsureVisibleAsync(string entityType, int id, string viewPermission, CancellationToken ct) =>
        context.CompanyId == 0 ? Task.CompletedTask : Check(entityType, id, null, viewPermission, manage: false, ct);

    public Task EnsureManageAsync(string entityType, int? id, OwnerRef? newOwner, string managePermission, CancellationToken ct)
    {
        if (context.CompanyId == 0)
        {
            return newOwner is null || newOwner == OwnerRef.Common
                ? Task.CompletedTask
                : throw new ErpException(ErpErrorKind.Forbidden, "CORE_OWNER_ACCESS_DENIED", "Alegeți întâi o firmă; la nivelul instalării se adaugă doar înregistrări comune.");
        }

        return Check(entityType, id, newOwner, managePermission, manage: true, ct);
    }

    public async Task<OwnerOptionsDto?> GetOptionsAsync(string viewPermission, string managePermission, CancellationToken ct)
    {
        if (context.CompanyId == 0)
        {
            return null;
        }

        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_OwnerOptions_Get", new
        {
            context.UserId, context.CompanyId, ViewPermission = viewPermission, ManagePermission = managePermission,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var company = await grid.ReadSingleOrDefaultAsync<CompanyRow>().ConfigureAwait(false);
        var nodes = (await grid.ReadAsync<OwnerNodeDto>().ConfigureAwait(false)).ToList();
        return company is null ? null : new OwnerOptionsDto(company.Id, company.Name, company.ViewWholeCompany, company.ManageWholeCompany, nodes);
    }

    // Apelat si din interogari (fara TransactionBehavior), deci codurile THROW se traduc aici (NET-09).
    private async Task Check(string entityType, int? id, OwnerRef? newOwner, string permission, bool manage, CancellationToken ct)
    {
        try
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("core.usp_OwnerAccess_Check", new
            {
                EntityType = new DbString { Value = entityType, IsAnsi = true, Length = 10 }, Id = id, CheckNewOwner = newOwner is not null,
                OwnerCompanyId = newOwner?.CompanyId, OwnerBranchId = newOwner?.BranchId, OwnerLocationId = newOwner?.LocationId,
                Permission = permission, Manage = manage, context.UserId, context.CompanyId,
            }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception exception) when (errors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private sealed record CompanyRow(int Id, string Name, bool ViewWholeCompany, bool ManageWholeCompany);
}
