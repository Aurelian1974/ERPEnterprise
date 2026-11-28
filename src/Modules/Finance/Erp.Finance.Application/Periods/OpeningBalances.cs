using Erp.Core.Contracts;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Periods;

/// <summary>Un rand al balantei de deschidere importate (C3 3.7): contul, partenerul, moneda, soldul initial, rulajele cumulate si soldul final.</summary>
public sealed record OpeningBalanceRowInput(int RowNumber, string AccountCode, int? PartnerId, string? CurrencyCode, string? AmountCurrency, string OpeningDebit,
    string OpeningCredit, string TurnoverDebit, string TurnoverCredit, string ClosingDebit, string ClosingCredit);

public sealed record OpeningBalanceRowDto(int RowNumber, string AccountCode, string AccountName, int? PartnerId, string? PartnerName, string? CurrencyCode,
    string OpeningDebit, string OpeningCredit, string TurnoverDebit, string TurnoverCredit, string ClosingDebit, string ClosingCredit);

/// <summary>Importul de solduri initiale al firmei (macheta c3-05). Status: 1 ciorna, 2 validat, 3 anulat.</summary>
public sealed record OpeningBalanceImportDto(int Id, DateOnly StartDate, byte Status, string? FileName, long? OpeningEntryId, long? TurnoverEntryId,
    DateTime? ValidatedAt, IReadOnlyList<OpeningBalanceRowDto> Rows);

/// <summary>Soldurile initiale prin fin.usp_OpeningBalanceImport_* (NET-06).</summary>
public interface IOpeningBalanceStore
{
    Task<int> SaveAsync(int companyId, DateOnly startDate, string? fileName, IReadOnlyList<OpeningBalanceRowInput> rows, int userId, CancellationToken ct);

    Task ValidateAsync(int companyId, int id, int userId, CancellationToken ct);

    Task CancelAsync(int companyId, int id, int userId, CancellationToken ct);

    Task<OpeningBalanceImportDto?> GetAsync(int companyId, CancellationToken ct);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetOpeningBalances : IQuery<OpeningBalanceImportDto?>;

/// <summary>
/// Incarca balanta de deschidere ca ciorna (C3 3.7 pasii 1–3), cu luna de start (oricare, decizia 7): conturile, partenerii ceruti, sold initial +
/// rulaje = sold final pe fiecare rand si echilibrul pe fiecare coloana (51914).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Import")]
public sealed record ImportOpeningBalances(DateOnly StartDate, string? FileName, IReadOnlyList<OpeningBalanceRowInput> Rows) : ICommand<int>;

internal sealed class ImportOpeningBalancesValidator : AbstractValidator<ImportOpeningBalances>
{
    public ImportOpeningBalancesValidator()
    {
        RuleFor(x => x.StartDate).Must(d => d.Day == 1).WithMessage("Luna de start începe în prima zi.");
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= 20_000);
        RuleForEach(x => x.Rows).ChildRules(r =>
        {
            r.RuleFor(x => x.AccountCode).NotEmpty().MaximumLength(20);
            r.RuleFor(x => x).Must(x => new[] { x.OpeningDebit, x.OpeningCredit, x.TurnoverDebit, x.TurnoverCredit, x.ClosingDebit, x.ClosingCredit }
                .All(a => string.IsNullOrWhiteSpace(a) || Money.ParseOptional(a) is not null)).WithMessage("Sumă invalidă pe rând.");
        });
    }
}

/// <summary>Valideaza importul (C3 3.7 pasul 4): notele de deschidere, lunile de dinainte inchise ca importate, concordanta cu evidenta (51915).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Validate")]
public sealed record ValidateOpeningBalances(int Id) : ICommand<bool>;

/// <summary>Anuleaza un import validat cat timp luna de start nu are note (C3 3.7 pasul 5, 51916).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Cancel")]
public sealed record CancelOpeningBalances(int Id) : ICommand<bool>;

internal sealed class OpeningBalanceHandlers(IOpeningBalanceStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetOpeningBalances, OpeningBalanceImportDto?>, ICommandHandler<ImportOpeningBalances, int>, ICommandHandler<ValidateOpeningBalances, bool>,
    ICommandHandler<CancelOpeningBalances, bool>
{
    public Task<OpeningBalanceImportDto?> Handle(GetOpeningBalances request, CancellationToken cancellationToken) => store.GetAsync(context.CompanyId, cancellationToken);

    public async Task<int> Handle(ImportOpeningBalances request, CancellationToken cancellationToken)
    {
        var id = await store.SaveAsync(context.CompanyId, request.StartDate, request.FileName, request.Rows, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.OpeningBalanceImport", id, new { request.StartDate, request.FileName, Rows = request.Rows.Count });
        return id;
    }

    public async Task<bool> Handle(ValidateOpeningBalances request, CancellationToken cancellationToken)
    {
        await store.ValidateAsync(context.CompanyId, request.Id, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.OpeningBalanceImport", request.Id, null);
        return true;
    }

    public async Task<bool> Handle(CancelOpeningBalances request, CancellationToken cancellationToken)
    {
        await store.CancelAsync(context.CompanyId, request.Id, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.OpeningBalanceImport", request.Id, null);
        return true;
    }
}
