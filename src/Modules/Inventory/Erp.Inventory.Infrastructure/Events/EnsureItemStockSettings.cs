using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;

namespace Erp.Inventory.Infrastructure.Events;

/// <summary>
/// Consumatorul ArticolModificat (02.05, S1 5.2): setarile de stoc implicite ale unui model stocabil nou, in fiecare firma initializata in
/// Stocuri. Idempotent (02.01, 4.5).
/// </summary>
public sealed class EnsureItemStockSettings(IItemStockSettingsStore store, ICurrentContext context) : IIntegrationEventHandler<ArticolModificat>
{
    public Task HandleAsync(ArticolModificat evt, CancellationToken ct) => store.EnsureDefaultsAsync(evt.ItemId, context.UserId == 0 ? 1 : context.UserId, ct);
}
