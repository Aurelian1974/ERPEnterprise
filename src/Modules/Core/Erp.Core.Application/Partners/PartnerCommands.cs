using System.Globalization;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using Erp.Core.Domain;
using FluentValidation;

namespace Erp.Core.Application.Partners;

/// <summary>
/// Creare sau modificare de partener (02.01, 3.2). Codul gol la creare se aloca din seria „Core.Partner” a firmei curente.
/// PersonalId (CNP, doar persoana fizica) se trimite numai cu ChangePersonalId = true; altfel CNP-ul existent ramane.
/// Properties = null lasa proprietatile neschimbate; altfel le inlocuieste pe toate. AnafCheckedAt = momentul interogarii ANAF
/// din care s-au preluat datele („Preia de la ANAF”); null pastreaza data ultimei verificari.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.Partner.Save")]
public sealed record SavePartner(int? Id, string? Code, string Name, byte PartnerType, string? FiscalCode, bool IsCustomer, bool IsSupplier,
    int? PartnerGroupId, bool VatPayer, bool VatOnCollection, bool AnafInactive, string? TradeRegisterNo, string Address, string City,
    string? CountyCode, string CountryCode, string? PostalCode, string? Phone, string? Email, int? IntraGroupCompanyId, bool IsActive,
    byte[]? RowVersion, bool ChangePersonalId = false, string? PersonalId = null, IReadOnlyList<PartnerPropertyValue>? Properties = null,
    DateTime? AnafCheckedAt = null) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.PartnerAddress.Save")]
