using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Foile Excel de import pentru celelalte module (ISpreadsheetFiles), prin acelasi cititor ClosedXML ca importurile Nucleului.</summary>
public sealed class SpreadsheetFiles(IPartnerSpreadsheet spreadsheet) : ISpreadsheetFiles
{
    public byte[] Write(string sheetName, IReadOnlyList<SpreadsheetColumn> columns, IReadOnlyList<IReadOnlyList<string?>> rows, IReadOnlyList<SpreadsheetList> lists,
        IReadOnlyList<string>? errors = null) =>
        spreadsheet.Write(Definition(sheetName, columns), rows, [.. lists.Select(l => new SheetList(l.Header, l.Values, l.ColumnKeys))], errors);

    public IReadOnlyList<SpreadsheetRow> Read(string sheetName, IReadOnlyList<SpreadsheetColumn> columns, byte[] content) =>
        [.. spreadsheet.Read(Definition(sheetName, columns), content).Select(r => new SpreadsheetRow(r.RowNumber, r.Values))];

    private static SheetDefinition Definition(string sheetName, IReadOnlyList<SpreadsheetColumn> columns) =>
        new(sheetName, [.. columns.Select(c => new PartnerSheetColumn(c.Key, c.Header, c.Required, c.Example))]);
}

/// <summary>Joburile de import pentru celelalte module (IImportJobs), peste core.usp_ImportJob_*, cu firma si utilizatorul din context.</summary>
public sealed class ImportJobs(IImportJobStore store, ICurrentContext context) : IImportJobs
{
    public async Task<int> CreateAsync(ImportJobDraft job, CancellationToken ct) =>
        (await store.CreateAsync(new NewImportJob(job.EntityType, context.CompanyId, job.FileName, false, job.TotalRows, job.ValidRows, job.ErrorRows,
            job.WarningRows, job.ReportJson, job.PayloadJson), context.UserId, ct).ConfigureAwait(false)).Id;

    public async Task<ImportJobState?> GetAsync(int id, bool forUpdate, CancellationToken ct)
    {
        var record = await store.GetAsync(id, forUpdate, withData: true, ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var j = record.Job;
        return new ImportJobState(j.Id, j.EntityType, record.CompanyId, j.FileName, j.Status, j.TotalRows, j.ValidRows, j.ErrorRows, j.ImportedRows, j.LastError,
            j.CreatedAt, j.CompletedAt, record.ErrorReport, record.Payload);
    }

    public Task UpdateAsync(int id, byte status, int importedRows, string? lastError, bool clearPayload, CancellationToken ct) =>
        store.UpdateAsync(id, status, importedRows, lastError, clearPayload, context.UserId, ct);
}
