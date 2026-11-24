using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Finance.Application.Declarations;

public sealed record VatJournalRowDto(long Id, string DocumentNumber, DateOnly DocumentDate, DateOnly? ExigibilityDate, string? PartnerName, string? PartnerFiscalCode,
    string? PartnerCountryCode, string OperationTypeCode, string VatRateCode, decimal VatRatePercent, string BaseRon, string VatRon, bool IsVatOnCollection,
    bool IsReversal, long EntryId);

public sealed record VatJournalTotalDto(string OperationTypeCode, string Name, string BaseRon, string VatRon, int Rows);

/// <summary>Jurnalul de vanzari sau de cumparari al unei luni (C4 3.1, 10; macheta c4-03): randurile exigibile si totalurile pe tip de operatiune.</summary>
public sealed record VatJournalDto(int Year, int Month, byte Direction, IReadOnlyList<VatJournalRowDto> Rows, IReadOnlyList<VatJournalTotalDto> Totals);

/// <summary>Registrul de TVA prin fin.usp_VatJournal_Get (NET-06).</summary>
public interface IVatRegisterStore
{
    Task<VatJournalDto> JournalAsync(int companyId, int year, int month, byte direction, CancellationToken ct);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record GetVatJournal(int Year, int Month, byte Direction) : IQuery<VatJournalDto>;

internal sealed class GetVatJournalValidator : AbstractValidator<GetVatJournal>
{
    public GetVatJournalValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Direction).InclusiveBetween((byte)1, (byte)2);
    }
}

internal sealed class GetVatJournalHandler(IVatRegisterStore store, ICurrentContext context) : IQueryHandler<GetVatJournal, VatJournalDto>
{
    public Task<VatJournalDto> Handle(GetVatJournal request, CancellationToken cancellationToken) =>
        store.JournalAsync(context.CompanyId, request.Year, request.Month, request.Direction, cancellationToken);
}
