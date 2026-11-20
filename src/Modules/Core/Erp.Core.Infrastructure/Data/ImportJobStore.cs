using System.Data;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Joburile de import si cautarea partenerilor existenti prin core.usp_ImportJob_* si core.usp_Partner_FindForImport (NET-06).</summary>
public sealed class ImportJobStore(IDbSession session) : IImportJobStore
{
    public Task<SavedEntity> CreateAsync(string entityType, int companyId, PartnerImportAnalysis analysis, string report, string payload, int userId,
        CancellationToken ct) => session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ImportJob_Create", new
        {
            EntityType = Ansi(entityType), CompanyId = companyId, analysis.FileName, analysis.AnafCheck, analysis.TotalRows,
            ValidRows = analysis.ValidRows.Count, ErrorRows = analysis.Report.ErrorRows.Count, analysis.WarningRows,
            ErrorReport = report, Payload = payload, UserId = userId,
        }, ct));

    public async Task<ImportJobRecord?> GetAsync(int id, bool forUpdate, bool withData, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<Row>(Command("core.usp_ImportJob_Get",
            new { Id = id, ForUpdate = forUpdate, WithData = withData }, ct)).ConfigureAwait(false);
        return row is null ? null : new ImportJobRecord(row.ToDto(), row.CompanyId, row.ErrorReport, row.Payload);
    }

    public async Task UpdateAsync(int id, byte status, int importedRows, string? lastError, bool clearPayload, int userId, CancellationToken ct) =>
        await session.Connection.ExecuteAsync(Command("core.usp_ImportJob_Update", new
        {
            Id = id, Status = status, ImportedRows = importedRows, LastError = lastError, ClearPayload = clearPayload, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public async Task<IReadOnlyList<ImportJobDto>> ListAsync(string? entityType, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<Row>(Command("core.usp_ImportJob_List",
            new { EntityType = entityType is null ? null : Ansi(entityType), Top = 20 }, ct)).ConfigureAwait(false)).Select(r => r.ToDto())];

    public async Task<IReadOnlyList<ExistingPartner>> FindPartnersAsync(IReadOnlyList<(int Row, string? Code, string? FiscalCode)> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var table = new DataTable();
        table.Columns.Add("RowNumber", typeof(int));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("FiscalCode", typeof(string));
        foreach (var (row, code, fiscalCode) in keys)
        {
            table.Rows.Add(row, (object?)code ?? DBNull.Value, (object?)fiscalCode ?? DBNull.Value);
        }

        var parameters = new DynamicParameters();
        parameters.Add("Keys", table.AsTableValuedParameter("core.PartnerKeyList"));
        var rows = await session.Connection.QueryAsync<ExistingRow>(Command("core.usp_Partner_FindForImport", parameters, ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new ExistingPartner(r.Id, r.Code, r.FiscalCode, r.Name, r.IntraGroupCompanyId,
            r.AnafCheckedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null, r.RowVersion))];
    }

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, commandTimeout: 120, cancellationToken: ct);

    private static DbString Ansi(string value) => new() { Value = value, Length = 20, IsAnsi = true };

    private sealed record ExistingRow(int Id, string Code, string? FiscalCode, string Name, int? IntraGroupCompanyId, DateTime? AnafCheckedAt, byte[] RowVersion);

    private sealed record Row(int Id, string EntityType, int? CompanyId, string FileName, byte Status, bool AnafCheck, int TotalRows, int ValidRows,
        int ErrorRows, int WarningRows, int ImportedRows, string? LastError, DateTime CreatedAt, string CreatedByName, DateTime? CompletedAt,
        byte[] RowVersion, string? ErrorReport, string? Payload)
    {
        public ImportJobDto ToDto() => new(Id, EntityType, FileName, Status, AnafCheck, TotalRows, ValidRows, ErrorRows, WarningRows, ImportedRows,
            LastError, DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc), CreatedByName,
            CompletedAt is { } done ? DateTime.SpecifyKind(done, DateTimeKind.Utc) : null, RowVersion, [], []);
    }
}
