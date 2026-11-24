using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;
using FluentValidation;

namespace Erp.Finance.Application.Declarations;

public sealed record ProfitTaxLineDto(string RowCode, string Label, bool IsManual, string Amount, string? Reason);

/// <summary>Fisa de calcul a unui trimestru (macheta c4-05): regimul, cota, impozitul, nota contata si randurile.</summary>
public sealed record ProfitTaxDto(int Id, int Year, byte Quarter, byte Regime, decimal TaxRate, string TaxDue, long? EntryId, int? EntryNumber, string? PostedAmount,
    DateTime CalculatedAt, DateTime? PostedAt, int Hash, IReadOnlyList<ProfitTaxLineDto> Lines);

public sealed record ProfitTaxData(IReadOnlyList<ProfitTaxTurnover> Turnover, DateOnly YearStart, DateOnly QuarterEnd, byte? LastMonthStatus, byte Regime,
    decimal? Rate, decimal PriorTax, IReadOnlyList<ProfitTaxManualRow> Manual, int? ExpenseAccountId, int? PayableAccountId);

/// <summary>Fisa impozitului pe profit prin fin.usp_ProfitTax_* si nota prin fin.usp_SystemEntry_Write.</summary>
public interface IProfitTaxStore
{
    Task<ProfitTaxData> DataAsync(int companyId, int year, byte quarter, CancellationToken ct);

    Task<int> SaveAsync(int companyId, int year, byte quarter, byte regime, decimal rate, ProfitTaxResult result, int userId, CancellationToken ct);

    Task<ProfitTaxDto?> GetAsync(int companyId, int? year, byte? quarter, DateOnly? periodTo, CancellationToken ct);

    Task<long> WriteEntryAsync(int companyId, long? entryId, DateOnly date, int computationId, IReadOnlyList<SystemLine> lines, string reason, int userId,
        CancellationToken ct);

    Task SetPostedAsync(int companyId, int id, long entryId, decimal amount, int userId, CancellationToken ct);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record GetProfitTax(int Year, byte Quarter) : IQuery<ProfitTaxDto?>;

/// <summary>Calculeaza fisa trimestrului (C4 3.6, 4.4 pasul 1), cumulat de la inceputul anului; randurile manuale raman.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ProfitTaxManage)]
[Audited("Fin.ProfitTax.Calculate")]
public sealed record CalculateProfitTax(int Year, byte Quarter) : ICommand<int>;

/// <summary>Un rand manual al fisei (deduceri, pierderea de recuperat, credite fiscale), cu motiv (decizia 6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ProfitTaxManage)]
[Audited("Fin.ProfitTax.ManualRow")]
public sealed record SetProfitTaxManualRow(int Year, byte Quarter, string RowCode, string Amount, string Reason) : ICommand<int>;

internal sealed class SetProfitTaxManualRowValidator : AbstractValidator<SetProfitTaxManualRow>
{
    public SetProfitTaxManualRowValidator()
    {
        RuleFor(x => x.RowCode).Must(r => ProfitTax.ManualRows.Contains(r)).WithMessage("Rândul nu se completează manual.");
        RuleFor(x => x.Amount).Must(a => Money.TryParse(a, out _)).WithMessage("Suma are cel mult două zecimale.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul rândului manual.").MaximumLength(300);
    }
}

/// <summary>Conteaza impozitul trimestrului (C4 4.4 pasul 2): nota 691 = 4411 in ultima zi a trimestrului, dupa inchiderea operativa a lunii (51954).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ProfitTaxManage)]
[Audited("Fin.ProfitTax.Post")]
public sealed record PostProfitTax(int Year, byte Quarter) : ICommand<long?>;

internal sealed class ProfitTaxQuarterValidator : AbstractValidator<CalculateProfitTax>
{
    public ProfitTaxQuarterValidator() => RuleFor(x => x.Quarter).InclusiveBetween((byte)1, (byte)4);
}

internal sealed class ProfitTaxHandlers(IProfitTaxStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetProfitTax, ProfitTaxDto?>, ICommandHandler<CalculateProfitTax, int>, ICommandHandler<SetProfitTaxManualRow, int>,
    ICommandHandler<PostProfitTax, long?>
{
    public Task<ProfitTaxDto?> Handle(GetProfitTax request, CancellationToken cancellationToken) =>
        store.GetAsync(context.CompanyId, request.Year, request.Quarter, null, cancellationToken);

    public Task<int> Handle(CalculateProfitTax request, CancellationToken cancellationToken) => Calculate(request.Year, request.Quarter, null, cancellationToken);

    public Task<int> Handle(SetProfitTaxManualRow request, CancellationToken cancellationToken) =>
        Calculate(request.Year, request.Quarter, new ProfitTaxManualRow(request.RowCode, Money.ParseOptional(request.Amount) ?? 0, request.Reason.Trim()),
            cancellationToken);

    public async Task<long?> Handle(PostProfitTax request, CancellationToken cancellationToken)
    {
        var data = await store.DataAsync(context.CompanyId, request.Year, request.Quarter, cancellationToken).ConfigureAwait(false);
        if (data.LastMonthStatus is null or < 2)
        {
            throw new ErpException(ErpErrorKind.Conflict, "FIN_PROFIT_TAX_PERIOD_NOT_SOFT_CLOSED",
                "Impozitul se contează după închiderea operativă a ultimei luni din trimestru.");
        }

        var id = await Calculate(request.Year, request.Quarter, null, cancellationToken).ConfigureAwait(false);
        var sheet = (await store.GetAsync(context.CompanyId, request.Year, request.Quarter, null, cancellationToken).ConfigureAwait(false))!;
        var amount = Money.ParseOptional(sheet.TaxDue) ?? 0;
        if (amount == 0 && sheet.EntryId is null)
        {
            return null;
        }

        if (data.ExpenseAccountId is not { } expense || data.PayableAccountId is not { } payable)
        {
            throw new ErpException(ErpErrorKind.Conflict, "FIN_ACCOUNT_NOT_FOUND", "Conturile 691 și 4411 lipsesc din plan.");
        }

        var entry = await store.WriteEntryAsync(context.CompanyId, sheet.EntryId, data.QuarterEnd, id,
            [new SystemLine(expense, 1, amount), new SystemLine(payable, 2, amount)], $"Impozit pe profit T{request.Quarter} {request.Year}", context.UserId,
            cancellationToken).ConfigureAwait(false);
        await store.SetPostedAsync(context.CompanyId, id, entry, amount, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ProfitTax", id, new { request.Year, request.Quarter, Amount = amount, EntryId = entry });
        return entry;
    }

    private async Task<int> Calculate(int year, byte quarter, ProfitTaxManualRow? change, CancellationToken ct)
    {
        var data = await store.DataAsync(context.CompanyId, year, quarter, ct).ConfigureAwait(false);
        var manual = data.Manual.Where(m => change is null || m.RowCode != change.RowCode).ToList();
        if (change is not null)
        {
            manual.Add(change);
        }

        var rate = data.Rate ?? (data.Regime == 2 ? ProfitTax.DefaultMicroRate : ProfitTax.DefaultProfitRate);
        var result = ProfitTax.Compute(data.Turnover, manual, data.Regime, rate, data.PriorTax);
        var id = await store.SaveAsync(context.CompanyId, year, quarter, data.Regime, rate, result, context.UserId, ct).ConfigureAwait(false);
        audit.Describe("Fin.ProfitTax", id, new { year, quarter, result.TaxDue });
        return id;
    }
}
