using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Achizitii.Models;
using ValyanERP.Web.Features.Achizitii.Repositories;

namespace ValyanERP.Web.Features.Achizitii;

/// <summary>
/// Custom DataAdaptor for Achizitii (Purchase Invoices) Syncfusion DataGrid.
/// Handles server-side operations: paging, sorting, filtering, CRUD.
/// </summary>
public class AchizitiiAdaptor : DataAdaptor
{
    private readonly IAchizitiiRepository _repository;

    public AchizitiiAdaptor(IAchizitiiRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Handles read operations for documents.
    /// </summary>
    public override async Task<object> ReadAsync(DataManagerRequest dm, string key = null)
    {
        try
        {
            // Handle FilterChoiceRequest (for Excel filter dropdown)
            if (dm.RequiresCounts)
            {
                return await HandleFilterChoiceRequest(dm);
            }

            // Handle LazyLoad Grouping
            if (dm.Group != null && dm.Group.Any())
            {
                return await HandleLazyLoadGrouping(dm);
            }

            // Handle standard read with paging, sorting, filtering
            return await HandleStandardRead(dm);
        }
        catch (Exception ex)
        {
            // Log error and return empty result
            return new DataResult { Result = Array.Empty<Document>(), Count = 0 };
        }
    }

    /// <summary>
    /// Handles insert operations.
    /// </summary>
    public override async Task<object> InsertAsync(DataManager dm, object value, string key)
    {
        // For now, insert is handled through the service
        // This would be implemented if we add inline editing
        return value;
    }

    /// <summary>
    /// Handles update operations.
    /// </summary>
    public override async Task<object> UpdateAsync(DataManager dm, object value, string keyField, string key)
    {
        // For now, update is handled through the service
        // This would be implemented if we add inline editing
        return value;
    }

    /// <summary>
    /// Handles delete operations.
    /// </summary>
    public override async Task<object> RemoveAsync(DataManager dm, object value, string keyField, string key)
    {
        // For now, delete is handled through the service
        // This would be implemented if we add delete functionality
        return value;
    }

    private async Task<object> HandleFilterChoiceRequest(DataManagerRequest dm)
    {
        // Get all data for filter dropdowns
        var allData = await _repository.GetAllDocumentsAsync();
        return new DataResult { Result = allData, Count = allData.Count() };
    }

    private async Task<object> HandleLazyLoadGrouping(DataManagerRequest dm)
    {
        // For lazy load grouping, get all data and let Syncfusion handle grouping
        var allData = await _repository.GetAllDocumentsAsync();
        return new DataResult { Result = allData, Count = allData.Count() };
    }

    private async Task<object> HandleStandardRead(DataManagerRequest dm)
    {
        // Use paged query for performance
        var result = await _repository.GetDocumentsPagedAsync(dm);
        return result;
    }
}