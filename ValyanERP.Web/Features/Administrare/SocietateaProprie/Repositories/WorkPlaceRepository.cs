using System.Data;
using Dapper;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Implementare repository pentru puncte de lucru.
/// </summary>
public class WorkPlaceRepository : IWorkPlaceRepository
{
    private readonly DapperContext _context;

    /// <summary>
    /// Constructor cu injectare DapperContext.
    /// </summary>
    public WorkPlaceRepository(DapperContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WorkPlace>> GetAllAsync(bool includeInactive = false, Guid? companyId = null)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<WorkPlace>(
            "sp_WorkPlaces_GetAll",
            new { IncludeInactive = includeInactive, CompanyId = companyId },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<WorkPlace?> GetByIdAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<WorkPlace>(
            "sp_WorkPlaces_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(WorkPlace workPlace, Guid? createdBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<Guid>(
            "sp_WorkPlaces_Create",
            new
            {
                workPlace.CompanyId,
                workPlace.Denumire,
                workPlace.CodONRC,
                TipPunctLucru = (byte)workPlace.TipPunctLucru,
                RegimJuridic = (byte)workPlace.RegimJuridic,
                workPlace.Adresa,
                workPlace.Localitate,
                workPlace.Judet,
                workPlace.CodPostal,
                workPlace.Tara,
                workPlace.Telefon,
                workPlace.Email,
                workPlace.EsteSediuSocial,
                workPlace.DataInregistrare,
                workPlace.SortOrder,
                CreatedBy = createdBy
            },
            commandType: CommandType.StoredProcedure);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(WorkPlace workPlace, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_WorkPlaces_Update",
            new
            {
                workPlace.Id,
                workPlace.Denumire,
                workPlace.CodONRC,
                TipPunctLucru = (byte)workPlace.TipPunctLucru,
                RegimJuridic = (byte)workPlace.RegimJuridic,
                workPlace.Adresa,
                workPlace.Localitate,
                workPlace.Judet,
                workPlace.CodPostal,
                workPlace.Tara,
                workPlace.Telefon,
                workPlace.Email,
                workPlace.EsteSediuSocial,
                workPlace.DataInregistrare,
                workPlace.DataRadiere,
                workPlace.SortOrder,
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
            "sp_WorkPlaces_Delete",
            new { Id = id, UpdatedBy = updatedBy },
            commandType: CommandType.StoredProcedure);
        return result > 0;
    }

    /// <inheritdoc />
    public async Task<WorkPlace?> GetSediuSocialAsync(Guid companyId)
    {
        using var connection = _context.CreateConnection();
        var workPlaces = await connection.QueryAsync<WorkPlace>(
            "sp_WorkPlaces_GetAll",
            new { IncludeInactive = false, CompanyId = companyId },
            commandType: CommandType.StoredProcedure);
        
        return workPlaces.FirstOrDefault(wp => wp.EsteSediuSocial);
    }
}
