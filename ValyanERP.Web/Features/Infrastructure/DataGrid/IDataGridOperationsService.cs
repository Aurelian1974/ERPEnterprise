using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;

namespace ValyanERP.Web.Features.Infrastructure.DataGrid;

/// <summary>
/// Service interface for performing server-side DataGrid operations.
/// Provides reusable methods for grouping, sorting, filtering, and paging.
/// </summary>
/// <remarks>
/// This service is designed to be reused across all DataGrids in the application.
/// It abstracts Syncfusion DataOperations to provide a consistent implementation.
/// </remarks>
public interface IDataGridOperationsService
{
    /// <summary>
    /// Applies server-side grouping to the data source.
    /// </summary>
    /// <typeparam name="T">The type of entity in the data source.</typeparam>
    /// <param name="dataSource">The data source to group.</param>
    /// <param name="dm">The DataManagerRequest containing grouping information.</param>
    /// <param name="totalCount">The total count of records before grouping.</param>
    /// <returns>A DataResult with grouped data.</returns>
    DataResult ApplyGrouping<T>(IEnumerable<T> dataSource, DataManagerRequest dm, int totalCount);

    /// <summary>
    /// Applies all server-side operations (filtering, sorting, paging, grouping) to the data source.
    /// </summary>
    /// <typeparam name="T">The type of entity in the data source.</typeparam>
    /// <param name="dataSource">The data source to process.</param>
    /// <param name="dm">The DataManagerRequest containing operation parameters.</param>
    /// <returns>A DataResult with processed data.</returns>
    DataResult ApplyOperations<T>(IEnumerable<T> dataSource, DataManagerRequest dm);

    /// <summary>
    /// Checks if the request requires grouping.
    /// </summary>
    /// <param name="dm">The DataManagerRequest to check.</param>
    /// <returns>True if grouping is requested.</returns>
    bool RequiresGrouping(DataManagerRequest dm);
}
