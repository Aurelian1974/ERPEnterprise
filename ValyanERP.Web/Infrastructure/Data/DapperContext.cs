using Microsoft.Data.SqlClient;
using System.Data;

namespace ValyanERP.Web.Infrastructure.Data;

/// <summary>
/// Factory for creating database connections using Dapper.
/// Use this for raw SQL queries alongside Entity Framework for Identity.
/// </summary>
public class DapperContext
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public DapperContext(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
