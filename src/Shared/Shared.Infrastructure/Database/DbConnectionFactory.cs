using Microsoft.Data.SqlClient;
using Shared.Kernel.Abstractions;
using System.Data;

namespace Shared.Infrastructure.Database;

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public IDbConnection Create()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
