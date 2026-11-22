using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain.Bank;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

/// <summary>Un profil de format bancar (F2 3.1; macheta cf-04).</summary>
public sealed record BankFormatProfileDto(int Id, string Code, string Name, string? BankBic, StatementFormat StatementFormat, string? CsvMapping,
    PaymentFileFormat PaymentFormat, string? PaymentMapping, short MaxRemittanceLength, bool IsSystem, bool IsActive, int AccountCount, byte[] RowVersion);

public interface IBankFormatProfileStore
{
    Task<IReadOnlyList<BankFormatProfileDto>> ListAsync(int companyId, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(SaveBankFormatProfile profile, int userId, CancellationToken ct);

    Task SetAccountFormatAsync(int companyId, int treasuryAccountId, int? bankFormatProfileId, string? importFolder, int userId, CancellationToken ct);
}

/// <summary>Profilele de format bancar (livrate si ale clientului), cu numarul conturilor firmei care le folosesc.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetBankFormatProfiles : IQuery<IReadOnlyList<BankFormatProfileDto>>;

internal sealed class GetBankFormatProfilesHandler(IBankFormatProfileStore store, ICurrentContext context)
    : IQueryHandler<GetBankFormatProfiles, IReadOnlyList<BankFormatProfileDto>>
{
    public Task<IReadOnlyList<BankFormatProfileDto>> Handle(GetBankFormatProfiles request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, cancellationToken);
}

/// <summary>
/// Salveaza un profil de format bancar (F2 3.1): codul unic pe instalare (51022), maparea CSV verificata la salvare; profilele livrate
/// isi pastreaza codul si formatele (51021), dar pot primi alta denumire sau BIC.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankFormatManage)]
[Audited("Fin.BankFormatProfile.Save")]
public sealed record SaveBankFormatProfile(int? Id, string Code, string Name, string? BankBic, StatementFormat StatementFormat, string? CsvMapping,
    PaymentFileFormat PaymentFormat, string? PaymentMapping, short MaxRemittanceLength, bool IsActive, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveBankFormatProfileValidator : AbstractValidator<SaveBankFormatProfile>
{
    public SaveBankFormatProfileValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches(SettingsRules.CodePattern)
            .WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200);
        RuleFor(x => x.BankBic).Matches("^[A-Za-z]{6}[A-Za-z0-9]{2}([A-Za-z0-9]{3})?$").When(x => !string.IsNullOrWhiteSpace(x.BankBic))
            .WithMessage("BIC-ul are 8 sau 11 caractere.");
        RuleFor(x => x.StatementFormat).IsInEnum().WithMessage("Alegeți formatul extrasului.");
        RuleFor(x => x.PaymentFormat).IsInEnum().WithMessage("Alegeți formatul fișierului de plăți.");
        RuleFor(x => x.MaxRemittanceLength).InclusiveBetween((short)35, (short)500).WithMessage("Lungimea detaliilor este între 35 și 500.");
        RuleFor(x => x.CsvMapping).Must(BeValidMapping).When(x => x.StatementFormat == StatementFormat.Csv)
            .WithMessage("Maparea CSV cere separatorul (un caracter), data și suma (sau debit și credit), ca JSON.");
        RuleFor(x => x.PaymentMapping).Must(BeJson).When(x => !string.IsNullOrWhiteSpace(x.PaymentMapping)).WithMessage("Opțiunile de plată sunt JSON.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }

    private static bool BeValidMapping(string? mapping)
    {
        try
        {
            CsvStatementMapping.Read(mapping);
            return true;
        }
        catch (StatementFormatException)
        {
            return false;
        }
    }

    private static bool BeJson(string? json)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(json!);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

internal sealed class SaveBankFormatProfileHandler(IBankFormatProfileStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveBankFormatProfile, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveBankFormatProfile request, CancellationToken cancellationToken)
    {
        var profile = request with
        {
            Code = request.Code.Trim(), Name = request.Name.Trim(), BankBic = string.IsNullOrWhiteSpace(request.BankBic) ? null : request.BankBic.Trim().ToUpperInvariant(),
            CsvMapping = request.StatementFormat == StatementFormat.Csv ? request.CsvMapping : null,
            PaymentMapping = string.IsNullOrWhiteSpace(request.PaymentMapping) ? null : request.PaymentMapping,
        };
        var saved = await store.SaveAsync(profile, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankFormatProfile", saved.Id, new
        {
            profile.Code, profile.Name, profile.BankBic, profile.StatementFormat, profile.PaymentFormat, profile.MaxRemittanceLength, profile.IsActive,
        });
        return saved;
    }
}

/// <summary>Profilul bancar si folderul de import al unui cont de trezorerie (F2 3.1; Fin.BankFormat.Manage).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankFormatManage)]
[Audited("Fin.TreasuryAccount.BankFormat")]
public sealed record SetTreasuryAccountBankFormat(int TreasuryAccountId, int? BankFormatProfileId, string? ImportFolder) : ICommand<bool>;

internal sealed class SetTreasuryAccountBankFormatValidator : AbstractValidator<SetTreasuryAccountBankFormat>
{
    public SetTreasuryAccountBankFormatValidator()
    {
        RuleFor(x => x.ImportFolder).MaximumLength(260).Must(f => f is null || !f.Contains("..", StringComparison.Ordinal))
            .WithMessage("Folderul de import este o cale absolută, fără „..”.");
    }
}

internal sealed class SetTreasuryAccountBankFormatHandler(IBankFormatProfileStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetTreasuryAccountBankFormat, bool>
{
    public async Task<bool> Handle(SetTreasuryAccountBankFormat request, CancellationToken cancellationToken)
    {
        var folder = string.IsNullOrWhiteSpace(request.ImportFolder) ? null : request.ImportFolder.Trim();
        await store.SetAccountFormatAsync(context.CompanyId, request.TreasuryAccountId, request.BankFormatProfileId, folder, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.TreasuryAccount", request.TreasuryAccountId, new { request.BankFormatProfileId, ImportFolder = folder });
        return true;
    }
}
