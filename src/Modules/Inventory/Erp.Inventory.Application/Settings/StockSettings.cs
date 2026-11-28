using Erp.Core.Contracts;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using FluentValidation;

namespace Erp.Inventory.Application.Settings;

/// <summary>Setarile de stoc ale firmei (02.05, S1 3.3); ecranul e Configurare › Stocuri (S7).</summary>
public sealed record StockSettingsDto(ValuationMethod DefaultValuationMethod, StockKeepingLevel StockKeepingLevel,
    NegativeStockPolicy DefaultNegativeStockPolicy, bool RequireCountOnHandover, short BackdatingDays, bool BackdatingWithRight, byte TransferMode,
    byte CostRecalcScope, byte LandedCostAllocation, byte ExpiryPickRule, short ExpiryWarningDays, short ReservationExpiryDays, byte CountFreezeMode,
    byte[] RowVersion);

/// <summary>Accesul la date pentru setarile firmei (inv.usp_StockSettings_*).</summary>
public interface IStockSettingsStore
{
    Task<StockSettingsDto?> GetAsync(int companyId, CancellationToken ct);

    Task<InventorySaved> SaveAsync(int companyId, SaveStockSettings settings, int userId, CancellationToken ct);
}

/// <summary>Setarile de stoc ale firmei curente; firma neinitializata primeste valorile implicite si gestiunea TRANZIT.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetStockSettings : IQuery<StockSettingsDto>;

/// <summary>Salveaza setarile de stoc ale firmei curente (S1 3.3). Nivelul de evidenta se schimba separat (S1 3.8).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.SettingsManage)]
[Audited("Inv.StockSettings.Save")]
public sealed record SaveStockSettings(ValuationMethod DefaultValuationMethod, NegativeStockPolicy DefaultNegativeStockPolicy,
    bool RequireCountOnHandover, short BackdatingDays, bool BackdatingWithRight, byte TransferMode, byte CostRecalcScope, byte LandedCostAllocation,
    byte ExpiryPickRule, short ExpiryWarningDays, short ReservationExpiryDays, byte CountFreezeMode, byte[] RowVersion) : ICommand<InventorySaved>;

internal sealed class SaveStockSettingsValidator : AbstractValidator<SaveStockSettings>
{
    public SaveStockSettingsValidator()
    {
        RuleFor(x => x.DefaultValuationMethod).IsInEnum().WithMessage("Alegeți metoda de evaluare implicită.");
        RuleFor(x => x.DefaultNegativeStockPolicy).IsInEnum().WithMessage("Alegeți politica implicită de stoc negativ.");
        RuleFor(x => x.BackdatingDays).InclusiveBetween((short)0, (short)366).WithMessage("Zilele în urmă sunt între 0 și 366.");
        RuleFor(x => x.TransferMode).InclusiveBetween((byte)1, (byte)3).WithMessage("Alegeți modul transferurilor.");
        RuleFor(x => x.CostRecalcScope).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți modul de recalculare a costului.");
        RuleFor(x => x.LandedCostAllocation).InclusiveBetween((byte)1, (byte)3).WithMessage("Alegeți repartizarea costurilor adăugate.");
        RuleFor(x => x.ExpiryPickRule).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți ordinea de ieșire a loturilor.");
        RuleFor(x => x.ExpiryWarningDays).InclusiveBetween((short)0, (short)3650).WithMessage("Zilele de avertizare sunt între 0 și 3650.");
        RuleFor(x => x.ReservationExpiryDays).InclusiveBetween((short)0, (short)3650).WithMessage("Durata rezervării este între 0 și 3650 de zile.");
        RuleFor(x => x.CountFreezeMode).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți ce se întâmplă cu mișcările în timpul inventarului.");
        RuleFor(x => x.RowVersion).NotNull().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetStockSettingsHandler(IStockSettingsStore store, IStockCompanySetup setup, ICurrentContext context)
    : IQueryHandler<GetStockSettings, StockSettingsDto>
{
    public async Task<StockSettingsDto> Handle(GetStockSettings request, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(context.CompanyId, cancellationToken).ConfigureAwait(false) is { } settings)
        {
            return settings;
        }

        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return await store.GetAsync(context.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Setările de stoc nu au fost create.");
    }
}

internal sealed class SaveStockSettingsHandler(IStockSettingsStore store, IStockCompanySetup setup, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveStockSettings, InventorySaved>
{
    public async Task<InventorySaved> Handle(SaveStockSettings request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var saved = await store.SaveAsync(context.CompanyId, request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockSettings", saved.Id, request with { RowVersion = [] });
        return saved;
    }
}
