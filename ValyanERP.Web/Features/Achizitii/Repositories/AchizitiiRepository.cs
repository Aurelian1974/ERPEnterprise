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
            // Partner address selections
            parameters.Add("@PartnerAddressSediuId", dto.PartnerAddressSediuId);
            parameters.Add("@PartnerAddressCorespondentaId", dto.PartnerAddressCorespondentaId);
            parameters.Add("@PartnerAddressLivrareId", dto.PartnerAddressLivrareId);
            parameters.Add("@PartnerAddressFacturareId", dto.PartnerAddressFacturareId);
            // Partner contact and bank account
            parameters.Add("@PartnerContactId", dto.PartnerContactId);
            parameters.Add("@PartnerBankAccountId", dto.PartnerBankAccountId);

            // Execute the master stored procedure
            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_Document_InsertPurchaseInvoice",
                parameters,
                commandType: CommandType.StoredProcedure);

            var documentId = (Guid)result.DocumentId;
            var invoiceId = (Guid)result.InvoiceId;

            // Persist partner contact and bank account via helper proc (non-destructive)
            try
            {
                await connection.ExecuteAsync(
                    "sp_Invoice_SetPartnerContactAndBankAccount",
                    new
                    {
                        InvoiceId = invoiceId,
                        PartnerContactId = dto.PartnerContactId,
                        PartnerBankAccountId = dto.PartnerBankAccountId,
                        UpdatedBy = userId
                    },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set partner contact/bank account for Invoice {InvoiceId} via helper proc", invoiceId);
            }

            return (documentId, invoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating purchase invoice");
            throw;
        }
    }

    #endregion

    #region Document Validation Operations

    public async Task<bool> ValidateDocumentAsync(Guid documentId, Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();

            // Get user's current working location
            var userLocation = await GetUserCurrentLocationAsync(userId);
            
            var parameters = new DynamicParameters();
            parameters.Add("@DocumentId", documentId);
            parameters.Add("@UserId", userId);
            parameters.Add("@OwnerCompanyId", (Guid?)null);
            parameters.Add("@OwnerWorkPlaceId", (Guid?)null);
            parameters.Add("@OwnerLocationId", userLocation);

            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_Document_Validate",
                parameters,
                commandType: CommandType.StoredProcedure);

            return result.Success == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating document {DocumentId}", documentId);
            throw;
        }
    }

    public async Task<bool> UpdateInvoiceAsync(PurchaseInvoiceEditDto dto, Guid userId)
    {
        try
        {
            _logger.LogInformation("Updating invoice {DocumentId}", dto.DocumentId);

            using var connection = _context.CreateConnection();

            // Get invoice ID from document ID
            var invoiceId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                "SELECT Id FROM dbo.Invoice WHERE DocumentId = @DocumentId AND IsActive = 1",
                new { DocumentId = dto.DocumentId });

            if (invoiceId == null)
            {
                _logger.LogWarning("Invoice not found for document {DocumentId}", dto.DocumentId);
                return false;
            }

            // Get document state ID from code
            var documentStateId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                "SELECT Id FROM dbo.DocumentState WHERE CodStare = @CodStare AND IsActive = 1",
                new { CodStare = "C" }); // Default to draft for now

            if (documentStateId == null)
            {
                _logger.LogWarning("Document state not found");
                return false;
            }

            // Create table-valued parameter for line items
            var lineItemsTable = new DataTable();
            lineItemsTable.Columns.Add("ItemId", typeof(Guid));
            lineItemsTable.Columns.Add("Quantity", typeof(decimal));
            lineItemsTable.Columns.Add("UnitMeasure", typeof(string));
            lineItemsTable.Columns.Add("UnitPrice", typeof(decimal));
            lineItemsTable.Columns.Add("VATRate", typeof(decimal));

            foreach (var item in dto.LineItems)
            {
                lineItemsTable.Rows.Add(item.ItemId, item.Quantity, item.UnitMeasure, item.UnitPrice, item.VATRate);
            }

            var parameters = new
            {
                InvoiceId = invoiceId,
                DocumentDate = dto.DocumentDate,
                DueDate = dto.DueDate,
                DocumentNumber = dto.DocumentNumber,
                DocumentStateId = documentStateId,
                DocumentObservations = dto.DocumentObservations,
                PartnerId = dto.PartnerId,
                InvoiceObservations = dto.InvoiceObservations,
                PartnerAddressSediuId = dto.PartnerAddressSediuId,
                PartnerAddressCorespondentaId = dto.PartnerAddressCorespondentaId,
                PartnerAddressLivrareId = dto.PartnerAddressLivrareId,
                PartnerAddressFacturareId = dto.PartnerAddressFacturareId,
                PartnerContactId = dto.PartnerContactId,
                PartnerBankAccountId = dto.PartnerBankAccountId,
                LineItems = lineItemsTable.AsTableValuedParameter("dbo.InvoiceLineItemType"),
                UpdatedBy = userId,
                OwnerCompanyId = dto.OwnerCompanyId,
                OwnerWorkPlaceId = dto.OwnerWorkPlaceId,
                OwnerLocationId = dto.OwnerLocationId
            };

            var result = await connection.ExecuteAsync(
                "sp_Invoice_UpdateComplete",
                parameters,
                commandType: CommandType.StoredProcedure);

            _logger.LogInformation("Invoice {DocumentId} updated successfully", dto.DocumentId);
                // Ensure partner contact/bank account persisted via helper proc
                try
                {
                    await connection.ExecuteAsync(
                        "sp_Invoice_SetPartnerContactAndBankAccount",
                        new
                        {
                            InvoiceId = invoiceId,
                            PartnerContactId = dto.PartnerContactId,
                            PartnerBankAccountId = dto.PartnerBankAccountId,
                            UpdatedBy = userId
                        },
                        commandType: CommandType.StoredProcedure);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set partner contact/bank account for Invoice {InvoiceId} via helper proc", invoiceId);
                }

                return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating invoice {DocumentId}", dto.DocumentId);
            throw;
        }
    }

    #endregion

    #region User Context Operations

    public async Task<Guid?> GetUserCurrentLocationAsync(Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // Get the first location the user has access to
            // TODO: In the future, this should come from user's working context/session
            var locationId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                @"SELECT TOP 1 ua.EntityId 
                  FROM dbo.UserOrganizationalAccess ua 
                  WHERE ua.UserId = @UserId 
                  AND ua.EntityType = 'LOCATION' 
                  AND ua.IsActive = 1 
                  AND (ua.ValidTo IS NULL OR ua.ValidTo > GETDATE())
                  ORDER BY ua.CreatedAt",
                new { UserId = userId },
                commandType: CommandType.Text);

            return locationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user current location for user {UserId}", userId);
            return null;
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