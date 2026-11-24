using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Accounts;
using FluentValidation;

namespace Erp.Finance.Application.Accounts;

public sealed record ChartDto(int Id, string Code, string Name, byte Kind, byte? Purpose, string LanguageCode, bool RequireCompleteMapping, bool IsActive,
    int AccountCount);

/// <summary>Un cont din arborele planului (macheta c1-01). AccountType: 1 activ, 2 pasiv, 3 bifunctional.</summary>
public sealed record GlAccountItemDto(int Id, string Code, string Name, int? ParentId, byte Level, byte? AccountType, bool IsPostable, int? CompanyId,
    bool IsOffBalance, bool IsManagement, bool StatutoryReporting, byte CurrencyMode, string? CurrencyCode, byte PartyRequirement, bool ManualPosting,
    string? SaftAccountCode, byte TaxTreatment, byte ClosingTarget, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsSystem, bool IsActive);

public sealed record AccountDimensionDto(string DimensionType, string DimensionName, byte Mode, string FromCode, bool IsInherited);

public sealed record AccountRoleDto(int Id, int? CompanyId, string Role, byte? PartnerKind, string? CurrencyCode, DateOnly ValidFrom, DateOnly? ValidTo);

public sealed record AccountNameHistoryDto(string Name, DateTime ValidFrom, DateTime ValidTo, string? ChangedByName);

/// <summary>Fisa contului (macheta c1-01): datele, dimensiunile efective, rolurile si istoricul denumirilor.</summary>
public sealed record GlAccountDetailDto(int Id, int ChartId, string Code, string Name, int? ParentId, string? ParentCode, byte Level, byte? AccountType,
    bool IsPostable, int? CompanyId, bool IsOffBalance, bool IsManagement, bool StatutoryReporting, byte CurrencyMode, string? CurrencyCode,
    byte PartyRequirement, bool ManualPosting, string? SaftAccountCode, byte TaxTreatment, byte ClosingTarget, DateOnly? ValidFrom, DateOnly? ValidTo,
    bool IsSystem, bool IsActive, byte[] RowVersion, IReadOnlyList<AccountDimensionDto> Dimensions, IReadOnlyList<AccountRoleDto> Roles,
    IReadOnlyList<AccountNameHistoryDto> NameHistory);

public sealed record RoleAssignmentDto(int Id, int? CompanyId, string Role, int AccountId, string AccountCode, string AccountName, byte? PartnerKind,
    string? CurrencyCode, DateOnly ValidFrom, DateOnly? ValidTo);

