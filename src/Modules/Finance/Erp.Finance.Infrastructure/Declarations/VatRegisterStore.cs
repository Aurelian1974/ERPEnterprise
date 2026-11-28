using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>Registrul de TVA prin fin.usp_VatJournal_Get (NET-06).</summary>
public sealed class VatRegisterStore(IDbSession session) : IVatRegisterStore
{
    public async Task<VatJournalDto> JournalAsync(int companyId, int year, int month, byte direction, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("fin.usp_VatJournal_Get", new
        {
            CompanyId = companyId, Year = (short)year, Month = (byte)month, Direction = direction,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<Row>().ConfigureAwait(false)).Select(r => new VatJournalRowDto(r.Id, r.DocumentNumber, DateOnly.FromDateTime(r.DocumentDate),
            r.ExigibilityDate is { } e ? DateOnly.FromDateTime(e) : null, r.PartnerName, r.PartnerFiscalCode, r.PartnerCountryCode?.Trim(), r.OperationTypeCode,
            r.VatRateCode, r.VatRatePercent, Money.Format(r.BaseRon), Money.Format(r.VatRon), r.IsVatOnCollection, r.IsReversal, r.EntryId)).ToList();
        var totals = (await grid.ReadAsync<(string Code, string Name, decimal BaseRon, decimal VatRon, int Rows)>().ConfigureAwait(false))
            .Select(t => new VatJournalTotalDto(t.Code, t.Name, Money.Format(t.BaseRon), Money.Format(t.VatRon), t.Rows)).ToList();
        return new VatJournalDto(year, month, direction, rows, totals);
    }

    private sealed record Row(long Id, string DocumentNumber, DateTime DocumentDate, DateTime? ExigibilityDate, string? PartnerName, string? PartnerFiscalCode,
        string? PartnerCountryCode, string OperationTypeCode, string VatRateCode, decimal VatRatePercent, decimal BaseRon, decimal VatRon, bool IsVatOnCollection,
        bool IsReversal, long EntryId);
}
