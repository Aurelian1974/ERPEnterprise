using Erp.Core.Contracts;
using Erp.Core.Domain;
using FluentValidation;

namespace Erp.Core.Application.Organization;

/// <summary>Creare sau modificare de grup (02.01, 3.1); numai la nivelul instalarii (X-Company-Id: 0).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.CompanyManage)]
[Audited("Core.CompanyGroup.Save")]
public sealed record SaveCompanyGroup(int? Id, string Code, string Name, int? ParentGroupId, int? ParentCompanyId, bool IsActive, byte[]? RowVersion)
    : ICommand<SavedEntity>;

/// <summary>Creare sau modificare de firma (02.01, 3.1). Capitalul social: sir zecimal invariant, de exemplu „200.00”.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.CompanyManage)]
[Audited("Core.Company.Save")]
public sealed record SaveCompany(int? Id, int? GroupId, string Code, string Name, string FiscalCode, bool VatPayer, bool VatOnCollection,
    string TradeRegisterNo, string? ShareCapital, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode,
    int DefaultPrecisionPolicyId, bool IncludeInConsolidation, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.CompanyManage)]
[Audited("Core.Branch.Save")]
public sealed record SaveBranch(int? Id, int CompanyId, string Code, string Name, string Address, string City, string? CountyCode,
    string CountryCode, string? PostalCode, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.CompanyManage)]
[Audited("Core.Location.Save")]
public sealed record SaveLocation(int? Id, int CompanyId, int? BranchId, string Code, string Name, byte LocationType, string? Address,
    string? City, string? CountyCode, string? CountryCode, string? PostalCode, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.CompanyManage)]
[Audited("Core.CompanyBankAccount.Save")]
public sealed record SaveCompanyBankAccount(int? Id, int CompanyId, string Iban, string BankName, string Currency, bool IsDefault, bool IsActive,
    byte[]? RowVersion) : ICommand<SavedEntity>;

internal static partial class OrgRules
{
    public const string CodePattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$";
    public const string CodeMessage = "Codul are 1–20 caractere: litere, cifre, „.”, „_” sau „-”.";

    public static bool IsMoney(string? value) =>
        value is null || System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{1,17}(\\.[0-9]{1,2})?$");

    public static IRuleBuilderOptions<T, string> Code<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Codul este obligatoriu.").Matches(CodePattern).WithMessage(CodeMessage);

    public static IRuleBuilderOptions<T, string> Name<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");

    public static void Address<T>(this AbstractValidator<T> validator, Func<T, string?> address, Func<T, string?> city, Func<T, string?> country,
        Func<T, string?> postalCode, bool required)
    {
        if (required)
        {
            validator.RuleFor(x => address(x)).NotEmpty().WithMessage("Adresa este obligatorie.").OverridePropertyName("Address");
            validator.RuleFor(x => city(x)).NotEmpty().WithMessage("Localitatea este obligatorie.").OverridePropertyName("City");
            validator.RuleFor(x => country(x)).NotEmpty().WithMessage("Țara este obligatorie.").OverridePropertyName("CountryCode");
        }

        validator.RuleFor(x => address(x)).MaximumLength(300).WithMessage("Adresa are cel mult 300 de caractere.").OverridePropertyName("Address");
        validator.RuleFor(x => city(x)).MaximumLength(100).WithMessage("Localitatea are cel mult 100 de caractere.").OverridePropertyName("City");
        validator.RuleFor(x => country(x)).Matches("^[A-Z]{2}$").When(x => country(x) is not null).WithMessage("Țara nu este validă.")
            .OverridePropertyName("CountryCode");
        validator.RuleFor(x => postalCode(x)).Matches("^[0-9]{6}$").When(x => country(x) == "RO" && !string.IsNullOrEmpty(postalCode(x)))
            .WithMessage("Codul poștal românesc are 6 cifre.").OverridePropertyName("PostalCode");
    }
}

internal sealed class SaveCompanyGroupValidator : AbstractValidator<SaveCompanyGroup>
{
    public SaveCompanyGroupValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).Name();
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveCompanyValidator : AbstractValidator<SaveCompany>
{
    public SaveCompanyValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).Name();
        RuleFor(x => x.FiscalCode).Must(RomanianFiscalCode.IsValid).WithMessage("CUI-ul nu este valid (cifra de control greșită).");
        RuleFor(x => x.TradeRegisterNo).Must(TradeRegisterNumber.IsValid)
            .WithMessage("Numărul de la registrul comerțului nu are forma J40/1234/2015 sau J2024000123401.");
        RuleFor(x => x.ShareCapital).Must(OrgRules.IsMoney).WithMessage("Capitalul social este o sumă cu cel mult 2 zecimale.");
        RuleFor(x => x.ShareCapital).NotEmpty().When(x => x.TradeRegisterNo?.StartsWith('J') == true)
            .WithMessage("Capitalul social este obligatoriu pentru societăți (Legea 31/1990).");
        RuleFor(x => x.VatOnCollection).Equal(false).When(x => !x.VatPayer).WithMessage("TVA la încasare se aplică doar plătitorilor de TVA.");
        RuleFor(x => x.DefaultPrecisionPolicyId).GreaterThan(0).WithMessage("Alegeți politica de zecimale.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        this.Address(x => x.Address, x => x.City, x => x.CountryCode, x => x.PostalCode, required: true);
    }
}

