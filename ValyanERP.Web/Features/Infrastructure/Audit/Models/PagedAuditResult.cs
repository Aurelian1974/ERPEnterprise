using System.Collections.Generic;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Models;

/// <summary>
/// Represents a paginated result set of audit logs.
/// </summary>
/// <typeparam name="T">Type of audit items (typically AuditLog)</typeparam>
public class PagedAuditResult<T>
{
    /// <summary>
    /// Current page items.
    /// </summary>
    public List<T> Items { get; set; } = new();
    
    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; set; }
    
    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; set; }
    
    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; }
    
    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => CurrentPage > 1;
    
    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage => CurrentPage < TotalPages;
}
