using Dapper;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Repositories;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.TipuriDocumente.Repositories;

public class TipuriDocumenteRepository : ITipuriDocumenteRepository
{
    private readonly DapperContext _context;

    public TipuriDocumenteRepository(DapperContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TipDocument>> GetAllAsync()
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<TipDocument>(
            "sp_TipuriDocumente_GetAll",
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<TipDocument?> GetByIdAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<TipDocument>(
            "sp_TipuriDocumente_GetById",
            new { Id = id },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<int> CreateAsync(CreateTipDocumentDto dto)
    {
        using var connection = _context.CreateConnection();
        return await connection.ExecuteAsync(
            "sp_TipuriDocumente_Create",
            new
            {
                dto.TipDocumentCode,
                dto.TipDocumentName,
                dto.Description
            },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    public async Task<bool> UpdateAsync(UpdateTipDocumentDto dto)
    {
        using var connection = _context.CreateConnection();
        var affectedRows = await connection.ExecuteAsync(
            "sp_TipuriDocumente_Update",
            new
            {
                dto.Id,
                dto.TipDocumentCode,
                dto.TipDocumentName,
                dto.Description,
                dto.IsActive
            },
            commandType: System.Data.CommandType.StoredProcedure);
        return affectedRows > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var connection = _context.CreateConnection();
        var affectedRows = await connection.ExecuteAsync(
            "sp_TipuriDocumente_Delete",
            new { Id = id },
            commandType: System.Data.CommandType.StoredProcedure);
        return affectedRows > 0;
    }

    public async Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_TipuriDocumente_ExistsByCode",
            new { Code = code, ExcludeId = excludeId },
            commandType: System.Data.CommandType.StoredProcedure);
        return result > 0;
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
    {
        using var connection = _context.CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_TipuriDocumente_ExistsByName",
            new { Name = name, ExcludeId = excludeId },
            commandType: System.Data.CommandType.StoredProcedure);
        return result > 0;
    }
}