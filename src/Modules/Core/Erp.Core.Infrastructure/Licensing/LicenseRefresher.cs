using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Application.Licensing;
using Erp.Licensing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Infrastructure.Licensing;

/// <summary>
/// Reincarca licenta din baza (08, 4.3 pasul 2; 4.4): la pornire (inainte de primele cereri), la fiecare 5 minute (si alt
/// proces poate incarca o licenta) si imediat dupa o incarcare in acest proces. Actualizeaza si data de referinta.
/// </summary>
public sealed class LicenseRefresher(string connectionString, LicenseInfo info, ILicenseReader reader, TimeProvider time,
    ILogger<LicenseRefresher> log) : BackgroundService, ILicenseRefresh
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int SystemUserId = 1;
    private TaskCompletionSource signal = NewSignal();

    public void RequestRefresh() => Volatile.Read(ref signal).TrySetResult();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshSafelyAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var requested = Volatile.Read(ref signal);
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                await Task.WhenAny(Task.Delay(Interval, time, wait.Token), requested.Task).ConfigureAwait(false);
                await wait.CancelAsync().ConfigureAwait(false);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (requested.Task.IsCompleted)
            {
                Interlocked.Exchange(ref signal, NewSignal());

                // Comanda care a cerut reincarcarea isi termina tranzactia intre timp.
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken).ConfigureAwait(false);
            }

            await RefreshSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var highest = await TouchClockAsync(connection, today, ct).ConfigureAwait(false);
        var clockBehind = today < highest.AddDays(-1);

        var row = await connection.QuerySingleOrDefaultAsync<LicenseRow>(
            new CommandDefinition("core.usp_InstallationLicense_GetCurrent", commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var reference = highest > today ? highest : today;
        var result = row is null ? null : reader.Read(row.LicenseFile, reference);
        if (result is { IsAuthentic: false })
        {
            log.LogCritical("Licența din bază (rândul {Id}) a fost refuzată: {Reason}", row!.Id, result.Message);
        }

        var license = result is { IsAuthentic: true } ? result.License : null;
        var companies = await connection.QueryAsync<CompanyRow>(
            new CommandDefinition("core.usp_Company_ListActiveFiscalCodes", commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var unlicensed = license is null
            ? new HashSet<int>()
            : companies.Where(c => !license.ContainsFiscalCode(c.FiscalCode)).Select(c => c.Id).ToHashSet();

        info.Apply(new LicenseSnapshotData(license, result?.KeyId ?? 0, highest, clockBehind, unlicensed));
    }

    private async Task<DateOnly> TouchClockAsync(SqlConnection connection, DateOnly today, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var clock = await connection.QuerySingleAsync<ClockRow>(new CommandDefinition("core.usp_LicenseClock_Touch",
            new { Today = today.ToDateTime(TimeOnly.MinValue), UserId = SystemUserId }, transaction, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false);
        var highest = DateOnly.FromDateTime(clock.Highest);
        if (today < highest.AddDays(-1) && !info.ClockBehind)
        {
            // 08, 4.4: ceasul dat inapoi nu prelungeste licenta; administratorii vad avertismentul, iar faptul ramane in audit.
            log.LogWarning("Ceasul serverului ({Today}) este în urma celei mai mari date văzute de aplicație ({Highest}).", today, highest);
            await connection.ExecuteAsync(new CommandDefinition("core.usp_AuditEntry_Insert", new
            {
                OccurredAt = time.GetUtcNow().UtcDateTime,
                UserId = SystemUserId,
                CompanyId = (int?)null,
                Action = "Core.License.ClockBehind",
                EntityType = "Core.Setting",
                EntityId = (long?)null,
                Changes = JsonSerializer.Serialize(new { today, highestSeenDate = highest }),
                IpAddress = (string?)null,
                CorrelationId = Guid.NewGuid(),
            }, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return highest;
    }

    private async Task RefreshSafelyAsync(CancellationToken ct)
    {
        try
        {
            await RefreshAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            // Baza poate lipsi temporar (de exemplu in mentenanta, 08, 10.3): starea anterioara ramane, se reincearca la urmatorul pas.
            log.LogError(ex, "Licența nu s-a putut reîncărca din bază; se păstrează starea anterioară.");
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record LicenseRow(int Id, byte[] LicenseFile);

    private sealed record CompanyRow(int Id, string FiscalCode);

    private sealed record ClockRow(DateTime? PreviousHighest, DateTime Highest);
}
