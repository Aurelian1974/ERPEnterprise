using Erp.Core.Contracts;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Treasury;

/// <summary>Un cont in lista ecranului Conturi de trezorerie (macheta f1-01), grupata pe tip.</summary>
public sealed record TreasuryAccountItemDto(int Id, TreasuryAccountKind Kind, string Code, string Name, int? BranchId, string? BranchName,
    string CurrencyCode, string? GlAccountCode, int? CompanyBankAccountId, string? BankName, string? Iban, bool IsActive, int CashierCount);

/// <summary>Un casier atribuit casei (F1 3.1, TreasuryAccountUser).</summary>
public sealed record TreasuryCashierDto(int UserId, string DisplayName, bool CanValidate, DateOnly? ValidFrom, DateOnly? ValidTo);

/// <summary>Fisa contului de trezorerie (macheta f1-01): date generale, casieri, serii, plafon propriu.</summary>
public sealed record TreasuryAccountDetailDto(int Id, int CompanyId, TreasuryAccountKind Kind, string Code, string Name, int? BranchId, string CurrencyCode,
    int? CompanyBankAccountId, string? GlAccountCode, int? ResponsibleUserId, int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId,
    string? MaxClosingBalance, CashCloseMode CloseMode, DateOnly? OpeningDate, bool IsActive, bool HasDocuments, DateTime CreatedAt,
    DateTime? ModifiedAt, string? ModifiedByName, IReadOnlyList<TreasuryCashierDto> Cashiers, byte[] RowVersion);

/// <summary>O optiune simpla de lista (punct de lucru, utilizator).</summary>
public sealed record TreasuryOptionDto(int Id, string Code, string Name);

/// <summary>Un cont bancar al firmei (Nucleu, core.CompanyBankAccount), pentru banca si card.</summary>
public sealed record TreasuryBankAccountOptionDto(int Id, string Iban, string BankName, string CurrencyCode);

/// <summary>O serie de numerotare a firmei pentru documentele de casa (chitanta, dispozitie de incasare, dispozitie de plata).</summary>
public sealed record TreasurySeriesOptionDto(int Id, string DocumentType, string Prefix, short Year, int? BranchId);

/// <summary>Listele fisei: puncte de lucru, conturi bancare, valute active, utilizatori, serii.</summary>
public sealed record TreasuryLookupsDto(IReadOnlyList<TreasuryOptionDto> Branches, IReadOnlyList<TreasuryBankAccountOptionDto> BankAccounts,
    IReadOnlyList<string> Currencies, IReadOnlyList<TreasuryOptionDto> Users, IReadOnlyList<TreasurySeriesOptionDto> Series);

public sealed record TreasuryAccountPageDto(IReadOnlyList<TreasuryAccountItemDto> Accounts, TreasuryLookupsDto Lookups);

public sealed record TreasuryCashierInput(int UserId, bool CanValidate, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record TreasuryAccountRecord(int? Id, int CompanyId, TreasuryAccountKind Kind, string Code, string Name, int? BranchId, string CurrencyCode,
    int? CompanyBankAccountId, string? GlAccountCode, int? ResponsibleUserId, int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId,
    decimal? MaxClosingBalance, CashCloseMode CloseMode, DateOnly? OpeningDate, bool IsActive, IReadOnlyList<TreasuryCashierInput>? Cashiers,
    byte[]? RowVersion);

/// <summary>Accesul la date pentru conturile de trezorerie (fin.usp_TreasuryAccount_*, NET-06).</summary>
public interface ITreasuryAccountStore
{
    Task<IReadOnlyList<TreasuryAccountItemDto>> ListAsync(int companyId, string? search, bool includeInactive, CancellationToken ct);

    Task<TreasuryLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct);

    Task<TreasuryAccountDetailDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(TreasuryAccountRecord account, int userId, CancellationToken ct);
}

/// <summary>Lista ecranului Conturi de trezorerie pentru firma curenta, cu listele fisei.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryAccountView)]
public sealed record GetTreasuryAccounts(string? Search = null, bool IncludeInactive = true) : IQuery<TreasuryAccountPageDto>;

/// <summary>Fisa unui cont de trezorerie al firmei curente.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryAccountView)]
public sealed record GetTreasuryAccount(int Id) : IQuery<TreasuryAccountDetailDto>;

