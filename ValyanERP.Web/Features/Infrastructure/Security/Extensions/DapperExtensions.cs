using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace ValyanERP.Web.Features.Infrastructure.Security.Extensions;

/// <summary>
/// Extension methods for Dapper to support organizational security features.
/// Includes Table-Valued Parameter (TVP) helpers and security filter builders.
/// </summary>
public static class DapperExtensions
{
    /// <summary>
    /// Converts a collection of GUIDs to a SQL Server Table-Valued Parameter.
    /// Uses the GuidList type defined in the database.
    /// </summary>
    /// <param name="guids">Collection of GUIDs to convert.</param>
    /// <param name="typeName">The TVP type name (default: dbo.GuidList).</param>
    /// <returns>A parameter that can be passed to Dapper queries.</returns>
    /// <example>
    /// <code>
    /// var companyIds = new List&lt;Guid&gt; { guid1, guid2 };
    /// await connection.QueryAsync&lt;Partner&gt;(
    ///     "SELECT * FROM Partners WHERE OwnerCompanyId IN (SELECT Id FROM @CompanyIds)",
    ///     new { CompanyIds = companyIds.AsGuidTableValuedParameter() });
    /// </code>
    /// </example>
    public static SqlMapper.ICustomQueryParameter AsGuidTableValuedParameter(
        this IEnumerable<Guid> guids,
        string typeName = "dbo.GuidList")
    {
        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(Guid));
        
        foreach (var guid in guids)
        {
            dataTable.Rows.Add(guid);
        }
        
        return dataTable.AsTableValuedParameter(typeName);
    }
    
    /// <summary>
    /// Creates a security filter SQL fragment for company-based filtering.
    /// </summary>
    /// <param name="tableAlias">Table alias (e.g., "p" for Partners).</param>
    /// <param name="ownerColumn">Column name containing the owner company ID.</param>
    /// <param name="createdByColumn">Column name containing the creator user ID.</param>
    /// <returns>SQL WHERE clause fragment.</returns>
    /// <example>
    /// <code>
    /// var filter = DapperExtensions.BuildCompanySecurityFilter("p");
    /// // Returns: "(p.OwnerCompanyId IN (SELECT CompanyId FROM fn_GetUserVisibleCompanies(@CurrentUserId)) OR p.CreatedBy = @CurrentUserId)"
    /// </code>
    /// </example>
    public static string BuildCompanySecurityFilter(
        string tableAlias = "",
        string ownerColumn = "OwnerCompanyId",
        string createdByColumn = "CreatedBy")
    {
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : $"{tableAlias}.";
        
        return $@"({prefix}{ownerColumn} IN (
            SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@CurrentUserId)
        ) OR {prefix}{createdByColumn} = @CurrentUserId)";
    }
    
    /// <summary>
    /// Creates a security filter SQL fragment using SESSION_CONTEXT.
    /// For use with filtered views.
    /// </summary>
    /// <param name="tableAlias">Table alias.</param>
    /// <param name="ownerColumn">Column containing owner company ID.</param>
    /// <returns>SQL WHERE clause fragment using SESSION_CONTEXT.</returns>
    public static string BuildSessionContextFilter(
        string tableAlias = "",
        string ownerColumn = "OwnerCompanyId")
    {
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : $"{tableAlias}.";
        
        return $@"(
            CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
            OR {prefix}{ownerColumn} IN (
                SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(
                    CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
                )
            )
        )";
    }
    
    /// <summary>
    /// Appends a company filter to an existing SQL query.
    /// </summary>
    /// <param name="sql">Base SQL query (should end with WHERE or AND).</param>
    /// <param name="tableAlias">Table alias for the filter.</param>
    /// <param name="ownerColumn">Column name for company ownership.</param>
    /// <returns>SQL with appended filter.</returns>
    public static string WithCompanyFilter(
        this string sql,
        string tableAlias = "",
        string ownerColumn = "OwnerCompanyId")
    {
        var filter = BuildCompanySecurityFilter(tableAlias, ownerColumn);
        return $"{sql} AND {filter}";
    }
    
    /// <summary>
    /// Creates parameters object with visible company IDs included.
    /// </summary>
    /// <param name="visibleCompanyIds">Collection of visible company IDs.</param>
    /// <param name="currentUserId">Current user's ID.</param>
    /// <param name="additionalParams">Additional parameters to include.</param>
    /// <returns>Anonymous object for Dapper parameters.</returns>
    public static object WithSecurityParams(
        IEnumerable<Guid> visibleCompanyIds,
        Guid currentUserId,
        object? additionalParams = null)
    {
        var securityParams = new Dictionary<string, object>
        {
            { "VisibleCompanyIds", visibleCompanyIds.AsGuidTableValuedParameter() },
            { "CurrentUserId", currentUserId }
        };
        
        if (additionalParams != null)
        {
            foreach (var prop in additionalParams.GetType().GetProperties())
            {
                var value = prop.GetValue(additionalParams);
                if (value != null)
                {
                    securityParams[prop.Name] = value;
                }
            }
        }
        
        return new DynamicParameters(securityParams);
    }
}
