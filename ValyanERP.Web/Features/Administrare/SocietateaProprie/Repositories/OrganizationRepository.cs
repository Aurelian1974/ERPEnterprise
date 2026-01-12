using System.Data;
using Dapper;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Implementare repository pentru structura organizațională.
/// </summary>
public class OrganizationRepository : IOrganizationRepository
{
    private readonly DapperContext _context;

    /// <summary>
    /// Constructor cu injectare DapperContext.
    /// </summary>
    public OrganizationRepository(DapperContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<OrganizationTreeNode>> GetTreeAsync()
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryAsync<OrganizationTreeNode>(
            "sp_Organization_GetTree",
            commandType: CommandType.StoredProcedure);
    }
}
