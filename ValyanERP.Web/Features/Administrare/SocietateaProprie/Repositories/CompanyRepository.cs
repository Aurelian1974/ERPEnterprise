using System.Data;
using Dapper;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Implementare repository pentru companii.
/// </summary>
public class CompanyRepository : ICompanyRepository
{
    private readonly DapperContext _context;

    /// <summary>
    /// Constructor cu injectare DapperContext.
    /// </summary>
    public CompanyRepository(DapperContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Company>> GetAllAsync(bool includeInactive = false, Guid? groupId = null)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<Company>(
            "sp_Companies_GetAll",
            new { IncludeInactive = includeInactive, GroupId = groupId },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Company?> GetByIdAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "sp_Companies_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Company?> GetByCUIAsync(string cui)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "sp_Companies_GetByCUI",
            new { CUI = cui },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(Company company, Guid? createdBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Guid>(
            "sp_Companies_Create",
            new
            {
                company.CUI,
                company.Denumire,
                company.DenumireScurta,
                company.RegCom,
                company.GroupId,
                company.ParentCompanyId,
                TipCompanie = (byte)company.TipCompanie,
                company.ProcentDetinere,
                company.CapitalSocial,
                company.Telefon,
                company.Email,
                company.Website,
                company.LogoUrl,
                company.IsPrincipal,
                company.SortOrder,
                CreatedBy = createdBy
            },
            commandType: CommandType.StoredProcedure);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Company company, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Companies_Update",
            new
            {
                company.Id,
                company.CUI,
                company.Denumire,
                company.DenumireScurta,
                company.RegCom,
                company.GroupId,
                company.ParentCompanyId,
                TipCompanie = (byte)company.TipCompanie,
                company.ProcentDetinere,
                company.CapitalSocial,
                company.Telefon,
                company.Email,
                company.Website,
                company.LogoUrl,
                company.IsPrincipal,
                company.SortOrder,
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
            "sp_Companies_Delete",
            new { Id = id, UpdatedBy = updatedBy },
            commandType: CommandType.StoredProcedure);
        return result > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCUIAsync(string cui, Guid? excludeId = null)
    {
        using var connection = _context.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Companies WHERE CUI = @CUI AND Id <> @ExcludeId"
            : "SELECT COUNT(1) FROM Companies WHERE CUI = @CUI";
        
        var count = await connection.ExecuteScalarAsync<int>(sql, new { CUI = cui, ExcludeId = excludeId });
        return count > 0;
    }
}
