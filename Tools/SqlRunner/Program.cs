using System;
using System.Data;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(Path.Combine("ValyanERP.Web","appsettings.json"), optional: false, reloadOnChange: false);

var config = builder.Build();
var conn = config.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("Connection string 'DefaultConnection' not found in appsettings.json");
    return 2;
}

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project Tools/SqlRunner -- <sql-file-path>");
    return 1;
}

var mode = args[0];
if (mode == "--test-stoc")
{
    try
    {
        using var connection = new SqlConnection(conn);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sp_Rapoarte_Stoc_Get";
        using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        // Cache column names
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
        while (await reader.ReadAsync() && rowCount < 50)
        {
            // Print a few columns if present
            var articolId = columns.Contains("ArticolId") ? reader["ArticolId"]?.ToString() : string.Empty;
            var cod = columns.Contains("Cod") ? reader["Cod"]?.ToString() : string.Empty;
            var den = columns.Contains("Denumire") ? reader["Denumire"]?.ToString() : string.Empty;
            var cant = columns.Contains("Cantitate") ? reader["Cantitate"]?.ToString() : "0";
            var val = columns.Contains("Valoare") ? reader["Valoare"]?.ToString() : "0";
            Console.WriteLine($"{articolId} | {cod} | {den} | {cant} | {val}");
            rowCount++;
        }
        Console.WriteLine($"Displayed {rowCount} rows from sp_Rapoarte_Stoc_Get.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error executing sp_Rapoarte_Stoc_Get: {ex.Message}");
        Console.Error.WriteLine(ex.ToString());
        return 5;
    }
}
else
{
    var sqlFile = mode;
    if (!File.Exists(sqlFile))
    {
        Console.Error.WriteLine($"SQL file not found: {sqlFile}");
        return 3;
    }

    var sql = File.ReadAllText(sqlFile);

    try
    {
        using var connection = new SqlConnection(conn);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 0; // allow long scripts
        // Split on GO (at line boundaries)
        var batches = sql.Split(new[] { "\r\nGO\r\n", "\nGO\n", "\rGO\r" }, StringSplitOptions.RemoveEmptyEntries);
        int executed = 0;
        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            command.CommandText = trimmed;
            command.ExecuteNonQuery();
            executed++;
        }

        Console.WriteLine($"Executed {executed} batches from '{sqlFile}'.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error executing script: {ex.Message}");
        Console.Error.WriteLine(ex.ToString());
        return 4;
    }
}