/// <summary>
/// Creeaza sau modifica un cont de trezorerie al firmei curente (F1 3.1), cu casierii lui (lista inlocuieste atribuirile). Moneda nu se
/// schimba dupa primul document (51015); un cont cu sold diferit de zero nu se dezactiveaza (51016).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryAccountManage)]
[Audited("Fin.TreasuryAccount.Save")]
public sealed record SaveTreasuryAccount(int? Id, TreasuryAccountKind Kind, string Code, string Name, int? BranchId, string CurrencyCode,
    int? CompanyBankAccountId, string? GlAccountCode, int? ResponsibleUserId, int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId,
    string? MaxClosingBalance, CashCloseMode CloseMode, DateOnly? OpeningDate, bool IsActive, IReadOnlyList<TreasuryCashierInput>? Cashiers,
    byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveTreasuryAccountValidator : AbstractValidator<SaveTreasuryAccount>
{
    public SaveTreasuryAccountValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Alegeți tipul contului.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$").WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");
        RuleFor(x => x.CurrencyCode).Matches("^[A-Z]{3}$").WithMessage("Alegeți moneda.");
        RuleFor(x => x.GlAccountCode).Matches("^[0-9]{3,6}(\\.[0-9A-Za-z]{1,10})?$").When(x => !string.IsNullOrWhiteSpace(x.GlAccountCode))
            .WithMessage("Simbolul contului contabil are 3–6 cifre, opțional cu analitic (de exemplu 5311 sau 5311.01).");
        RuleFor(x => x.BranchId).NotNull().When(x => TreasuryAccountRules.RequiresBranch(x.Kind)).WithMessage("Casa cere punctul de lucru.");
        RuleFor(x => x.CompanyBankAccountId).NotNull().When(x => TreasuryAccountRules.RequiresBankAccount(x.Kind))
            .WithMessage("Alegeți contul bancar al firmei (IBAN-ul vine din Nucleu).");
        RuleFor(x => x.MaxClosingBalance).Must(v => Money.TryParse(v, out var amount) && amount > 0).When(x => !string.IsNullOrWhiteSpace(x.MaxClosingBalance))
            .WithMessage("Plafonul de sold propriu este o sumă pozitivă, cu cel mult două zecimale.");
        RuleFor(x => x.CloseMode).IsInEnum().WithMessage("Alegeți modul de închidere a zilei.");
        RuleFor(x => x.Cashiers).Must(c => c is null || c.Select(x => x.UserId).Distinct().Count() == c.Count)
            .WithMessage("Un casier apare o singură dată.");
        RuleForEach(x => x.Cashiers).Must(c => c.ValidFrom is null || c.ValidTo is null || c.ValidTo >= c.ValidFrom)
            .WithMessage("Atribuirea casierului nu se poate încheia înainte să înceapă.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetTreasuryAccountsHandler(ITreasuryAccountStore store, ICurrentContext context)
    : IQueryHandler<GetTreasuryAccounts, TreasuryAccountPageDto>
{
    public async Task<TreasuryAccountPageDto> Handle(GetTreasuryAccounts request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
        var accounts = await store.ListAsync(context.CompanyId, search, request.IncludeInactive, cancellationToken).ConfigureAwait(false);
        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return new TreasuryAccountPageDto(accounts, lookups);
    }
}

internal sealed class GetTreasuryAccountHandler(ITreasuryAccountStore store, ICurrentContext context)
    : IQueryHandler<GetTreasuryAccount, TreasuryAccountDetailDto>
{
    public async Task<TreasuryAccountDetailDto> Handle(GetTreasuryAccount request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Contul de trezorerie nu există în firma curentă.");
}

internal sealed class SaveTreasuryAccountHandler(ITreasuryAccountStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveTreasuryAccount, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveTreasuryAccount request, CancellationToken cancellationToken)
    {
        var cash = TreasuryAccountRules.IsCash(request.Kind);
        var gl = string.IsNullOrWhiteSpace(request.GlAccountCode) ? TreasuryAccountRules.DefaultGlAccount(request.Kind, request.CurrencyCode)
            : request.GlAccountCode.Trim();
        // Campurile de casa (casieri, gestionar, serii, plafon, inchiderea zilei) nu au sens pe banca, card, tranzit sau depozit.
        var record = new TreasuryAccountRecord(request.Id, context.CompanyId, request.Kind, request.Code.Trim(), request.Name.Trim(), request.BranchId,
            request.CurrencyCode, TreasuryAccountRules.RequiresBankAccount(request.Kind) ? request.CompanyBankAccountId : null, gl,
            cash ? request.ResponsibleUserId : null, cash ? request.ReceiptSeriesId : null, cash ? request.CashInSeriesId : null,
            cash ? request.CashOutSeriesId : null, cash ? Money.ParseOptional(request.MaxClosingBalance) : null, cash ? request.CloseMode : CashCloseMode.OnDemand,
            request.OpeningDate, request.IsActive, cash ? request.Cashiers ?? [] : [], request.RowVersion);
        var saved = await store.SaveAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.TreasuryAccount", saved.Id, new
        {
            record.Kind, record.Code, record.Name, record.BranchId, record.CurrencyCode, record.CompanyBankAccountId, record.GlAccountCode,
            record.ResponsibleUserId, record.ReceiptSeriesId, record.CashInSeriesId, record.CashOutSeriesId, record.MaxClosingBalance, record.CloseMode,
            record.OpeningDate, record.IsActive, Cashiers = record.Cashiers?.Select(c => new { c.UserId, c.CanValidate, c.ValidFrom, c.ValidTo }),
        });
        return saved;
    }
}
