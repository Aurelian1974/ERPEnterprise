using System.Data;
using Dapper;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Implementare repository pentru locații.
/// </summary>
public class LocationRepository : ILocationRepository
{
    private readonly DapperContext _context;

    /// <summary>
    /// Constructor cu injectare DapperContext.
    /// </summary>
    public LocationRepository(DapperContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetAllAsync(bool includeInactive = false, Guid? companyId = null, Guid? workPlaceId = null)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<Location>(
            "sp_Locations_GetAll",
            new { IncludeInactive = includeInactive, CompanyId = companyId, WorkPlaceId = workPlaceId },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Location?> GetByIdAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Location>(
            "sp_Locations_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(Location location, Guid? createdBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Guid>(
            "sp_Locations_Create",
            new
            {
                location.CompanyId,
                location.WorkPlaceId,
                location.CodIntern,
                location.Denumire,
                TipLocatie = (byte)location.TipLocatie,
                location.Adresa,
                location.Localitate,
                location.Judet,
                location.CodPostal,
                RegimJuridic = (byte)location.RegimJuridic,
                location.HasStock,
                location.CanSell,
                location.CanPurchase,
                location.CanManufacture,
                location.Suprafata,
                location.Descriere,
                location.SortOrder,
                CreatedBy = createdBy
            },
            commandType: CommandType.StoredProcedure);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Location location, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Locations_Update",
            new
            {
                location.Id,
                location.WorkPlaceId,
                location.CodIntern,
                location.Denumire,
                TipLocatie = (byte)location.TipLocatie,
                location.Adresa,
                location.Localitate,
                location.Judet,
                location.CodPostal,
                RegimJuridic = (byte)location.RegimJuridic,
                location.HasStock,
                location.CanSell,
                location.CanPurchase,
                location.CanManufacture,
                location.Suprafata,
                location.Descriere,
                location.SortOrder,
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
            "sp_Locations_Delete",
            new { Id = id, UpdatedBy = updatedBy },
            commandType: CommandType.StoredProcedure);
        return result > 0;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetWithStockAsync(Guid companyId)
    {
        var locations = await GetAllAsync(false, companyId);
        return locations.Where(l => l.HasStock);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetForSaleAsync(Guid companyId)
    {
        var locations = await GetAllAsync(false, companyId);
        return locations.Where(l => l.CanSell);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodInternAsync(Guid companyId, string codIntern, Guid? excludeId = null)
    {
        using var connection = _context.CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT COUNT(1) FROM Locations WHERE CompanyId = @CompanyId AND CodIntern = @CodIntern AND Id <> @ExcludeId"
            : "SELECT COUNT(1) FROM Locations WHERE CompanyId = @CompanyId AND CodIntern = @CodIntern";
        
        var count = await connection.ExecuteScalarAsync<int>(sql, new { CompanyId = companyId, CodIntern = codIntern, ExcludeId = excludeId });
        return count > 0;
    }
}
