using System.Globalization;
using Erp.Core.Contracts;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

/// <summary>
/// Setarile Finantelor pe firma (F2 3.6 si 4.3): aprobarea cu patru ochi, nivelul validarii automate (0 niciodata, 1 OP-uri, perechi,
/// carduri si reguli, 2 si incasarile potrivite exact pe factura), toleranta comisionului de card (procent din brut, implicit 3%) si zilele
/// lucratoare pana la decontarea cardului (implicit 2).
/// </summary>
public sealed record FinanceSettingsDto(bool RequireApproval, int AutoValidateLevel, string CardFeeTolerancePercent, int CardSettlementDays);

public static class FinanceSettingCodes
{
    public const string RequireApproval = "Fin.PaymentOrder.RequireApproval";
    public const string AutoValidateLevel = "Fin.Bank.AutoValidateLevel";
    public const string CardFeeTolerancePercent = "Fin.Card.FeeTolerancePercent";
    public const string CardSettlementDays = "Fin.Card.SettlementDays";
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetFinanceSettings : IQuery<FinanceSettingsDto>;

internal sealed class GetFinanceSettingsHandler(IFinanceSettings settings, ICurrentContext context) : IQueryHandler<GetFinanceSettings, FinanceSettingsDto>
{
    public async Task<FinanceSettingsDto> Handle(GetFinanceSettings request, CancellationToken cancellationToken)
    {
        var company = context.CompanyId;
        return new FinanceSettingsDto(await settings.RequireApprovalAsync(company, cancellationToken).ConfigureAwait(false),
            await settings.AutoValidateLevelAsync(company, cancellationToken).ConfigureAwait(false),
            Money.Format(await settings.CardFeeTolerancePercentAsync(company, cancellationToken).ConfigureAwait(false)),
            await settings.CardSettlementDaysAsync(company, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Salveaza setarile Finantelor ale firmei curente (Configurare › Finante), auditat.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
[Audited("Fin.Settings.Save")]
public sealed record SaveFinanceSettings(bool RequireApproval, int AutoValidateLevel, string CardFeeTolerancePercent, int CardSettlementDays)
    : ICommand<FinanceSettingsDto>;

internal sealed class SaveFinanceSettingsValidator : AbstractValidator<SaveFinanceSettings>
{
    public SaveFinanceSettingsValidator()
    {
        RuleFor(x => x.AutoValidateLevel).InclusiveBetween(0, 2).WithMessage("Nivelul validării automate este 0, 1 sau 2.");
        RuleFor(x => x.CardFeeTolerancePercent).Must(v => Money.TryParse(v, out var p) && p is >= 0 and <= 20)
            .WithMessage("Toleranța comisionului de card este un procent între 0 și 20, cu cel mult 2 zecimale.");
        RuleFor(x => x.CardSettlementDays).InclusiveBetween(0, 30).WithMessage("Zilele până la decontare sunt între 0 și 30.");
    }
}

internal sealed class SaveFinanceSettingsHandler(IFinanceSettings settings, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveFinanceSettings, FinanceSettingsDto>
{
    public async Task<FinanceSettingsDto> Handle(SaveFinanceSettings request, CancellationToken cancellationToken)
    {
        var tolerance = Money.ParseOptional(request.CardFeeTolerancePercent) ?? 0;
        var values = new Dictionary<string, string>
        {
            [FinanceSettingCodes.RequireApproval] = request.RequireApproval ? "true" : "false",
            [FinanceSettingCodes.AutoValidateLevel] = request.AutoValidateLevel.ToString(CultureInfo.InvariantCulture),
            [FinanceSettingCodes.CardFeeTolerancePercent] = tolerance.ToString("0.00", CultureInfo.InvariantCulture),
            [FinanceSettingCodes.CardSettlementDays] = request.CardSettlementDays.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var (code, value) in values)
        {
            await settings.SaveAsync(context.CompanyId, code, value, context.UserId, cancellationToken).ConfigureAwait(false);
        }

        audit.Describe("Fin.Settings", context.CompanyId, values);
        return new FinanceSettingsDto(request.RequireApproval, request.AutoValidateLevel, Money.Format(tolerance), request.CardSettlementDays);
    }
}
