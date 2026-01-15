using Dapper;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System.Data;
using System.Text;
using System.Xml.Linq;
using ValyanERP.Web.Features.Achizitii.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Achizitii.Repositories;

/// <summary>
/// Repository for Achizitii (Purchase Invoices) data access using stored procedures.
/// All operations use parameterized queries to prevent SQL injection.
/// </summary>
public class AchizitiiRepository : IAchizitiiRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<AchizitiiRepository> _logger;

    public AchizitiiRepository(DapperContext context, ILogger<AchizitiiRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Document Operations

    public async Task<DataResult> GetDocumentsPagedAsync(DataManagerRequest dm)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();

            // Extract search term
            string? searchTerm = null;
            if (dm.Search != null && dm.Search.Count > 0)
            {
                searchTerm = dm.Search[0].Key;
                parameters.Add("@SearchTerm", searchTerm);
            }

            // Add sorting
            var orderBy = BuildOrderByClause(dm.Sorted);
            parameters.Add("@OrderBy", orderBy);

            // Add paging
            parameters.Add("@PageNumber", dm.Skip / dm.Take + 1);
            parameters.Add("@PageSize", dm.Take);

            // Execute stored procedure
            var result = await connection.QueryMultipleAsync(
                "sp_Document_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var documents = await result.ReadAsync<Document>();
            var totalCount = await result.ReadSingleAsync<int>();

            return new DataResult
            {
                Result = documents,
                Count = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged documents");
            throw;
        }
    }

    public async Task<IEnumerable<Document>> GetAllDocumentsAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<Document>(
                "sp_Document_GetAll",
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all documents");
            throw;
        }
    }

    public async Task<Document?> GetDocumentByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<Document>(
                "sp_Document_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document by id {Id}", id);
            throw;
        }
    }

    #endregion

    #region Invoice Operations

    public async Task<DataResult> GetInvoicesPagedAsync(DataManagerRequest dm)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();

            // Extract search term
            string? searchTerm = null;
            if (dm.Search != null && dm.Search.Count > 0)
            {
                searchTerm = dm.Search[0].Key;
                parameters.Add("@SearchTerm", searchTerm);
            }

            // Add sorting
            var orderBy = BuildOrderByClause(dm.Sorted);
            parameters.Add("@OrderBy", orderBy);

            // Add paging
            parameters.Add("@PageNumber", dm.Skip / dm.Take + 1);
            parameters.Add("@PageSize", dm.Take);

            // Execute stored procedure
            var result = await connection.QueryMultipleAsync(
                "sp_Invoice_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var invoices = await result.ReadAsync<Invoice>();
            var totalCount = await result.ReadSingleAsync<int>();

            return new DataResult
            {
                Result = invoices,
                Count = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged invoices");
            throw;
        }
    }

    public async Task<IEnumerable<Invoice>> GetAllInvoicesAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<Invoice>(
                "sp_Invoice_GetAll",
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all invoices");
            throw;
        }
    }

    public async Task<Invoice?> GetInvoiceByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<Invoice>(
                "sp_Invoice_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice by id {Id}", id);
            throw;
        }
    }

    #endregion

    #region DocumentDetail Operations

    public async Task<IEnumerable<DocumentDetail>> GetDocumentDetailsByDocumentIdAsync(Guid documentId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<DocumentDetail>(
                "sp_DocumentDetail_GetByDocumentId",
                new { DocumentId = documentId },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document details for document {DocumentId}", documentId);
            throw;
        }
    }

    #endregion

    #region InvoiceDetail Operations

    public async Task<IEnumerable<InvoiceDetail>> GetInvoiceDetailsByInvoiceIdAsync(Guid invoiceId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<InvoiceDetail>(
                "sp_InvoiceDetail_GetByInvoiceId",
                new { InvoiceId = invoiceId },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice details for invoice {InvoiceId}", invoiceId);
            throw;
        }
    }

    #endregion

    #region DocumentState Operations

    public async Task<IEnumerable<DocumentState>> GetAllDocumentStatesAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<DocumentState>(
                "sp_DocumentState_GetAll",
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all document states");
            throw;
        }
    }

    #endregion

    #region Purchase Invoice Operations

    public async Task<(Guid DocumentId, Guid InvoiceId)> CreatePurchaseInvoiceAsync(PurchaseInvoiceCreateDto dto, Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();

            // Build XML for line items
            var lineItemsXml = BuildLineItemsXml(dto.LineItems);

            var parameters = new DynamicParameters();
            parameters.Add("@DocumentDate", dto.DocumentDate);
            parameters.Add("@DueDate", dto.DueDate);
            parameters.Add("@DocumentNumber", dto.DocumentNumber);
            parameters.Add("@DocumentTypeCode", "FFA");
            parameters.Add("@DocumentStateCode", dto.DocumentStateCode);
            parameters.Add("@Observations", dto.DocumentObservations);
            parameters.Add("@UserId", userId);
            parameters.Add("@EntityIntroduced", (Guid?)null);
            parameters.Add("@PartnerId", dto.PartnerId);
            parameters.Add("@InvoiceObservations", dto.InvoiceObservations);
            parameters.Add("@OwnerCompanyId", dto.OwnerCompanyId);
            parameters.Add("@OwnerWorkPlaceId", dto.OwnerWorkPlaceId);
            parameters.Add("@OwnerLocationId", dto.OwnerLocationId);
            parameters.Add("@LineItems", lineItemsXml);

            // Execute the master stored procedure
            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_Document_InsertPurchaseInvoice",
                parameters,
                commandType: CommandType.StoredProcedure);

            return ((Guid)result.DocumentId, (Guid)result.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating purchase invoice");
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private string BuildOrderByClause(List<Sort> sorted)
    {
        if (sorted == null || sorted.Count == 0)
            return "CreatedAt DESC";

        var orderByParts = sorted.Select(s =>
            $"{s.Name} {(s.Direction == "descending" ? "DESC" : "ASC")}");

        return string.Join(", ", orderByParts);
    }

    private string BuildLineItemsXml(List<InvoiceLineItemDto> lineItems)
    {
        var xml = new XElement("LineItems",
            lineItems.Select(item =>
                new XElement("Item",
                    new XElement("ItemId", item.ItemId),
                    new XElement("Quantity", item.Quantity),
                    new XElement("UnitMeasure", item.UnitMeasure),
                    new XElement("UnitPrice", item.UnitPrice),
                    new XElement("VATRate", item.VATRate)
                )
            )
        );

        return xml.ToString();
    }

    #endregion
}