public sealed record SavePartnerAddress(int? Id, int PartnerId, int AddressType, string? Name, string Address, string City, string? CountyCode,
    string CountryCode, string? PostalCode, string? Phone, string? Email, bool IsDefault, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.PartnerBankAccount.Save")]
public sealed record SavePartnerBankAccount(int? Id, int PartnerId, string Iban, string BankName, string Currency, bool IsDefault, bool IsActive,
    byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.PartnerContact.Save")]
public sealed record SavePartnerContact(int? Id, int PartnerId, string Name, string? Role, string? Phone, string? Email, string? Notes, bool IsActive,
    byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Grupa de parteneri (Configurare › Grupe de parteneri), la nivelul instalării.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.PartnerGroup.Save")]
public sealed record SavePartnerGroup(int? Id, string Code, string Name, int SortOrder, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>
/// Afisarea CNP-ului unei persoane fizice (GDPR, A13): cere Core.Partner.ViewPersonalData si lasa o intrare de audit
/// (fara CNP) in aceeasi tranzactie. E comanda, nu interogare, tocmai ca accesul sa fie jurnalizat.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerViewPersonalData)]
[Audited("Core.Partner.ViewPersonalData")]
[AllowedWhenLicenseReadOnly]
public sealed record RevealPartnerPersonalId(int Id) : ICommand<PartnerPersonalIdDto>;

internal static class PartnerRules
{
    public const string DocumentType = "Core.Partner";
    public const string CodePattern = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$";

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>CUI-ul in forma salvata: cifrele pentru juridica RO, codul TVA cu majuscule pentru UE, textul pentru non-UE, nimic pentru persoana fizica.</summary>
    public static string? NormalizeFiscalCode(byte partnerType, string? value) => (PartnerType)partnerType switch
    {
        Domain.PartnerType.LegalRomania => RomanianFiscalCode.Normalize(value),
        Domain.PartnerType.LegalEu => value is null ? null : EuVatNumber.Normalize(value),
        Domain.PartnerType.LegalNonEu => Clean(value)?.ToUpperInvariant(),
        _ => null,
    };

    /// <summary>Proprietatea se aplica rolului partenerului (AppliesTo: 1 client, 2 furnizor, 3 ambele).</summary>
    public static bool Applies(byte appliesTo, bool isCustomer, bool isSupplier) =>
        appliesTo == 3 || (appliesTo == 1 && isCustomer) || (appliesTo == 2 && isSupplier);
}

internal sealed class SavePartnerValidator : AbstractValidator<SavePartner>
{
    public SavePartnerValidator()
    {
        RuleFor(x => x.Name).Name();
        RuleFor(x => x.Code).Matches(PartnerRules.CodePattern).When(x => !string.IsNullOrWhiteSpace(x.Code))
            .WithMessage("Codul are 1–30 caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Code).NotEmpty().When(x => x.Id is not null).WithMessage("Codul este obligatoriu.");
        RuleFor(x => x.PartnerType).InclusiveBetween((byte)1, (byte)4).WithMessage("Alegeți tipul partenerului.");
        RuleFor(x => x.IsCustomer).Equal(true).When(x => !x.IsSupplier).WithMessage("Bifați cel puțin un rol: client sau furnizor.");

        RuleFor(x => x.FiscalCode).Must(RomanianFiscalCode.IsValid).When(x => x.PartnerType == (byte)PartnerType.LegalRomania)
            .WithMessage("CUI-ul nu este valid (cifra de control greșită).");
        RuleFor(x => x.FiscalCode).Must(EuVatNumber.IsValid).When(x => x.PartnerType == (byte)PartnerType.LegalEu)
            .WithMessage("Codul de TVA UE are prefixul țării urmat de 2–13 litere sau cifre (de exemplu DE298765432).");
        RuleFor(x => x.FiscalCode).NotEmpty().MaximumLength(30).When(x => x.PartnerType == (byte)PartnerType.LegalNonEu)
            .WithMessage("Codul fiscal este obligatoriu (cel mult 30 de caractere).");
        RuleFor(x => x.FiscalCode).Empty().When(x => x.PartnerType == (byte)PartnerType.Individual)
            .WithMessage("Persoana fizică nu are CUI; CNP-ul se completează separat.");
        RuleFor(x => x.CountryCode).Equal("RO").When(x => x.PartnerType == (byte)PartnerType.LegalRomania)
            .WithMessage("O persoană juridică română are sediul în România.");
        RuleFor(x => x.CountryCode).NotEqual("RO").When(x => x.PartnerType is (byte)PartnerType.LegalEu or (byte)PartnerType.LegalNonEu)
            .WithMessage("Pentru o persoană juridică străină alegeți țara sediului.");

        RuleFor(x => x.PersonalId).Must(RomanianPersonalId.IsValid)
            .When(x => x.ChangePersonalId && !string.IsNullOrWhiteSpace(x.PersonalId))
            .WithMessage("CNP-ul nu este valid (13 cifre, cifra de control greșită).");
        RuleFor(x => x.PersonalId).Empty().When(x => x.PartnerType != (byte)PartnerType.Individual)
            .WithMessage("CNP-ul se completează doar pentru persoane fizice.");

        RuleFor(x => x.VatOnCollection).Equal(false).When(x => !x.VatPayer).WithMessage("TVA la încasare se aplică doar plătitorilor de TVA.");
        RuleFor(x => x.TradeRegisterNo).MaximumLength(30).WithMessage("Numărul de la registrul comerțului are cel mult 30 de caractere.");
        RuleFor(x => x.CountyCode).NotEmpty().When(x => x.CountryCode == "RO").WithMessage("Județul este obligatoriu pentru România.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        this.Address(x => x.Address, x => x.City, x => x.CountryCode, x => x.PostalCode, required: true);
        this.ContactDetails(x => x.Phone, x => x.Email);
    }
}

internal sealed class SavePartnerHandler(IPartnerStore store, INumberSeriesService series, IPersonalDataProtector protector, ICurrentContext context,
    IAuditScope audit, IOutbox outbox, TimeProvider clock) : ICommandHandler<SavePartner, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePartner request, CancellationToken cancellationToken)
    {
        if (request.Properties is not null)
        {
            var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
            PartnerPropertyRules.Validate(request, lookups);
        }

        var code = PartnerRules.Clean(request.Code);
        if (code is null)
        {
            if (context.CompanyId == 0)
            {
                throw Field("Code", "Alegeți firma curentă (seria de coduri a partenerilor e pe firmă) sau introduceți codul.");
            }

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            code = (await series.NextAsync(context.CompanyId, PartnerRules.DocumentType, today, cancellationToken).ConfigureAwait(false)).Formatted;
        }

        var individual = request.PartnerType == (byte)PartnerType.Individual;
        var personalId = individual && request.ChangePersonalId ? PartnerRules.Clean(request.PersonalId) : null;
        var partner = new PartnerRecord(request.Id, code, request.Name.Trim(), request.PartnerType,
            PartnerRules.NormalizeFiscalCode(request.PartnerType, request.FiscalCode),
            individual && request.ChangePersonalId, personalId is null ? null : protector.Protect(RomanianPersonalId.Normalize(personalId)),
            request.IsCustomer, request.IsSupplier, request.PartnerGroupId, request.VatPayer, request.VatOnCollection, request.AnafInactive,
            PartnerRules.Clean(request.TradeRegisterNo), request.Address.Trim(), request.City.Trim(), PartnerRules.Clean(request.CountyCode),
            request.CountryCode, PartnerRules.Clean(request.PostalCode), PartnerRules.Clean(request.Phone), PartnerRules.Clean(request.Email),
            request.IntraGroupCompanyId, request.IsActive, request.Properties, request.RowVersion, CheckedAt(request.AnafCheckedAt));
        var saved = await store.SaveAsync(partner, context.UserId, cancellationToken).ConfigureAwait(false);

        // Fara date personale in audit (NET-18): numele unei persoane fizice si CNP-ul nu se scriu, doar faptul ca s-a schimbat.
        audit.Describe("Core.Partner", saved.Id, new
        {
            partner.Code,
            Name = individual ? null : partner.Name,
            partner.PartnerType, partner.FiscalCode, partner.IsCustomer, partner.IsSupplier, partner.PartnerGroupId, partner.VatPayer,
            partner.VatOnCollection, partner.AnafInactive, partner.CountryCode, partner.CountyCode, partner.IntraGroupCompanyId, partner.IsActive,
            partner.AnafCheckedAt,
            PersonalIdChanged = partner.PersonalIdChange ? true : (bool?)null,
            PropertiesChanged = partner.Properties is null ? (bool?)null : true,
        });
        await outbox.EnqueueAsync(new PartenerModificat(saved.Id, partner.Code, partner.Name, partner.FiscalCode, partner.VatPayer,
            partner.VatOnCollection, partner.AnafInactive, partner.IsActive) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    // Momentul vine de la client (din raspunsul ANAF); nu poate fi in viitor.
    private DateTime? CheckedAt(DateTime? value)
    {
        if (value is not { } checkedAt)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var utc = checkedAt.Kind == DateTimeKind.Local ? checkedAt.ToUniversalTime() : DateTime.SpecifyKind(checkedAt, DateTimeKind.Utc);
        return utc > now ? now : utc;
    }

    private static ErpValidationException Field(string field, string message) =>
        new(new Dictionary<string, string[]> { [char.ToLowerInvariant(field[0]) + field[1..]] = [message] });
}

/// <summary>Valorile proprietatilor se valideaza dupa tipul definitiei (02.01, 3.2); cea obligatorie se cere doar daca se aplica rolului.</summary>
internal static class PartnerPropertyRules
{
    public static void Validate(SavePartner request, PartnerLookupsDto lookups)
    {
        var errors = new Dictionary<string, string[]>();
        var definitions = lookups.Properties.ToDictionary(d => d.Id);
        var values = request.Properties!;
        foreach (var group in values.GroupBy(v => v.PropertyDefinitionId))
        {
            if (!definitions.TryGetValue(group.Key, out var definition))
            {
                errors[$"properties.{group.Key}"] = ["Proprietatea nu există sau nu este valabilă pentru firma curentă."];
                continue;
            }

            var message = Check(definition, [.. group], lookups);
            if (message is not null)
            {
                errors[$"properties.{definition.Id}"] = [message];
            }
        }

        foreach (var definition in lookups.Properties.Where(d => d.IsRequired && PartnerRules.Applies(d.AppliesTo, request.IsCustomer, request.IsSupplier)))
        {
            if (values.All(v => v.PropertyDefinitionId != definition.Id))
            {
                errors[$"properties.{definition.Id}"] = [$"„{definition.Name}” este obligatorie."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ErpValidationException(errors);
        }
    }

    private static string? Check(PropertyDefinitionDto definition, IReadOnlyList<PartnerPropertyValue> values, PartnerLookupsDto lookups)
    {
        if (definition.DataType != 6 && values.Count > 1)
        {
            return "Proprietatea are o singură valoare.";
        }

        foreach (var value in values)
        {
            var filled = (value.ValueText is null ? 0 : 1) + (value.ValueNumber is null ? 0 : 1) + (value.ValueDate is null ? 0 : 1)
                + (value.ValueBool is null ? 0 : 1) + (value.ValueOptionId is null ? 0 : 1) + (value.ValueUserId is null ? 0 : 1);
            var ok = filled == 1 && definition.DataType switch
            {
                1 => !string.IsNullOrWhiteSpace(value.ValueText) && value.ValueText.Length <= 1000,
                2 => value.ValueNumber is not null && IsNumber(value.ValueNumber),
                3 => value.ValueDate is not null,
                4 => value.ValueBool is not null,
                5 or 6 => value.ValueOptionId is { } option
                    && lookups.Options.Any(o => o.Id == option && o.PropertyDefinitionId == definition.Id && o.IsActive),
                7 => value.ValueUserId is { } user && lookups.Users.Any(u => u.Id == user),
                _ => false,
            };
            if (!ok)
            {
                return definition.DataType switch
                {
                    1 => "Textul are cel mult 1000 de caractere.",
                    2 => "Valoarea este un număr cu cel mult 6 zecimale.",
                    3 => "Data nu este validă.",
                    5 or 6 => "Alegeți o valoare din listă.",
                    7 => "Alegeți un utilizator activ.",
                    _ => "Valoarea nu este validă.",
                };
            }
        }

        if (definition.DataType == 6 && values.Select(v => v.ValueOptionId).Distinct().Count() != values.Count)
        {
            return "O valoare apare de două ori.";
        }

        return null;
    }

    private static bool IsNumber(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^-?[0-9]{1,13}(\\.[0-9]{1,6})?$")
        && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}

internal sealed class SavePartnerAddressValidator : AbstractValidator<SavePartnerAddress>
{
    public SavePartnerAddressValidator()
    {
        RuleFor(x => x.PartnerId).GreaterThan(0);
        RuleFor(x => x.AddressType).GreaterThan(0).WithMessage("Alegeți tipul adresei.");
        RuleFor(x => x.Name).MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        RuleFor(x => x.CountyCode).NotEmpty().When(x => x.CountryCode == "RO").WithMessage("Județul este obligatoriu pentru România.");
        RuleFor(x => x.IsDefault).Equal(false).When(x => !x.IsActive).WithMessage("O adresă inactivă nu poate fi implicită.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        this.Address(x => x.Address, x => x.City, x => x.CountryCode, x => x.PostalCode, required: true);
        this.ContactDetails(x => x.Phone, x => x.Email);
    }
}

internal sealed class SavePartnerAddressHandler(IPartnerStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePartnerAddress, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePartnerAddress request, CancellationToken cancellationToken)
    {
        var command = request with
        {
            Name = PartnerRules.Clean(request.Name),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            CountyCode = PartnerRules.Clean(request.CountyCode),
            PostalCode = PartnerRules.Clean(request.PostalCode),
            Phone = PartnerRules.Clean(request.Phone),
            Email = PartnerRules.Clean(request.Email),
        };
        var saved = await store.SaveAddressAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.PartnerAddress", saved.Id, new { command.PartnerId, command.AddressType, command.CountryCode, command.IsDefault, command.IsActive });
        return saved;
    }
}

internal sealed class SavePartnerBankAccountValidator : AbstractValidator<SavePartnerBankAccount>
{
    public SavePartnerBankAccountValidator()
    {
        RuleFor(x => x.PartnerId).GreaterThan(0);
        RuleFor(x => x.Iban).Must(Iban.IsValid).WithMessage("IBAN-ul nu este valid.");
        RuleFor(x => x.BankName).NotEmpty().WithMessage("Banca este obligatorie.").MaximumLength(100);
        RuleFor(x => x.Currency).Matches("^[A-Z]{3}$").WithMessage("Moneda are 3 litere (ISO 4217, de exemplu RON).");
        RuleFor(x => x.IsDefault).Equal(false).When(x => !x.IsActive).WithMessage("Un cont inactiv nu poate fi implicit.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePartnerBankAccountHandler(IPartnerStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePartnerBankAccount, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePartnerBankAccount request, CancellationToken cancellationToken)
    {
        var command = request with { Iban = Iban.Normalize(request.Iban), BankName = request.BankName.Trim() };
        var saved = await store.SaveBankAccountAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.PartnerBankAccount", saved.Id, new { command.PartnerId, command.Currency, command.IsDefault, command.IsActive });
        return saved;
    }
}

internal sealed class SavePartnerContactValidator : AbstractValidator<SavePartnerContact>
{
    public SavePartnerContactValidator()
    {
        RuleFor(x => x.PartnerId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().WithMessage("Numele este obligatoriu.").MaximumLength(150).WithMessage("Numele are cel mult 150 de caractere.");
        RuleFor(x => x.Role).MaximumLength(100).WithMessage("Funcția are cel mult 100 de caractere.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Observațiile au cel mult 500 de caractere.");
        this.ContactDetails(x => x.Phone, x => x.Email);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Dați cel puțin un telefon sau un e-mail.").OverridePropertyName("Phone");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePartnerContactHandler(IPartnerStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePartnerContact, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePartnerContact request, CancellationToken cancellationToken)
    {
        var command = request with
        {
            Name = request.Name.Trim(),
            Role = PartnerRules.Clean(request.Role),
            Phone = PartnerRules.Clean(request.Phone),
            Email = PartnerRules.Clean(request.Email),
            Notes = PartnerRules.Clean(request.Notes),
        };
        var saved = await store.SaveContactAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        // Numele, telefonul si e-mailul persoanei de contact sunt date personale: in audit doar partenerul si starea (NET-18).
        audit.Describe("Core.PartnerContact", saved.Id, new { command.PartnerId, command.IsActive });
        return saved;
    }
}

internal sealed class SavePartnerGroupValidator : AbstractValidator<SavePartnerGroup>
{
    public SavePartnerGroupValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999).WithMessage("Ordinea este între 0 și 9999.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePartnerGroupHandler(IPartnerStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SavePartnerGroup, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePartnerGroup request, CancellationToken cancellationToken)
    {
        var command = request with { Code = request.Code.Trim(), Name = request.Name.Trim() };
        var saved = await store.SaveGroupAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.PartnerGroup", saved.Id, new { command.Code, command.Name, command.SortOrder, command.IsActive });
        return saved;
    }
}

internal sealed class RevealPartnerPersonalIdHandler(IPartnerStore store, IPersonalDataProtector protector, IAuditScope audit)
    : ICommandHandler<RevealPartnerPersonalId, PartnerPersonalIdDto>
{
    public async Task<PartnerPersonalIdDto> Handle(RevealPartnerPersonalId request, CancellationToken cancellationToken)
    {
        var encrypted = await store.GetPersonalIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Partner", request.Id);
        return new PartnerPersonalIdDto(encrypted is null ? null : protector.Unprotect(encrypted));
    }
}
