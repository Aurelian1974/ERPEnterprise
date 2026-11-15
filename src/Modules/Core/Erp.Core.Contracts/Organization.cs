namespace Erp.Core.Contracts;

/// <summary>
/// Alocarea numarului la validarea unui document (02.01, 4.2), in tranzactia documentului: fara goluri in seriile fiscale,
/// iar un rollback anuleaza si alocarea. Seria implicita a punctului de lucru are prioritate fata de cea a firmei.
/// </summary>
public interface INumberSeriesService
{
    Task<DocumentNumber> NextAsync(int companyId, string documentType, DateOnly documentDate, CancellationToken ct, int? branchId = null);
}

/// <summary>Numarul alocat; Formatted = {Prefix}-{An}-{Numar} (macheta 07c).</summary>
public sealed record DocumentNumber(string Prefix, int Year, long Number, string Formatted);

/// <summary>Modificarea datelor unei firme (02.01, 5): Integrari (emitentul e-Factura), Finante. CompanyId e in anvelopa.</summary>
public sealed record FirmaModificata(string FiscalCode, string Name, bool VatPayer, bool VatOnCollection) : IntegrationEvent;
