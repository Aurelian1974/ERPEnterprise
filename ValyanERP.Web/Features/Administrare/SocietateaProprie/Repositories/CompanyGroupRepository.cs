using System.Data;
using Dapper;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Implementare repository pentru grupuri de companii.
/// </summary>
public class CompanyGroupRepository : ICompanyGroupRepository
{
    private readonly DapperContext _context;

    /// <summary>
    /// Constructor cu injectare DapperContext.
    /// </summary>
    public CompanyGroupRepository(DapperContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<CompanyGroup>> GetAllAsync(bool includeInactive = false)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<CompanyGroup>(
            "sp_CompanyGroups_GetAll",
            new { IncludeInactive = includeInactive },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<CompanyGroup?> GetByIdAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<CompanyGroup>(
            "sp_CompanyGroups_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(CompanyGroup group, Guid? createdBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Guid>(
            "sp_CompanyGroups_Create",
            new
            {
                group.Denumire,
                group.Descriere,
                group.LogoUrl,
                group.Website,
                group.SortOrder,
                CreatedBy = createdBy
            },
            commandType: CommandType.StoredProcedure);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(CompanyGroup group, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_CompanyGroups_Update",
            new
            {
                group.Id,
                group.Denumire,
                group.Descriere,
                group.LogoUrl,
                group.Website,
                group.SortOrder,
                UpdatedBy = updatedBy
            },
            commandType: CommandType.StoredProcedure);
        return result > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_CompanyGroups_Delete",
            new { Id = id, UpdatedBy = updatedBy },
            commandType: CommandType.StoredProcedure);
        return result > 0;
    }
}
