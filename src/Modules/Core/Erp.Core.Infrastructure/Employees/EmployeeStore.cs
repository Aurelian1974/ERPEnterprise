using System.Data;
using Dapper;
using Erp.Core.Application.Employees;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Employees;

/// <summary>Fisele de angajat prin procedurile core.usp_Employee* (NET-06), pe conexiunea si tranzactia cererii.</summary>
public sealed class EmployeeStore(IDbSession session) : IEmployeeStore
{
    public async Task<EmployeePageDto> SearchAsync(EmployeeSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Employee_Search", new
        {
            criteria.Search, criteria.CompanyId, criteria.IsActive, criteria.WithoutUser, criteria.Offset, criteria.PageSize,
        }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<SearchRow>().ConfigureAwait(false)).ToList();
        var active = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        if (rows.Count == 0 && criteria.Offset > 0)
        {
            total = (await SearchAsync(criteria with { Offset = 0, PageSize = 1 }, ct).ConfigureAwait(false)).Total;
        }

        return new EmployeePageDto([.. rows.Select(r => new EmployeeListItemDto(r.Id, r.CompanyId, r.CompanyName, r.BranchId, r.BranchName,
            r.EmployeeNo, r.FirstName, r.LastName, r.Department, r.JobTitle, r.WorkEmail, Date(r.HireDate), Date(r.LeaveDate), r.IsActive, r.UserId,
            r.UserName))], total, active);
    }

    public async Task<EmployeeDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command("core.usp_Employee_Get", new { Id = id }, ct)).ConfigureAwait(false);
        return r is null ? null : new EmployeeDetailDto(r.Id, r.CompanyId, r.CompanyName, r.BranchId, r.BranchName, r.EmployeeNo, r.FirstName,
            r.LastName, r.Department, r.JobTitle, r.WorkEmail, r.Phone, Date(r.HireDate), Date(r.LeaveDate), r.IsActive, r.UserId, r.UserName,
            DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.LastChangedAt, DateTimeKind.Utc), r.ModifiedByName,
            r.RowVersion);
    }

    public async Task<EmployeeLookupsDto> GetLookupsAsync(CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_EmployeeLookups_Get", null, ct)).ConfigureAwait(false);
        var companies = (await grid.ReadAsync<CompanyOptionDto>().ConfigureAwait(false)).ToList();
        var branches = (await grid.ReadAsync<BranchOptionDto>().ConfigureAwait(false)).ToList();
        return new EmployeeLookupsDto(companies, branches);
    }

    public async Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<HistoryRow>(Command("core.usp_Employee_History", new { Id = id }, ct)).ConfigureAwait(false))
            .Select(h => new PartnerHistoryEntryDto(h.Id, DateTime.SpecifyKind(h.OccurredAt, DateTimeKind.Utc), h.Action, h.EntityType, h.EntityId,
                h.UserName, h.Changes))];

    public Task<EmployeeSaveResult> SaveAsync(SaveEmployee command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<EmployeeSaveResult>(Command("core.usp_Employee_Save", new
        {
            command.Id, command.CompanyId, command.BranchId, command.EmployeeNo, command.FirstName, command.LastName, command.Department,
            command.JobTitle, command.WorkEmail, command.Phone, HireDate = command.HireDate?.ToDateTime(TimeOnly.MinValue),
            LeaveDate = command.LeaveDate?.ToDateTime(TimeOnly.MinValue), command.IsActive, command.RowVersion, UserId = userId,
        }, ct));

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private sealed record SearchRow(int Id, int CompanyId, string CompanyName, int? BranchId, string? BranchName, string EmployeeNo, string FirstName,
        string LastName, string? Department, string? JobTitle, string? WorkEmail, DateTime? HireDate, DateTime? LeaveDate, bool IsActive, int? UserId,
        string? UserName, int TotalCount);

    private sealed record DetailRow(int Id, int CompanyId, string CompanyName, int? BranchId, string? BranchName, string EmployeeNo, string FirstName,
        string LastName, string? Department, string? JobTitle, string? WorkEmail, string? Phone, DateTime? HireDate, DateTime? LeaveDate, bool IsActive,
        int? UserId, string? UserName, DateTime CreatedAt, DateTime LastChangedAt, string ModifiedByName, byte[] RowVersion);

    private sealed record HistoryRow(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);
}
