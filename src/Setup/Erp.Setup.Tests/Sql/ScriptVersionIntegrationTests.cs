using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// Jurnalul versiunilor scripturilor (08, 5.9) pe SQL Server real: un rand la fiecare continut nou al unui script, cu versiunea
/// din manifest; rularea repetata a unui script neschimbat nu adauga nimic; un script esuat nu are rand.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ScriptVersionIntegrationTests(SqlServerFixture sql)
{
    private const string Procedure = "core/Procedures/core.usp_Probe_Get.sql";
    private const string Migration = "core/2026.11.001_Create_Probe.sql";
    private const string Seed = "core/01_Probe.sql";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ILogger Log => new LoggerConfiguration().CreateLogger();

    private SqlOperator Operator() => sql.Operator(Log);

    private async Task<string> NewDatabase(string name)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(name, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(name, TestPaths.BootstrapDirectory(), Ct);
        return name;
    }

    private static TempKitDatabase Kit(string procedureBody) => new TempKitDatabase()
        .Add("migrations/core/2026.11.001_Create_Probe.sql", "CREATE TABLE core.Probe (Id int NOT NULL CONSTRAINT PK_Probe PRIMARY KEY);")
        .Add("programmability/core/Procedures/core.usp_Probe_Get.sql", $"CREATE OR ALTER PROCEDURE core.usp_Probe_Get AS SELECT {procedureBody} AS Value;")
        .Add("seed/core/01_Probe.sql", "MERGE core.Probe AS t USING (VALUES (1)) AS s (Id) ON t.Id = s.Id WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.Id);");

    private void Deploy(string database, TempKitDatabase kit, ScriptVersions versions)
    {
        var runner = new MigrationRunner(Log, versions);
        var connection = Operator().ConnectionString(database);
        var scripts = KitScripts.Load(kit.Root);
        runner.RunMigrations(connection, scripts);
        runner.RunProgrammability(connection, scripts);
        runner.RunSeed(connection, scripts);
    }

    private static ScriptVersions Versions(string product, int procedureVersion) => new(
        new Dictionary<string, int>(StringComparer.Ordinal) { [Migration] = 1, [Procedure] = procedureVersion, [Seed] = 1 }, product);

    private async Task<IReadOnlyList<(string Name, byte Kind, int Version, string Product)>> Rows(string database)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand("SELECT ScriptName, ScriptKind, Version, ProductVersion FROM core.ScriptVersion ORDER BY Id;", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(string, byte, int, string)>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), reader.GetByte(1), reader.GetInt32(2), reader.GetString(3)));
        }

        return rows;
    }

    private async Task<int?> CurrentVersion(string database, string script)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand("SELECT Version FROM core.v_ScriptVersionCurrent WHERE ScriptName = @Name;", connection);
        command.Parameters.AddWithValue("@Name", script);
        return await command.ExecuteScalarAsync(Ct) as int?;
    }

    [Fact]
    public async Task Installing_records_every_script_once_with_its_manifest_version_and_a_rerun_records_nothing()
    {
        var database = await NewDatabase("ERP_IT_SV_INSTALL");
        using var kit = Kit("1");

        Deploy(database, kit, Versions("2027.1.0", 1));
        Deploy(database, kit, Versions("2027.1.0", 1));

        Assert.Equal(
            [(Migration, (byte)ScriptKind.Migration, 1, "2027.1.0"), (Procedure, (byte)ScriptKind.Programmability, 1, "2027.1.0"), (Seed, (byte)ScriptKind.Seed, 1, "2027.1.0")],
            await Rows(database));
    }

    [Fact]
    public async Task An_upgrade_records_only_the_changed_procedure_with_its_new_version()
    {
        var database = await NewDatabase("ERP_IT_SV_UPGRADE");
        using (var first = Kit("1"))
        {
            Deploy(database, first, Versions("2027.1.0", 1));
        }

        using var second = Kit("2");
        Deploy(database, second, Versions("2027.2.0", 2));

        var rows = await Rows(database);
        Assert.Equal(4, rows.Count);
        Assert.Equal((Procedure, (byte)ScriptKind.Programmability, 2, "2027.2.0"), rows[^1]);
        Assert.Equal(2, await CurrentVersion(database, Procedure));
        Assert.Equal(1, await CurrentVersion(database, Seed));
    }

    [Fact]
    public async Task Reformatting_a_script_does_not_create_a_new_version()
    {
        var database = await NewDatabase("ERP_IT_SV_FORMAT");
        using (var first = Kit("1"))
        {
            Deploy(database, first, Versions("2027.1.0", 1));
        }

        using var reformatted = new TempKitDatabase()
            .Add("migrations/core/2026.11.001_Create_Probe.sql", "CREATE TABLE core.Probe (Id int NOT NULL CONSTRAINT PK_Probe PRIMARY KEY);")
            .Add("programmability/core/Procedures/core.usp_Probe_Get.sql", "CREATE OR ALTER PROCEDURE core.usp_Probe_Get AS SELECT 1 AS Value;   \r\n\r\n")
            .Add("seed/core/01_Probe.sql", "MERGE core.Probe AS t USING (VALUES (1)) AS s (Id) ON t.Id = s.Id WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.Id);");
        Deploy(database, reformatted, Versions("2027.1.0", 1));

        Assert.Equal(3, (await Rows(database)).Count);
    }

    [Fact]
    public async Task A_failing_script_gets_no_version_row()
    {
        var database = await NewDatabase("ERP_IT_SV_FAIL");
        using (var first = Kit("1"))
        {
            Deploy(database, first, Versions("2027.1.0", 1));
        }

        // O coloana inexistenta intr-o tabela existenta pica la creare (o tabela inexistenta n-ar pica: rezolvare amanata).
        using var broken = Kit("NoSuchColumn FROM core.Probe");
        Assert.Throws<SqlSetupException>(() => Deploy(database, broken, Versions("2027.2.0", 2)));

        Assert.Equal(1, await CurrentVersion(database, Procedure));
        Assert.Equal(3, (await Rows(database)).Count);
    }
}
