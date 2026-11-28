using Erp.Core.Contracts;
using Erp.Inventory.Application.Warehouses;

namespace Erp.Inventory.Infrastructure.Events;

/// <summary>
/// Consumatorul FirmaModificata (02.05, S1 5.2): la prima aparitie a unei firme, setarile de stoc implicite si gestiunea TRANZIT. Idempotent
/// (02.01, 4.5): firmele deja initializate nu se schimba.
/// </summary>
public sealed class InitializeCompanyStock(IStockCompanySetup setup) : IIntegrationEventHandler<FirmaModificata>
{
    public Task HandleAsync(FirmaModificata evt, CancellationToken ct) => setup.EnsureAsync(evt.CompanyId, ct);
}
