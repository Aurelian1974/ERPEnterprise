using Erp.Core.Contracts;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Treasury;

/// <summary>Un tip de operatiune de trezorerie in Configurare (macheta cf-01).</summary>
public sealed record OperationTypeDto(int Id, string Code, string Name, CashDirection Direction, bool RequiresPartner, bool RequiresEmployee,
    bool RequiresCounterAccount, byte SettlementKind, string? CounterGlAccountCode, string? CashLimitCategory, bool AppliesToCash, bool AppliesToBank,
    bool IsSystem, bool IsActive, byte[] RowVersion);

/// <summary>O versiune a unui tip de cheltuiala (macheta cf-02).</summary>
public sealed record ExpenseTypeDto(int Id, string Code, string Name, bool IsPerDiem, string? DailyAmount, string? NonTaxableDailyLimit,
    bool VatDeductibleOnReceipt, DateOnly ValidFrom, DateOnly? ValidTo, string? CounterGlAccountCode, bool IsSystem, bool IsActive, byte[] RowVersion);

/// <summary>Un plafon de numerar (macheta cf-03): legal (doar citire) sau propriu al firmei.</summary>
public sealed record CashLimitRuleDto(int Id, string Category, string Amount, CashLimitAction Action, DateOnly ValidFrom, DateOnly? ValidTo,
    string? LegalReference, bool IsLegal, byte[] RowVersion);

/// <summary>Accesul la date pentru configurarea trezoreriei (fin.usp_TreasuryOperationType_*, fin.usp_ExpenseType_*, fin.usp_CashLimitRule_*).</summary>
public interface ITreasurySettingsStore
{
    Task<IReadOnlyList<OperationTypeDto>> ListOperationTypesAsync(CancellationToken ct);

    Task<FinanceSaved> SaveOperationTypeAsync(SaveOperationType type, int userId, CancellationToken ct);

    Task<IReadOnlyList<ExpenseTypeDto>> ListExpenseTypesAsync(CancellationToken ct);

    Task<FinanceSaved> SaveExpenseTypeAsync(SaveExpenseType type, decimal? dailyAmount, decimal? nonTaxableLimit, int userId, CancellationToken ct);

    Task<IReadOnlyList<CashLimitRuleDto>> ListCashLimitsAsync(int companyId, CancellationToken ct);

    Task<FinanceSaved> SaveCashLimitAsync(int companyId, SaveCashLimitRule rule, decimal amount, int userId, CancellationToken ct);
}

internal static class SettingsRules
{
    public const string CodePattern = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$";
    public const string GlPattern = "^[0-9]{3,6}(\\.[0-9A-Za-z]{1,10})?$";
}

// ---------- Tipuri de operatiuni ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
public sealed record GetOperationTypes : IQuery<IReadOnlyList<OperationTypeDto>>;

