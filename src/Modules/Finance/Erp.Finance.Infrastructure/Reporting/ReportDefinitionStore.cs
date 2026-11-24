using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>Rapoartele definite prin fin.usp_ReportDefinition_List, _Get, _Save, _AccountCodes.</summary>
public sealed class ReportDefinitionStore(IDbSession session) : IReportDefinitionStore
{
    public async Task<IReadOnlyList<ReportDefinitionItemDto>> ListAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("fin.usp_ReportDefinition_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new ReportDefinitionItemDto(r.Id, r.Code, r.Name, r.Kind, r.Version, D(r.ValidFrom), D(r.ValidTo), r.Rows)).ToList();

    public async Task<ReportDefinitionData?> GetAsync(int companyId, int? id, string? code, DateOnly? onDate, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_ReportDefinition_Get", new
        {
            CompanyId = companyId, Id = id, Code = code, OnDate = onDate?.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        var rows = (await grid.ReadAsync<ReportRowDto>().ConfigureAwait(false)).ToList();
        var codes = (await grid.ReadAsync<string>().ConfigureAwait(false)).ToList();
        var dims = (await grid.ReadAsync<ReportDimension>().ConfigureAwait(false)).ToList();
        return h is null ? null : new ReportDefinitionData(new ReportDefinitionDto(h.Id, h.Code, h.Name, h.Kind, h.ChartId, h.Version, D(h.ValidFrom), D(h.ValidTo),
            h.RowVersion, rows), codes, dims);
    }

    public async Task<IReadOnlyList<string>> AccountCodesAsync(int companyId, int? chartId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<string>(Command("fin.usp_ReportDefinition_AccountCodes", new { CompanyId = companyId, ChartId = chartId }, ct))
            .ConfigureAwait(false)).ToList();

    public Task<int> SaveAsync(int companyId, int? id, byte[]? rowVersion, string code, string name, int? chartId, IReadOnlyList<ReportRowDto> rows, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_ReportDefinition_Save", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, Code = code, Name = name, ChartId = chartId, UserId = userId,
            Rows = JsonSerializer.Serialize(rows.Select(r => new { rowCode = r.RowCode, label = r.Label, formula = r.Formula, isTotal = r.IsTotal, indent = r.Indent })),
        }, ct));

    private static DateOnly? D(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : null;

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ItemRow(int Id, string Code, string Name, byte Kind, short Version, DateTime? ValidFrom, DateTime? ValidTo, int Rows);

    private sealed record HeaderRow(int Id, string Code, string Name, byte Kind, int? ChartId, short Version, DateTime? ValidFrom, DateTime? ValidTo, byte[] RowVersion);
}
