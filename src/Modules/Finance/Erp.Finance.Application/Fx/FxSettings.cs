using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Fx;

/// <summary>
/// Setarile multivaluta ale firmei (02.02, F4 2–4; macheta cf-08): regula implicita de curs incrucisat (1 BNR din ziua platii, 2 BNR + marja,
/// 3 cursul facturii) si marja implicita, reevaluarea avansurilor, blocarea alocarilor cu data intr-o luna reevaluata.
/// </summary>
public sealed record FxSettingsDto(CrossRateRule CrossCurrencyRule, string CrossCurrencyMarkupPercent, bool RevalueAdvances, bool BlockAllocationsInRevaluedMonth);

public static class FxSettingCodes
{
    public const string CrossCurrencyRule = "Fin.Fx.CrossCurrencyRule";
    public const string CrossCurrencyMarkupPercent = "Fin.Fx.CrossCurrencyMarkupPercent";
    public const string RevalueAdvances = "Fin.Fx.RevalueAdvances";
    public const string BlockAllocationsInRevaluedMonth = "Fin.Fx.BlockAllocationsInRevaluedMonth";
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetFxSettings : IQuery<FxSettingsDto>;

internal sealed class GetFxSettingsHandler(IFinanceSettings settings, ICurrentContext context) : IQueryHandler<GetFxSettings, FxSettingsDto>
{
    public async Task<FxSettingsDto> Handle(GetFxSettings request, CancellationToken cancellationToken)
    {
        var company = context.CompanyId;
        return new FxSettingsDto(await settings.FxCrossCurrencyRuleAsync(company, cancellationToken).ConfigureAwait(false),
            (await settings.FxCrossCurrencyMarkupPercentAsync(company, cancellationToken).ConfigureAwait(false)).ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture),
            await settings.RevalueAdvancesAsync(company, cancellationToken).ConfigureAwait(false),
            await settings.BlockAllocationsInRevaluedMonthAsync(company, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Salveaza setarile multivaluta ale firmei curente (Configurare › Finante › Valuta), auditat; se aplica de la salvare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxSettings)]
[Audited("Fin.Fx.Settings.Save")]
public sealed record SaveFxSettings(CrossRateRule CrossCurrencyRule, string CrossCurrencyMarkupPercent, bool RevalueAdvances, bool BlockAllocationsInRevaluedMonth)
    : ICommand<FxSettingsDto>;

internal sealed class SaveFxSettingsValidator : AbstractValidator<SaveFxSettings>
{
    public SaveFxSettingsValidator()
    {
        RuleFor(x => x.CrossCurrencyRule).Must(r => r is CrossRateRule.BnrOnPaymentDate or CrossRateRule.BnrOnPaymentDatePlusMarkup or CrossRateRule.InvoiceRate)
            .WithMessage("Regula implicită este BNR din ziua plății, BNR + marjă sau cursul facturii.");
        RuleFor(x => x.CrossCurrencyMarkupPercent).Must(v => decimal.TryParse(v?.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var p) && p is >= 0 and < 100 && decimal.Round(p, 4) == p)
            .WithMessage("Marja este un procent între 0 și 100, cu cel mult 4 zecimale.");
    }
}

internal sealed class SaveFxSettingsHandler(IFinanceSettings settings, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveFxSettings, FxSettingsDto>
{
    public async Task<FxSettingsDto> Handle(SaveFxSettings request, CancellationToken cancellationToken)
    {
        var company = context.CompanyId;
        var markup = decimal.Parse(request.CrossCurrencyMarkupPercent.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        await settings.SaveAsync(company, FxSettingCodes.CrossCurrencyRule, ((byte)request.CrossCurrencyRule).ToString(invariant), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await settings.SaveAsync(company, FxSettingCodes.CrossCurrencyMarkupPercent, markup.ToString(invariant), context.UserId, cancellationToken).ConfigureAwait(false);
        await settings.SaveAsync(company, FxSettingCodes.RevalueAdvances, request.RevalueAdvances ? "true" : "false", context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await settings.SaveAsync(company, FxSettingCodes.BlockAllocationsInRevaluedMonth, request.BlockAllocationsInRevaluedMonth ? "true" : "false", context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Settings", company, request);
        return new FxSettingsDto(request.CrossCurrencyRule, markup.ToString("0.00##", invariant), request.RevalueAdvances, request.BlockAllocationsInRevaluedMonth);
    }
}
