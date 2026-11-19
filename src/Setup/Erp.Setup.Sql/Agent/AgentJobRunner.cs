using System.Data;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Agent;

public sealed record AgentJobRun(bool Succeeded, string Message);

/// <summary>
/// Pasul I-22 (8.13): porneste un job (sp_start_job) si urmareste msdb.dbo.sysjobactivity si sysjobhistory pana la terminare.
/// Folosit pentru primul backup complet (la 5 s, maximum 30 de minute) si pentru backup-ul imediat dupa rollback (R-06).
/// </summary>
public sealed class AgentJobRunner(SqlOperator sql)
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Porneste jobul fara sa astepte (R-05: backup complet imediat dupa readucerea bazei).</summary>
    public async Task StartAsync(string jobName, CancellationToken ct)
    {
        await using var msdb = await sql.OpenAsync("msdb", ct).ConfigureAwait(false);
        await sql.ExecuteAsync(msdb, $"Pornire job {jobName}", "EXEC dbo.sp_start_job @job_name = @Name;", ct, Name(jobName)).ConfigureAwait(false);
    }

    public async Task<AgentJobRun> RunAndWaitAsync(string jobName, CancellationToken ct, TimeSpan? pollInterval = null, TimeSpan? timeout = null)
    {
        await using var msdb = await sql.OpenAsync("msdb", ct).ConfigureAwait(false);
        var startedAfter = await SqlOperator.ScalarAsync<DateTime>(msdb, "SELECT DATEADD(SECOND, -1, GETDATE());", ct).ConfigureAwait(false);
        await sql.ExecuteAsync(msdb, $"Pornire job {jobName}", "EXEC dbo.sp_start_job @job_name = @Name;", ct, Name(jobName)).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(pollInterval ?? DefaultPollInterval, ct).ConfigureAwait(false);
            var historyId = await FinishedRunAsync(msdb, jobName, startedAfter, ct).ConfigureAwait(false);
            if (historyId is not null)
            {
                return await OutcomeAsync(msdb, jobName, historyId.Value, ct).ConfigureAwait(false);
            }
        }

        sql.Log.Error("Jobul {Job} nu s-a terminat în {Minutes} minute", jobName, (timeout ?? DefaultTimeout).TotalMinutes);
        return new AgentJobRun(false, $"Jobul „{jobName}” nu s-a terminat în {(timeout ?? DefaultTimeout).TotalMinutes:F0} minute.");
    }

    private static async Task<int?> FinishedRunAsync(SqlConnection msdb, string jobName, DateTime startedAfter, CancellationToken ct)
    {
        const string query = """
            SELECT TOP (1) a.job_history_id
            FROM dbo.sysjobactivity AS a
            JOIN dbo.sysjobs AS j ON j.job_id = a.job_id
            WHERE j.name = @Name
              AND a.session_id = (SELECT MAX(s.session_id) FROM dbo.syssessions AS s)
              AND a.start_execution_date >= @StartedAfter
              AND a.stop_execution_date IS NOT NULL
              AND a.job_history_id IS NOT NULL
            ORDER BY a.start_execution_date DESC;
            """;
        return await SqlOperator.ScalarAsync<int?>(msdb, query, ct, Name(jobName),
            new SqlParameter("@StartedAfter", SqlDbType.DateTime) { Value = startedAfter }).ConfigureAwait(false);
    }

    private async Task<AgentJobRun> OutcomeAsync(SqlConnection msdb, string jobName, int historyId, CancellationToken ct)
    {
        var status = await SqlOperator.ScalarAsync<int>(msdb, "SELECT run_status FROM dbo.sysjobhistory WHERE instance_id = @Id;", ct,
            new SqlParameter("@Id", SqlDbType.Int) { Value = historyId }).ConfigureAwait(false);
        if (status == 1)
        {
            sql.Log.Information("Jobul {Job} a reușit", jobName);
            return new AgentJobRun(true, $"Jobul „{jobName}” a reușit.");
        }

        // Mesajul pasului esuat spune cauza (de exemplu calea de backup fara drept de scriere).
        const string stepMessage = """
            SELECT TOP (1) h.message
            FROM dbo.sysjobhistory AS h
            JOIN dbo.sysjobs AS j ON j.job_id = h.job_id
            WHERE j.name = @Name AND h.step_id > 0 AND h.instance_id < @Id
            ORDER BY h.instance_id DESC;
            """;
        var message = await SqlOperator.ScalarAsync<string>(msdb, stepMessage, ct, Name(jobName),
            new SqlParameter("@Id", SqlDbType.Int) { Value = historyId }).ConfigureAwait(false);
        sql.Log.Error("Jobul {Job} a eșuat: {Message}", jobName, message);
        return new AgentJobRun(false, $"Jobul „{jobName}” a eșuat: {message}");
    }

    private static SqlParameter Name(string jobName) => new("@Name", SqlDbType.NVarChar, 128) { Value = jobName };
}
