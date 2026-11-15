using System.Data;
using Erp.Setup.Core;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Agent;

public enum AgentJobChange
{
    Created,
    Recreated,
    ScheduleUpdated,
}

/// <summary>
/// Pasul I-21 (8.13): creeaza folderele de backup si joburile in categoria ERP, cu proprietarul SUSER_SNAME(0x01) si
/// esecurile in Event Log. La repair/upgrade un job se recreeaza doar daca definitia din kit e mai noua; altfel se
/// actualizeaza doar programarea (R8).
/// </summary>
public sealed class AgentJobInstaller(SqlOperator sql)
{
    public async Task<IReadOnlyDictionary<string, AgentJobChange>> EnsureAsync(BackupSettings settings, CancellationToken ct)
    {
        var definitions = AgentJobDefinitions.For(settings);
        await using var msdb = await sql.OpenAsync("msdb", ct).ConfigureAwait(false);

        if (await SqlOperator.ScalarAsync<int>(msdb, "SELECT IS_SRVROLEMEMBER(N'sysadmin');", ct).ConfigureAwait(false) != 1)
        {
            throw new SqlSetupException(
                "Joburile SQL Agent cu proprietarul sa se pot crea doar de un operator cu rolul sysadmin (8.13).", ExitCodes.RequirementsNotMet);
        }

        foreach (var kind in new[] { "FULL", "LOG", "UPGRADE" })
        {
            var folder = AgentJobDefinitions.BackupFolder(settings, kind);
            await sql.ExecuteAsync(msdb, $"Folderul de backup {folder}", "EXEC master.dbo.xp_create_subdir @Path;", ct, Text("@Path", folder, 400)).ConfigureAwait(false);
        }

        await sql.ExecuteAsync(msdb, "Categoria de joburi ERP", """
            IF NOT EXISTS (SELECT 1 FROM dbo.syscategories WHERE name = @Category AND category_class = 1)
                EXEC dbo.sp_add_category @class = N'JOB', @type = N'LOCAL', @name = @Category;
            """, ct, Text("@Category", AgentJobDefinitions.Category, 128)).ConfigureAwait(false);

        var owner = await SqlOperator.ScalarAsync<string>(msdb, "SELECT SUSER_SNAME(0x01);", ct).ConfigureAwait(false)
            ?? throw new SqlSetupException("Contul sa (SID 0x01) nu a fost găsit.");

        var changes = new Dictionary<string, AgentJobChange>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            changes[definition.Name] = await EnsureJobAsync(msdb, definition, owner, ct).ConfigureAwait(false);
        }

        return changes;
    }

    private async Task<AgentJobChange> EnsureJobAsync(SqlConnection msdb, AgentJobDefinition definition, string owner, CancellationToken ct)
    {
        var description = await SqlOperator.ScalarAsync<string>(msdb, "SELECT description FROM dbo.sysjobs WHERE name = @Name;", ct,
            Text("@Name", definition.Name, 128)).ConfigureAwait(false);
        var exists = await SqlOperator.ScalarAsync<int>(msdb, "SELECT COUNT(*) FROM dbo.sysjobs WHERE name = @Name;", ct,
            Text("@Name", definition.Name, 128)).ConfigureAwait(false) > 0;

        if (exists && (AgentJobDefinitions.ParseVersion(description) ?? 0) >= AgentJobDefinitions.DefinitionVersion)
        {
            await UpdateScheduleAsync(msdb, definition, ct).ConfigureAwait(false);
            return AgentJobChange.ScheduleUpdated;
        }

        if (exists)
        {
            await sql.ExecuteAsync(msdb, $"Ștergere job vechi {definition.Name}", "EXEC dbo.sp_delete_job @job_name = @Name, @delete_unused_schedule = 1;", ct,
                Text("@Name", definition.Name, 128)).ConfigureAwait(false);
        }

        await CreateAsync(msdb, definition, owner, ct).ConfigureAwait(false);
        return exists ? AgentJobChange.Recreated : AgentJobChange.Created;
    }

    private async Task CreateAsync(SqlConnection msdb, AgentJobDefinition definition, string owner, CancellationToken ct)
    {
        const string create = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @ScheduleId int;
            EXEC dbo.sp_add_job @job_name = @Name, @enabled = 1, @description = @Description, @category_name = @Category,
                @owner_login_name = @Owner, @notify_level_eventlog = 2;
            EXEC dbo.sp_add_jobstep @job_name = @Name, @step_name = @StepName, @subsystem = N'TSQL', @database_name = @StepDatabase,
                @command = @Command, @on_success_action = 1, @on_fail_action = 2;
            EXEC dbo.sp_add_schedule @schedule_name = @Name, @enabled = 1, @freq_type = @FreqType, @freq_interval = @FreqInterval,
                @freq_subday_type = @FreqSubdayType, @freq_subday_interval = @FreqSubdayInterval,
                @freq_recurrence_factor = @FreqRecurrenceFactor, @active_start_time = @ActiveStartTime, @schedule_id = @ScheduleId OUTPUT;
            EXEC dbo.sp_attach_schedule @job_name = @Name, @schedule_id = @ScheduleId;
            EXEC dbo.sp_add_jobserver @job_name = @Name, @server_name = N'(local)';
            COMMIT TRANSACTION;
            """;
        await sql.ExecuteAsync(msdb, $"Creare job {definition.Name}", create, ct,
        [
            Text("@Name", definition.Name, 128),
            Text("@Description", definition.Description, 512),
            Text("@Category", AgentJobDefinitions.Category, 128),
            Text("@Owner", owner, 128),
            Text("@StepName", definition.StepName, 128),
            Text("@StepDatabase", definition.StepDatabase, 128),
            Text("@Command", definition.Command, -1),
            .. ScheduleParameters(definition.Schedule),
        ]).ConfigureAwait(false);
    }

    private Task UpdateScheduleAsync(SqlConnection msdb, AgentJobDefinition definition, CancellationToken ct)
    {
        const string update = """
            DECLARE @ScheduleId int = (
                SELECT TOP (1) s.schedule_id
                FROM dbo.sysjobs AS j
                JOIN dbo.sysjobschedules AS s ON s.job_id = j.job_id
                WHERE j.name = @Name);
            IF @ScheduleId IS NULL
                THROW 50000, N'Jobul nu are programare.', 1;
            EXEC dbo.sp_update_schedule @schedule_id = @ScheduleId, @enabled = 1, @freq_type = @FreqType, @freq_interval = @FreqInterval,
                @freq_subday_type = @FreqSubdayType, @freq_subday_interval = @FreqSubdayInterval,
                @freq_recurrence_factor = @FreqRecurrenceFactor, @active_start_time = @ActiveStartTime;
            """;
        return sql.ExecuteAsync(msdb, $"Programarea jobului {definition.Name}", update, ct,
            [Text("@Name", definition.Name, 128), .. ScheduleParameters(definition.Schedule)]);
    }

    private static SqlParameter[] ScheduleParameters(AgentSchedule schedule) =>
    [
        Int("@FreqType", schedule.FreqType),
        Int("@FreqInterval", schedule.FreqInterval),
        Int("@FreqSubdayType", schedule.FreqSubdayType),
        Int("@FreqSubdayInterval", schedule.FreqSubdayInterval),
        Int("@FreqRecurrenceFactor", schedule.FreqRecurrenceFactor),
        Int("@ActiveStartTime", schedule.ActiveStartTime),
    ];

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
}
