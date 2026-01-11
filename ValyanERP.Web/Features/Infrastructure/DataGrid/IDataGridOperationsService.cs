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
    /// Applies server-side lazy load grouping and returns the appropriate object for Syncfusion.
    /// For lazy load grouping, returns DataResult when RequiresCounts is true, otherwise IEnumerable.
    /// </summary>
    /// <typeparam name="T">The type of entity in the data source.</typeparam>
    /// <param name="dataSource">The data source to group.</param>
    /// <param name="dm">The DataManagerRequest containing grouping information.</param>
    /// <param name="totalCount">The total count of records before grouping.</param>
    /// <returns>DataResult or IEnumerable based on RequiresCounts flag.</returns>
    object ApplyLazyLoadGrouping<T>(IEnumerable<T> dataSource, DataManagerRequest dm, int totalCount);

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

    /// <summary>
    /// Checks if the request is for lazy load grouping.
    /// </summary>
    /// <param name="dm">The DataManagerRequest to check.</param>
    /// <returns>True if lazy load grouping is requested.</returns>
    bool IsLazyLoadGrouping(DataManagerRequest dm);

    /// <summary>
    /// Applies server-side filtering to the data source.
    /// Used for lazy load group expand operations.
    /// </summary>
    /// <typeparam name="T">The type of entity in the data source.</typeparam>
    /// <param name="dataSource">The data source to filter.</param>
    /// <param name="dm">The DataManagerRequest containing filter conditions.</param>
    /// <returns>Filtered data source.</returns>
    IEnumerable<T> ApplyFiltering<T>(IEnumerable<T> dataSource, DataManagerRequest dm);
}
