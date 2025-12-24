using Dapper;
using System.Data;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Persoane.Repositories;

/// <summary>
/// Repository implementation for Persoane using Dapper and stored procedures.
/// </summary>
public class PersoaneRepository : IPersoaneRepository
{
    private readonly DapperContext _context;

    public PersoaneRepository(DapperContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Persoana>> GetAllAsync(bool includeInactive = false)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@IncludeInactive", includeInactive);
        
        return await connection.QueryAsync<Persoana>(
            "sp_Persoane_GetAll",
            parameters,
            commandType: CommandType.StoredProcedure);
    }

    public async Task<Persoana?> GetByIdAsync(int id)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@Id", id);
        
        return await connection.QueryFirstOrDefaultAsync<Persoana>(
            "sp_Persoane_GetById",
            parameters,
            commandType: CommandType.StoredProcedure);
    }

    public async Task<int> CreateAsync(CreatePersoanaDto persoana, Guid? createdBy = null)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@Nume", persoana.Nume);
        parameters.Add("@Prenume", persoana.Prenume);
        parameters.Add("@CNP", persoana.CNP);
        parameters.Add("@DataNasterii", persoana.DataNasterii);
        parameters.Add("@Email", persoana.Email);
        parameters.Add("@Telefon", persoana.Telefon);
        parameters.Add("@Adresa", persoana.Adresa);
        parameters.Add("@Oras", persoana.Oras);
        parameters.Add("@Judet", persoana.Judet);
        parameters.Add("@CodPostal", persoana.CodPostal);
        parameters.Add("@Tara", persoana.Tara ?? "Romania");
        parameters.Add("@CreatedBy", createdBy);
        
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Persoane_Create",
            parameters,
            commandType: CommandType.StoredProcedure);
        
        return result;
    }

    public async Task<bool> UpdateAsync(UpdatePersoanaDto persoana, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@Id", persoana.Id);
        parameters.Add("@Nume", persoana.Nume);
        parameters.Add("@Prenume", persoana.Prenume);
        parameters.Add("@CNP", persoana.CNP);
        parameters.Add("@DataNasterii", persoana.DataNasterii);
        parameters.Add("@Email", persoana.Email);
        parameters.Add("@Telefon", persoana.Telefon);
        parameters.Add("@Adresa", persoana.Adresa);
        parameters.Add("@Oras", persoana.Oras);
        parameters.Add("@Judet", persoana.Judet);
        parameters.Add("@CodPostal", persoana.CodPostal);
        parameters.Add("@Tara", persoana.Tara ?? "Romania");
        parameters.Add("@IsActive", persoana.IsActive);
        parameters.Add("@UpdatedBy", updatedBy);
        
        var rowsAffected = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Persoane_Update",
            parameters,
            commandType: CommandType.StoredProcedure);
        
        return rowsAffected > 0;
    }

    public async Task<bool> DeleteAsync(int id, Guid? updatedBy = null)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@Id", id);
        parameters.Add("@UpdatedBy", updatedBy);
        
        var rowsAffected = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Persoane_Delete",
            parameters,
            commandType: CommandType.StoredProcedure);
        
        return rowsAffected > 0;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@Id", id);
        
        var rowsAffected = await connection.QueryFirstOrDefaultAsync<int>(
            "sp_Persoane_HardDelete",
            parameters,
            commandType: CommandType.StoredProcedure);
        
        return rowsAffected > 0;
    }

    public async Task<IEnumerable<Persoana>> SearchAsync(string? searchTerm, bool includeInactive = false)
    {
        using var connection = _context.CreateConnection();
        
        var parameters = new DynamicParameters();
        parameters.Add("@SearchTerm", searchTerm);
        parameters.Add("@IncludeInactive", includeInactive);
        
        return await connection.QueryAsync<Persoana>(
            "sp_Persoane_Search",
            parameters,
            commandType: CommandType.StoredProcedure);
    }
}