internal sealed class SaveBranchValidator : AbstractValidator<SaveBranch>
{
    public SaveBranchValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).Name();
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        this.Address(x => x.Address, x => x.City, x => x.CountryCode, x => x.PostalCode, required: true);
    }
}

internal sealed class SaveLocationValidator : AbstractValidator<SaveLocation>
{
    public SaveLocationValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).Name();
        RuleFor(x => x.LocationType).InclusiveBetween((byte)1, (byte)6).WithMessage("Alegeți tipul locației.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        this.Address(x => x.Address, x => x.City, x => x.CountryCode, x => x.PostalCode, required: false);
        RuleFor(x => x.City).NotEmpty().When(x => !string.IsNullOrEmpty(x.Address)).WithMessage("Localitatea este obligatorie când se dă adresa.");
        RuleFor(x => x.CountryCode).NotEmpty().When(x => !string.IsNullOrEmpty(x.Address)).WithMessage("Țara este obligatorie când se dă adresa.");
    }
}

internal sealed class SaveCompanyBankAccountValidator : AbstractValidator<SaveCompanyBankAccount>
{
    public SaveCompanyBankAccountValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Iban).Must(Iban.IsValid).WithMessage("IBAN-ul nu este valid.");
        RuleFor(x => x.BankName).NotEmpty().WithMessage("Banca este obligatorie.").MaximumLength(100);
        RuleFor(x => x.Currency).Matches("^[A-Z]{3}$").WithMessage("Moneda are 3 litere (ISO 4217, de exemplu RON).");
        RuleFor(x => x.IsDefault).Equal(false).When(x => !x.IsActive).WithMessage("Un cont inactiv nu poate fi implicit.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveCompanyGroupHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveCompanyGroup, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveCompanyGroup request, CancellationToken cancellationToken)
    {
        CompanyScope.EnsureInstallation(context);
        var saved = await store.SaveGroupAsync(request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.CompanyGroup", saved.Id, new { request.Code, request.Name, request.ParentGroupId, request.ParentCompanyId, request.IsActive });
        return saved;
    }
}

internal sealed class SaveCompanyHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit, ILicenseInfo license, IOutbox outbox)
    : ICommandHandler<SaveCompany, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveCompany request, CancellationToken cancellationToken)
    {
        if (request.Id is { } id)
        {
            CompanyScope.Ensure(context, id);
        }
        else
        {
            CompanyScope.EnsureInstallation(context);
        }

        var fiscalCode = RomanianFiscalCode.Normalize(request.FiscalCode)!;
        if (request.IsActive && !license.IsFiscalCodeLicensed(fiscalCode))
        {
            throw ErpException.Conflict("CORE_COMPANY_NOT_LICENSED",
                $"CUI-ul {fiscalCode} nu este în licență (4.4). Cereți furnizorului o licență care include firma.");
        }

        var command = request with
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            FiscalCode = fiscalCode,
            TradeRegisterNo = request.TradeRegisterNo.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim(),
            ShareCapital = string.IsNullOrWhiteSpace(request.ShareCapital) ? null : request.ShareCapital,
        };
        var saved = await store.SaveCompanyAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Company", saved.Id, new
        {
            command.Code, command.Name, command.FiscalCode, command.VatPayer, command.VatOnCollection, command.TradeRegisterNo,
            command.ShareCapital, command.GroupId, command.CountyCode, command.CountryCode, command.IsActive,
        });
        await outbox.EnqueueAsync(new FirmaModificata(command.FiscalCode, command.Name, command.VatPayer, command.VatOnCollection) { CompanyId = saved.Id },
            cancellationToken).ConfigureAwait(false);
        return saved;
    }
}

internal sealed class SaveBranchHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveBranch, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveBranch request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        var command = request with
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim(),
        };
        var saved = await store.SaveBranchAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Branch", saved.Id, new { command.CompanyId, command.Code, command.Name, command.IsActive });
        return saved;
    }
}

internal sealed class SaveLocationHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveLocation, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveLocation request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var command = request with
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Address = Clean(request.Address),
            City = Clean(request.City),
            CountyCode = Clean(request.CountyCode),
            CountryCode = Clean(request.CountryCode),
            PostalCode = Clean(request.PostalCode),
        };
        var saved = await store.SaveLocationAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Location", saved.Id, new { command.CompanyId, command.BranchId, command.Code, command.Name, command.LocationType, command.IsActive });
        return saved;
    }
}

internal sealed class SaveCompanyBankAccountHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveCompanyBankAccount, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveCompanyBankAccount request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        var command = request with { Iban = Iban.Normalize(request.Iban), BankName = request.BankName.Trim() };
        var saved = await store.SaveBankAccountAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.CompanyBankAccount", saved.Id, new { command.CompanyId, command.Iban, command.Currency, command.IsDefault, command.IsActive });
        return saved;
    }
}