/// <summary>Creeaza sau modifica un tip de operatiune (F1 3.2); tipurile livrate se pot doar redenumi, dezactiva si li se schimba contul.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
[Audited("Fin.Treasury.OperationType.Save")]
public sealed record SaveOperationType(int? Id, string Code, string Name, CashDirection Direction, bool RequiresPartner, bool RequiresEmployee,
    bool RequiresCounterAccount, byte SettlementKind, string? CounterGlAccountCode, string? CashLimitCategory, bool AppliesToCash, bool AppliesToBank,
    bool IsActive, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveOperationTypeValidator : AbstractValidator<SaveOperationType>
{
    public SaveOperationTypeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches(SettingsRules.CodePattern)
            .WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200);
        RuleFor(x => x.Direction).IsInEnum().WithMessage("Alegeți sensul.");
        RuleFor(x => x.SettlementKind).InclusiveBetween((byte)0, (byte)6);
        RuleFor(x => x.CounterGlAccountCode).Matches(SettingsRules.GlPattern).When(x => !string.IsNullOrWhiteSpace(x.CounterGlAccountCode))
            .WithMessage("Simbolul contului are 3–6 cifre, opțional cu analitic.");
        RuleFor(x => x).Must(x => !(x.AppliesToCash && x.Direction == CashDirection.Both)).WithName("direction")
            .WithMessage("Un tip folosit la casă are sensul intrare sau ieșire.");
        RuleFor(x => x).Must(x => x.AppliesToCash || x.AppliesToBank).WithName("appliesToCash").WithMessage("Tipul se folosește la casă, la bancă sau la amândouă.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

// ---------- Tipuri de cheltuieli ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
public sealed record GetExpenseTypes : IQuery<IReadOnlyList<ExpenseTypeDto>>;

/// <summary>Un tip de cheltuiala sau o versiune noua (alt ValidFrom) a lui (F1 3.7, P6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
[Audited("Fin.Treasury.ExpenseType.Save")]
public sealed record SaveExpenseType(int? Id, string Code, string Name, bool IsPerDiem, string? DailyAmount, string? NonTaxableDailyLimit,
    bool VatDeductibleOnReceipt, DateOnly ValidFrom, DateOnly? ValidTo, string? CounterGlAccountCode, bool IsActive, byte[]? RowVersion)
    : ICommand<FinanceSaved>;

internal sealed class SaveExpenseTypeValidator : AbstractValidator<SaveExpenseType>
{
    public SaveExpenseTypeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches(SettingsRules.CodePattern)
            .WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200);
        RuleFor(x => x.DailyAmount).Must(v => Money.TryParse(v, out var a) && a > 0).When(x => x.IsPerDiem)
            .WithMessage("Diurna are o sumă pe zi pozitivă.");
        RuleFor(x => x.NonTaxableDailyLimit).Must(v => Money.TryParse(v, out var a) && a >= 0).When(x => x.IsPerDiem)
            .WithMessage("Diurna are plafonul neimpozabil pe zi.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidTo is not null)
            .WithMessage("Valabilitatea nu se poate încheia înainte să înceapă.");
        RuleFor(x => x.CounterGlAccountCode).Matches(SettingsRules.GlPattern).When(x => !string.IsNullOrWhiteSpace(x.CounterGlAccountCode))
            .WithMessage("Simbolul contului are 3–6 cifre, opțional cu analitic.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

// ---------- Plafoane de numerar ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashLimitManage)]
public sealed record GetCashLimitRules : IQuery<IReadOnlyList<CashLimitRuleDto>>;

/// <summary>Un plafon propriu al firmei curente (F1 3.6), mai strict decat cel legal; plafoanele legale vin doar cu versiunile produsului.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashLimitManage)]
[Audited("Fin.CashLimit.Save")]
public sealed record SaveCashLimitRule(int? Id, string Category, string Amount, CashLimitAction Action, DateOnly ValidFrom, DateOnly? ValidTo,
    byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveCashLimitRuleValidator : AbstractValidator<SaveCashLimitRule>
{
    public SaveCashLimitRuleValidator()
    {
        RuleFor(x => x.Category).Must(c => c is "IncasarePJ" or "IncasarePF" or "PlataPJ" or "PlataPF" or "AvansPersoanaZi" or "SoldCasa")
            .WithMessage("Alegeți categoria plafonului.");
        RuleFor(x => x.Amount).Must(v => Money.TryParse(v, out var a) && a > 0).WithMessage("Plafonul este o sumă pozitivă.");
        RuleFor(x => x.Action).IsInEnum().WithMessage("Alegeți acțiunea.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidTo is not null)
            .WithMessage("Valabilitatea nu se poate încheia înainte să înceapă.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class TreasurySettingsHandlers(ITreasurySettingsStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetOperationTypes, IReadOnlyList<OperationTypeDto>>,
    ICommandHandler<SaveOperationType, FinanceSaved>,
    IQueryHandler<GetExpenseTypes, IReadOnlyList<ExpenseTypeDto>>,
    ICommandHandler<SaveExpenseType, FinanceSaved>,
    IQueryHandler<GetCashLimitRules, IReadOnlyList<CashLimitRuleDto>>,
    ICommandHandler<SaveCashLimitRule, FinanceSaved>
{
    public Task<IReadOnlyList<OperationTypeDto>> Handle(GetOperationTypes request, CancellationToken cancellationToken) =>
        store.ListOperationTypesAsync(cancellationToken);

    public async Task<FinanceSaved> Handle(SaveOperationType request, CancellationToken cancellationToken)
    {
        var clean = request with
        {
            Code = request.Code.Trim(), Name = request.Name.Trim(),
            CounterGlAccountCode = string.IsNullOrWhiteSpace(request.CounterGlAccountCode) ? null : request.CounterGlAccountCode.Trim(),
            CashLimitCategory = string.IsNullOrWhiteSpace(request.CashLimitCategory) ? null : request.CashLimitCategory,
        };
        var saved = await store.SaveOperationTypeAsync(clean, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.TreasuryOperationType", saved.Id, clean with { RowVersion = null });
        return saved;
    }

    public Task<IReadOnlyList<ExpenseTypeDto>> Handle(GetExpenseTypes request, CancellationToken cancellationToken) =>
        store.ListExpenseTypesAsync(cancellationToken);

    public async Task<FinanceSaved> Handle(SaveExpenseType request, CancellationToken cancellationToken)
    {
        var clean = request with
        {
            Code = request.Code.Trim(), Name = request.Name.Trim(),
            CounterGlAccountCode = string.IsNullOrWhiteSpace(request.CounterGlAccountCode) ? null : request.CounterGlAccountCode.Trim(),
        };
        var saved = await store.SaveExpenseTypeAsync(clean, clean.IsPerDiem ? Money.ParseOptional(clean.DailyAmount) : null,
            clean.IsPerDiem ? Money.ParseOptional(clean.NonTaxableDailyLimit) : null, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ExpenseType", saved.Id, clean with { RowVersion = null });
        return saved;
    }

    public Task<IReadOnlyList<CashLimitRuleDto>> Handle(GetCashLimitRules request, CancellationToken cancellationToken) =>
        store.ListCashLimitsAsync(context.CompanyId, cancellationToken);

    public async Task<FinanceSaved> Handle(SaveCashLimitRule request, CancellationToken cancellationToken)
    {
        var amount = Money.ParseOptional(request.Amount) ?? 0;
        var saved = await store.SaveCashLimitAsync(context.CompanyId, request, amount, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashLimitRule", saved.Id, new { request.Category, amount, request.Action, request.ValidFrom, request.ValidTo });
        return saved;
    }
}
