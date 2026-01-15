using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Achizitii.Models;
using ValyanERP.Web.Features.Achizitii.Repositories;

namespace ValyanERP.Web.Features.Achizitii.Services;

/// <summary>
/// Service for Achizitii (Purchase Invoices) business logic.
/// Handles validation, business rules, and coordinates between repositories.
/// </summary>
public interface IAchizitiiService
{
    /// <summary>
    /// Gets all documents for export purposes.
    /// </summary>
    Task<IEnumerable<Document>> GetAllDocumentsAsync();

    /// <summary>
    /// Gets all invoices for export purposes.
    /// </summary>
    Task<IEnumerable<Invoice>> GetAllInvoicesAsync();

    /// <summary>
    /// Gets document with related details.
    /// </summary>
    Task<DocumentDetailsViewModel?> GetDocumentWithDetailsAsync(Guid documentId);

    /// <summary>
    /// Gets invoice with related details.
    /// </summary>
    Task<InvoiceDetailsViewModel?> GetInvoiceWithDetailsAsync(Guid invoiceId);

    /// <summary>
    /// Gets all available document states for dropdowns.
    /// </summary>
    Task<IEnumerable<DocumentState>> GetDocumentStatesAsync();

    /// <summary>
    /// Creates a new purchase invoice with validation.
    /// </summary>
    Task<PurchaseInvoiceResult> CreatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, Guid userId);

    /// <summary>
    /// Validates purchase invoice data before creation.
    /// </summary>
    Task<ValidationResult> ValidatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto);
}

/// <summary>
/// Service implementation for Achizitii business logic.
/// </summary>
public class AchizitiiService : IAchizitiiService
{
    private readonly IAchizitiiRepository _repository;
    private readonly ILogger<AchizitiiService> _logger;

    public AchizitiiService(IAchizitiiRepository repository, ILogger<AchizitiiService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IEnumerable<Document>> GetAllDocumentsAsync()
    {
        try
        {
            return await _repository.GetAllDocumentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all documents");
            throw;
        }
    }

    public async Task<IEnumerable<Invoice>> GetAllInvoicesAsync()
    {
        try
        {
            return await _repository.GetAllInvoicesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all invoices");
            throw;
        }
    }

    public async Task<DocumentDetailsViewModel?> GetDocumentWithDetailsAsync(Guid documentId)
    {
        try
        {
            var document = await _repository.GetDocumentByIdAsync(documentId);
            if (document == null)
                return null;

            var details = await _repository.GetDocumentDetailsByDocumentIdAsync(documentId);

            return new DocumentDetailsViewModel
            {
                Document = document,
                Details = details.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document with details for {DocumentId}", documentId);
            throw;
        }
    }

    public async Task<InvoiceDetailsViewModel?> GetInvoiceWithDetailsAsync(Guid invoiceId)
    {
        try
        {
            var invoice = await _repository.GetInvoiceByIdAsync(invoiceId);
            if (invoice == null)
                return null;

            var details = await _repository.GetInvoiceDetailsByInvoiceIdAsync(invoiceId);

            return new InvoiceDetailsViewModel
            {
                Invoice = invoice,
                Details = details.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice with details for {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<IEnumerable<DocumentState>> GetDocumentStatesAsync()
    {
        try
        {
            return await _repository.GetAllDocumentStatesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document states");
            throw;
        }
    }

    public async Task<PurchaseInvoiceResult> CreatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, Guid userId)
    {
        try
        {
            // Validate the data
            var validationResult = await ValidatePurchaseInvoiceAsync(dto);
            if (!validationResult.IsValid)
            {
                return new PurchaseInvoiceResult
                {
                    Success = false,
                    Errors = validationResult.Errors
                };
            }

            // Create the purchase invoice
            var (documentId, invoiceId) = await _repository.CreatePurchaseInvoiceAsync(dto, userId);

            return new PurchaseInvoiceResult
            {
                Success = true,
                DocumentId = documentId,
                InvoiceId = invoiceId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating purchase invoice");
            return new PurchaseInvoiceResult
            {
                Success = false,
                Errors = new List<string> { "Eroare la crearea facturii de achiziție" }
            };
        }
    }

    public async Task<ValidationResult> ValidatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto)
    {
        var errors = new List<string>();

        // Validate document date
        if (dto.DocumentDate > DateTime.Now.AddDays(1))
        {
            errors.Add("Data documentului nu poate fi în viitor");
        }

        // Validate due date
        if (dto.DueDate.HasValue && dto.DueDate.Value < dto.DocumentDate)
        {
            errors.Add("Data scadenței nu poate fi înainte de data documentului");
        }

        // Validate document number uniqueness (simplified check)
        if (string.IsNullOrWhiteSpace(dto.DocumentNumber))
        {
            errors.Add("Numărul documentului este obligatoriu");
        }

        // Validate partner
        if (dto.PartnerId == Guid.Empty)
        {
            errors.Add("Partenerul este obligatoriu");
        }

        // Validate line items
        if (dto.LineItems == null || !dto.LineItems.Any())
        {
            errors.Add("Factura trebuie să conțină cel puțin o linie");
        }
        else
        {
            foreach (var item in dto.LineItems)
            {
                if (item.ItemId == Guid.Empty)
                {
                    errors.Add("Articolul este obligatoriu pentru fiecare linie");
                }
                if (item.Quantity <= 0)
                {
                    errors.Add("Cantitatea trebuie să fie mai mare decât 0");
                }
                if (item.UnitPrice < 0)
                {
                    errors.Add("Prețul unitar nu poate fi negativ");
                }
                if (item.VATRate < 0 || item.VATRate > 100)
                {
                    errors.Add("Cota TVA trebuie să fie între 0 și 100");
                }
            }
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors
        };
    }
}

/// <summary>
/// View model for document with details.
/// </summary>
public class DocumentDetailsViewModel
{
    public Document? Document { get; set; }
    public List<DocumentDetail> Details { get; set; } = new();
}

/// <summary>
/// View model for invoice with details.
/// </summary>
public class InvoiceDetailsViewModel
{
    public Invoice? Invoice { get; set; }
    public List<InvoiceDetail> Details { get; set; } = new();
}

/// <summary>
/// Result of purchase invoice creation.
/// </summary>
public class PurchaseInvoiceResult
{
    public bool Success { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? InvoiceId { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Validation result.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}