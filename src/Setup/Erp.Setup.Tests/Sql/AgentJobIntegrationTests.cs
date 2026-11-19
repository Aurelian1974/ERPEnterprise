using Erp.Setup.Sql;
using Erp.Setup.Sql.Agent;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>Pasii I-21 si I-22 pe SQL Server real cu SQL Server Agent (8.13, IA-15).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Criteriu", "08-IA-15")]
public sealed class AgentJobIntegrationTests(SqlServerFixture sql)
{
    private const string BackupRoot = "/var/opt/mssql/data/erp-backup-it";
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqlOperator Operator() => sql.Operator();

    private static BackupSettings Settings(string database) =>
        new(database, BackupRoot, new TimeOnly(1, 0), 15, 14, DayOfWeek.Sunday, new TimeOnly(3, 0));

    private async Task Deploy(string database, string instance)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(database, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(database, Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"), Ct);
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);
        var runner = new MigrationRunner(op.Log);
        runner.RunMigrations(op.ConnectionString(database), scripts);
        runner.RunProgrammability(op.ConnectionString(database), scripts);
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    private Task<AgentJobRun> Run(string job) => new AgentJobRunner(Operator()).RunAndWaitAsync(job, Ct, Poll, Timeout);

    [Fact]
    public async Task Jobs_are_created_in_the_erp_category_and_the_first_full_backup_succeeds()
    {
        const string database = "ERP_IT_JOBS";
        await Deploy(database, "ITJOBS");

        var changes = await new AgentJobInstaller(Operator()).EnsureAsync(Settings(database), Ct);

        Assert.All(changes.Values, c => Assert.Equal(AgentJobChange.Created, c));
        Assert.Equal(5, await Scalar<int>("msdb", $"""
            SELECT COUNT(*) FROM dbo.sysjobs AS j JOIN dbo.syscategories AS c ON c.category_id = j.category_id
            WHERE j.name LIKE N'ERP {database} - %' AND c.name = N'ERP' AND j.description LIKE N'ERP-JOB v1 |%'
              AND SUSER_SNAME(j.owner_sid) = SUSER_SNAME(0x01) AND j.notify_level_eventlog = 2 AND j.enabled = 1;
            """));
        Assert.Equal(15, await Scalar<int>("msdb", $"""
            SELECT s.freq_subday_interval FROM dbo.sysjobs AS j JOIN dbo.sysjobschedules AS js ON js.job_id = j.job_id
            JOIN dbo.sysschedules AS s ON s.schedule_id = js.schedule_id WHERE j.name = N'ERP {database} - Backup log';
            """));

        var full = await Run(AgentJobDefinitions.FullBackupJob(database));

        Assert.True(full.Succeeded, full.Message);
        Assert.Equal(1, await Scalar<int>("msdb", $"SELECT COUNT(*) FROM dbo.backupset WHERE database_name = N'{database}' AND type = 'D';"));
        Assert.EndsWith(".bak", await Scalar<string>("msdb", $"""
            SELECT TOP (1) f.physical_device_name FROM dbo.backupset AS b JOIN dbo.backupmediafamily AS f ON f.media_set_id = b.media_set_id
            WHERE b.database_name = N'{database}' ORDER BY b.backup_set_id DESC;
            """), StringComparison.Ordinal);

        // Backup-ul de log porneste lantul dupa primul backup complet.
        var log = await Run(AgentJobDefinitions.LogBackupJob(database));
        Assert.True(log.Succeeded, log.Message);
        Assert.Equal(1, await Scalar<int>("msdb", $"SELECT COUNT(*) FROM dbo.backupset WHERE database_name = N'{database}' AND type = 'L';"));

        foreach (var job in new[] { AgentJobDefinitions.CleanupJob(database), AgentJobDefinitions.IntegrityJob(database), AgentJobDefinitions.IndexMaintenanceJob(database) })
        {
            var run = await Run(job);
            Assert.True(run.Succeeded, run.Message);
        }
    }

    [Fact]
    public async Task Rerun_updates_only_schedules_and_an_older_definition_is_recreated()
    {
        const string database = "ERP_IT_JOBS2";
        await Deploy(database, "ITJOB2");
        var installer = new AgentJobInstaller(Operator());
        await installer.EnsureAsync(Settings(database), Ct);
        var jobId = await Scalar<Guid>("msdb", $"SELECT job_id FROM dbo.sysjobs WHERE name = N'ERP {database} - Backup complet';");

        var again = await installer.EnsureAsync(Settings(database) with { LogBackupEveryMinutes = 30 }, Ct);

        Assert.All(again.Values, c => Assert.Equal(AgentJobChange.ScheduleUpdated, c));
        Assert.Equal(jobId, await Scalar<Guid>("msdb", $"SELECT job_id FROM dbo.sysjobs WHERE name = N'ERP {database} - Backup complet';"));
        Assert.Equal(30, await Scalar<int>("msdb", $"""
            SELECT s.freq_subday_interval FROM dbo.sysjobs AS j JOIN dbo.sysjobschedules AS js ON js.job_id = j.job_id
            JOIN dbo.sysschedules AS s ON s.schedule_id = js.schedule_id WHERE j.name = N'ERP {database} - Backup log';
            """));
        Assert.Equal(5, await Scalar<int>("msdb", $"""
            SELECT COUNT(*) FROM dbo.sysjobschedules AS js JOIN dbo.sysjobs AS j ON j.job_id = js.job_id WHERE j.name LIKE N'ERP {database} - %';
            """));

        await sql.ExecuteAsync($"EXEC dbo.sp_update_job @job_name = N'ERP {database} - Backup complet', @description = N'ERP-JOB v0 | vechi';", "msdb");
        var upgraded = await installer.EnsureAsync(Settings(database), Ct);

        Assert.Equal(AgentJobChange.Recreated, upgraded[AgentJobDefinitions.FullBackupJob(database)]);
        Assert.NotEqual(jobId, await Scalar<Guid>("msdb", $"SELECT job_id FROM dbo.sysjobs WHERE name = N'ERP {database} - Backup complet';"));
        Assert.Equal(0, await Scalar<int>("msdb", $"SELECT COUNT(*) FROM dbo.sysschedules WHERE name = N'ERP {database} - Backup complet' AND schedule_id NOT IN (SELECT schedule_id FROM dbo.sysjobschedules);"));
    }

    [Fact]
    public async Task Failed_backup_is_reported_with_the_job_history_message()
    {
        const string database = "ERP_IT_JOBFAIL";
        await Deploy(database, "ITJOBF");
        await new AgentJobInstaller(Operator()).EnsureAsync(Settings(database), Ct);
        await sql.ExecuteAsync($"ALTER DATABASE [{database}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
        try
        {
            var run = await Run(AgentJobDefinitions.FullBackupJob(database));

            Assert.False(run.Succeeded);
            Assert.Contains(database, run.Message);
        }
        finally
        {
            await sql.ExecuteAsync($"ALTER DATABASE [{database}] SET ONLINE;");
        }
    }

    [Fact]
    public async Task Index_maintenance_rebuilds_fragmented_indexes()
    {
        const string database = "ERP_IT_MAINT";
        await Deploy(database, "ITMNT");
        await sql.ExecuteAsync("""
            CREATE TABLE core.MaintenanceProbe (Id uniqueidentifier NOT NULL CONSTRAINT PK_MaintenanceProbe PRIMARY KEY DEFAULT NEWID(), Filler char(400) NOT NULL);
            DECLARE @Batch int = 0;
            WHILE @Batch < 40     -- loturi separate: cheile aleatoare intercalate produc page split-uri
            BEGIN
                INSERT INTO core.MaintenanceProbe (Filler) SELECT TOP (1000) 'x' FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b;
                SET @Batch += 1;
            END;
            """, database);
        const string fragmentation = """
            SELECT CAST(MAX(s.avg_fragmentation_in_percent) AS int)
            FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID(N'core.MaintenanceProbe'), NULL, NULL, 'LIMITED') AS s
            WHERE s.index_id = 1;
            """;
        Assert.True(await Scalar<int>(database, fragmentation) > 30);

        await sql.ExecuteAsync("EXEC core.usp_Maintenance_IndexesAndStatistics;", database);

        Assert.True(await Scalar<int>(database, fragmentation) < 10);
    }

    [Fact]
    public async Task Operator_without_sysadmin_cannot_create_jobs_owned_by_sa()
    {
        const string login = "erp_it_dbcreator";
        const string password = "Dbcreator#Parola_2026x";
        await sql.ExecuteAsync($"""
            IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            ALTER SERVER ROLE dbcreator ADD MEMBER [{login}];
            ALTER SERVER ROLE securityadmin ADD MEMBER [{login}];
            """);
        var op = sql.Operator(new LoggerConfiguration().CreateLogger(), login, password);

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => new AgentJobInstaller(op).EnsureAsync(Settings("ERP_IT_NOSA"), Ct));

        Assert.Equal(Core.ExitCodes.RequirementsNotMet, ex.ExitCode);
        Assert.Contains("sysadmin", ex.Message);
    }
}