public sealed record RoleAssignmentInput(string Role, int AccountId, byte? PartnerKind, string? CurrencyCode, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record DimensionRuleInput(string DimensionType, byte Mode);

public sealed record GlAccountRecord(int? Id, int? ParentId, string Code, string Name, int? CompanyId, byte CurrencyMode, string? CurrencyCode,
    byte PartyRequirement, bool StatutoryReporting, bool ManualPosting, byte TaxTreatment, DateOnly? ValidFrom, DateOnly? ValidTo, byte[]? RowVersion);

/// <summary>De ce nu poate primi note un cod: 0 poate, 1 nu exista (sau e al altei firme), 2 inactiv sau in afara vigorii, 3 are analitice.</summary>
public enum PostableReason : byte
{
    Ok = 0,
    Missing = 1,
    Inactive = 2,
    HasChildren = 3,
}

/// <summary>Accesul la planurile de conturi prin procedurile fin.usp_GlAccount*, fin.usp_ChartOfAccounts* (NET-06).</summary>
public interface IChartOfAccountsStore
{
    Task<IReadOnlyList<ChartDto>> ChartsAsync(CancellationToken ct);

    Task<IReadOnlyList<GlAccountItemDto>> AccountsAsync(int? chartId, int companyId, bool includeInactive, CancellationToken ct);

    Task<GlAccountDetailDto?> GetAsync(int id, int companyId, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(GlAccountRecord account, int userId, CancellationToken ct);

    Task<byte[]> SetActiveAsync(int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct);

    Task SaveDimensionRulesAsync(int accountId, IReadOnlyList<DimensionRuleInput> rules, int userId, CancellationToken ct);

    Task<IReadOnlyList<RoleAssignmentDto>> RolesAsync(int companyId, CancellationToken ct);

    Task SaveRolesAsync(int companyId, IReadOnlyList<RoleAssignmentInput> roles, int userId, CancellationToken ct);

    Task<(GlAccountDto? Account, PostableReason Reason)> FindPostableAsync(int companyId, string code, DateOnly onDate, CancellationToken ct);

    Task<GlAccountDto?> FindByRoleAsync(int companyId, string role, byte? partnerKind, string? currencyCode, DateOnly onDate, CancellationToken ct);

    Task<IReadOnlyList<GlAccountRuleDto>> DimensionRulesAsync(int accountId, CancellationToken ct);
}

/// <summary>Planul de conturi pentru celelalte module (C1 6).</summary>
public sealed class ChartOfAccountsQueries(IChartOfAccountsStore store) : IChartOfAccountsQueries, IGlAccountValidator
{
    public async Task<GlAccountDto?> FindPostableAsync(int companyId, string code, DateOnly onDate, CancellationToken ct) =>
        (await store.FindPostableAsync(companyId, code.Trim(), onDate, ct).ConfigureAwait(false)).Account;

    public Task<IReadOnlyList<GlAccountRuleDto>> GetDimensionRulesAsync(int accountId, CancellationToken ct) => store.DimensionRulesAsync(accountId, ct);

    public Task<GlAccountDto?> FindByRoleAsync(int companyId, string role, PartnerAccountKind? partnerKind, string? currencyCode, DateOnly onDate,
        CancellationToken ct) =>
        store.FindByRoleAsync(companyId, role, (byte?)partnerKind, currencyCode, onDate, ct);

    public async Task<GlAccountCheck> ValidateAsync(int companyId, string code, DateOnly onDate, CancellationToken ct)
    {
        var (_, reason) = await store.FindPostableAsync(companyId, code.Trim(), onDate, ct).ConfigureAwait(false);
        return reason switch
        {
            PostableReason.Ok => GlAccountCheck.Valid,
            PostableReason.Missing => new(false, $"Contul {code} nu există în planul de conturi al firmei."),
            PostableReason.Inactive => new(false, $"Contul {code} e inactiv sau nu e în vigoare la {onDate:dd.MM.yyyy}."),
            _ => new(false, $"Contul {code} are analitice; alegeți un cont analitic."),
        };
    }
}

/// <summary>Validarea comuna a codurilor de cont din F1–F5 la salvare (C1 4.1 pasul 3): 51806 FIN_ACCOUNT_NOT_POSTABLE.</summary>
public static class GlAccountGuard
{
    public static async Task EnsurePostableAsync(this IGlAccountValidator validator, int companyId, string? code, DateOnly onDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var check = await validator.ValidateAsync(companyId, code, onDate, ct).ConfigureAwait(false);
        if (!check.IsValid)
        {
            throw new ErpException(ErpErrorKind.Validation, "FIN_ACCOUNT_NOT_POSTABLE", check.Reason!);
        }
    }
}

// ---------- Interogari ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetCharts : IQuery<IReadOnlyList<ChartDto>>;

/// <summary>Conturile unui plan (implicit cel romanesc), comune si ale firmei curente (C1 11, macheta c1-01).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetGlAccounts(int? ChartId = null, bool IncludeInactive = false) : IQuery<IReadOnlyList<GlAccountItemDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetGlAccount(int Id) : IQuery<GlAccountDetailDto>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetGlAccountRoles : IQuery<IReadOnlyList<RoleAssignmentDto>>;

internal sealed class ChartOfAccountsQueryHandlers(IChartOfAccountsStore store, ICurrentContext context) :
    IQueryHandler<GetCharts, IReadOnlyList<ChartDto>>,
    IQueryHandler<GetGlAccounts, IReadOnlyList<GlAccountItemDto>>,
    IQueryHandler<GetGlAccount, GlAccountDetailDto>,
    IQueryHandler<GetGlAccountRoles, IReadOnlyList<RoleAssignmentDto>>
{
    public Task<IReadOnlyList<ChartDto>> Handle(GetCharts request, CancellationToken cancellationToken) => store.ChartsAsync(cancellationToken);

    public Task<IReadOnlyList<GlAccountItemDto>> Handle(GetGlAccounts request, CancellationToken cancellationToken) =>
        store.AccountsAsync(request.ChartId, context.CompanyId, request.IncludeInactive, cancellationToken);

    public async Task<GlAccountDetailDto> Handle(GetGlAccount request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, context.CompanyId, cancellationToken).ConfigureAwait(false)
        ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_ACCOUNT_NOT_FOUND", "Contul nu există.");

    public Task<IReadOnlyList<RoleAssignmentDto>> Handle(GetGlAccountRoles request, CancellationToken cancellationToken) =>
        store.RolesAsync(context.CompanyId, cancellationToken);
}

// ---------- Comenzi ----------

/// <summary>
/// Adauga un analitic sub un cont sintetic al planului romanesc (codul parintelui, punct, sufix) sau modifica un cont (C1 3.2, 3.3): conturile
/// livrate isi pastreaza codul si denumirea; atributele de contare (moneda, partener, raportare statutara, note manuale, tratament fiscal,
/// vigoare) se pot schimba. CompanyId: analiticul doar pentru firma curenta.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
[Audited("Fin.GlAccount.Save")]
public sealed record SaveGlAccount(int? Id, int? ParentId, string Code, string Name, bool OnlyThisCompany, byte CurrencyMode, string? CurrencyCode,
    byte PartyRequirement, bool StatutoryReporting = true, bool ManualPosting = true, byte TaxTreatment = 1, DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null, byte[]? RowVersion = null) : ICommand<FinanceSaved>;

internal sealed class SaveGlAccountValidator : AbstractValidator<SaveGlAccount>
{
    public SaveGlAccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[0-9]{3,4}(\\.[0-9A-Za-z]{1,10})*$")
            .WithMessage("Codul are simbolul sintetic, apoi analiticul după punct (de exemplu 5121.01).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ParentId).NotNull().When(x => x.Id is null).WithMessage("Alegeți contul sub care se adaugă analiticul.");
        RuleFor(x => x.CurrencyMode).InclusiveBetween((byte)0, (byte)2);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3).When(x => x.CurrencyMode == 2).WithMessage("Alegeți moneda contului.");
        RuleFor(x => x.PartyRequirement).InclusiveBetween((byte)0, (byte)3);
        RuleFor(x => x.TaxTreatment).InclusiveBetween((byte)1, (byte)4);
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidFrom is not null && x.ValidTo is not null);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveGlAccountHandler(IChartOfAccountsStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveGlAccount, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveGlAccount request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveAsync(new GlAccountRecord(request.Id, request.ParentId, request.Code.Trim(), request.Name.Trim(),
            request.OnlyThisCompany ? context.CompanyId : null, request.CurrencyMode, request.CurrencyMode == 2 ? request.CurrencyCode?.Trim().ToUpperInvariant() : null,
            request.PartyRequirement, request.StatutoryReporting, request.ManualPosting, request.TaxTreatment, request.ValidFrom, request.ValidTo,
            request.RowVersion), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GlAccount", saved.Id, request with { RowVersion = null });
        return saved;
    }
}

/// <summary>Activeaza sau dezactiveaza un cont (C1 2, regula 9): un cont nu se sterge; un cont cu analitice active nu se dezactiveaza.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
[Audited("Fin.GlAccount.SetActive")]
public sealed record SetGlAccountActive(int Id, bool IsActive, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class SetGlAccountActiveHandler(IChartOfAccountsStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetGlAccountActive, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SetGlAccountActive request, CancellationToken cancellationToken)
    {
        var version = await store.SetActiveAsync(request.Id, request.IsActive, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GlAccount", request.Id, new { request.IsActive });
        return new FinanceSaved(request.Id, version);
    }
}

/// <summary>Regulile de dimensiuni proprii ale contului (C1 3.2): mai stricte decat cele mostenite, nu mai largi (51813).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
[Audited("Fin.GlAccount.DimensionRules")]
public sealed record SaveGlAccountDimensionRules(int AccountId, IReadOnlyList<DimensionRuleInput> Rules) : ICommand<bool>;

internal sealed class SaveGlAccountDimensionRulesHandler(IChartOfAccountsStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveGlAccountDimensionRules, bool>
{
    public async Task<bool> Handle(SaveGlAccountDimensionRules request, CancellationToken cancellationToken)
    {
        await store.SaveDimensionRulesAsync(request.AccountId, request.Rules, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GlAccount", request.AccountId, request.Rules);
        return true;
    }
}

/// <summary>Atribuirile de roluri ale firmei (C1 3.2), inlocuite in bloc; cele implicite ale instalarii raman.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
[Audited("Fin.GlAccount.Roles")]
public sealed record SaveGlAccountRoles(IReadOnlyList<RoleAssignmentInput> Roles) : ICommand<IReadOnlyList<RoleAssignmentDto>>;

internal sealed class SaveGlAccountRolesValidator : AbstractValidator<SaveGlAccountRoles>
{
    public SaveGlAccountRolesValidator() =>
        RuleForEach(x => x.Roles).ChildRules(r =>
        {
            r.RuleFor(x => x.Role).Must(role => GlAccountRoles.All.Contains(role)).WithMessage("Rolul nu există.");
            r.RuleFor(x => x.PartnerKind).InclusiveBetween((byte)1, (byte)4).When(x => x.PartnerKind is not null);
            r.RuleFor(x => x.CurrencyCode).Length(3).When(x => x.CurrencyCode is not null);
        });
}

internal sealed class SaveGlAccountRolesHandler(IChartOfAccountsStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveGlAccountRoles, IReadOnlyList<RoleAssignmentDto>>
{
    public async Task<IReadOnlyList<RoleAssignmentDto>> Handle(SaveGlAccountRoles request, CancellationToken cancellationToken)
    {
        await store.SaveRolesAsync(context.CompanyId, request.Roles, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GlAccountRole", context.CompanyId, request.Roles);
        return await store.RolesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
    }
}
