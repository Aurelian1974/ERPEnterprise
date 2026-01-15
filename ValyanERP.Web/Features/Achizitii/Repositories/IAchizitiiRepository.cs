using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Achizitii.Models;

namespace ValyanERP.Web.Features.Achizitii.Repositories;

public interface IAchizitiiRepository
{
    // Document operations
    Task<DataResult> GetDocumentsPagedAsync(DataManagerRequest dm);
    Task<IEnumerable<Document>> GetAllDocumentsAsync();
    Task<Document?> GetDocumentByIdAsync(Guid id);

    // Invoice operations
    Task<DataResult> GetInvoicesPagedAsync(DataManagerRequest dm);
    Task<IEnumerable<Invoice>> GetAllInvoicesAsync();
    Task<Invoice?> GetInvoiceByIdAsync(Guid id);

    // DocumentDetail operations
    Task<IEnumerable<DocumentDetail>> GetDocumentDetailsByDocumentIdAsync(Guid documentId);

    // InvoiceDetail operations
    Task<IEnumerable<InvoiceDetail>> GetInvoiceDetailsByInvoiceIdAsync(Guid invoiceId);

    // DocumentState operations
    Task<IEnumerable<DocumentState>> GetAllDocumentStatesAsync();

    // Purchase invoice operations
    Task<(Guid DocumentId, Guid InvoiceId)> CreatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, Guid userId);

    // Document validation operations
    Task<bool> ValidateDocumentAsync(Guid documentId, Guid userId);

    // Invoice update operations
    Task<bool> UpdateInvoiceAsync(PurchaseInvoiceEditDto dto, Guid userId);

    // User context operations
    Task<Guid?> GetUserCurrentLocationAsync(Guid userId);
}