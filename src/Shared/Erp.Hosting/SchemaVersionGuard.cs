using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Erp.Hosting;

public enum SchemaCompatibility
{
    Unknown,
    Compatible,
    Incompatible,
}

/// <summary>Ultima migrare aplicata bazei (10.5).</summary>
public interface ISchemaVersionSource
{
    Task<string?> ReadAsync(CancellationToken ct);
}

/// <summary>Citirea ultimei migrari din core.SchemaVersion (10.5).</summary>
public sealed class SchemaVersionReader(Func<SqlConnection> connectionFactory) : ISchemaVersionSource
{
    public const string Query = "SELECT TOP (1) ScriptName FROM core.SchemaVersion ORDER BY Id DESC;";

    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new SqlCommand(Query, connection) { CommandTimeout = 10 };
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }
}

/// <summary>
/// Protectia binare ↔ baza (10.5): la pornire, Erp.Host si Erp.Worker compara ultima migrare din baza cu cea incorporata.
/// Daca difera, partea de business nu porneste si se jurnalizeaza eroarea critica. Daca baza nu raspunde, se reincearca la 30 s.
/// </summary>
/// <param name="allowUnversionedBuild">
/// Doar in mediul Development: un build local, fara ErpLastMigration incorporata (kiturile o au mereu), accepta orice baza migrata.
/// </param>
public sealed class SchemaVersionGuard(
    ISchemaVersionSource reader, ErpBuildInfo build, ILogger<SchemaVersionGuard> log, TimeProvider time, bool allowUnversionedBuild = false)
    : BackgroundService
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource<SchemaCompatibility> decided = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SchemaCompatibility State { get; private set; } = SchemaCompatibility.Unknown;

    /// <summary>Partea de business porneste doar cu Compatible.</summary>
    public bool IsCompatible => State == SchemaCompatibility.Compatible;

    public Task<SchemaCompatibility> Decided => decided.Task;

    public async Task<SchemaCompatibility> CheckAsync(CancellationToken ct)
    {
        var actual = await reader.ReadAsync(ct).ConfigureAwait(false);
        State = Evaluate(build.LastMigration, actual, allowUnversionedBuild);
        if (State == SchemaCompatibility.Incompatible)
        {
            log.LogCritical("Versiunea bazei de date nu corespunde aplicației: baza are {Actual}, aplicația așteaptă {Expected}. Partea de business nu pornește.",
                actual ?? "(nicio migrare)", build.LastMigration ?? "(nedefinit)");
        }
        else if (string.IsNullOrEmpty(build.LastMigration))
        {
            log.LogWarning("Build de dezvoltare fără ultima migrare încorporată: baza ({LastMigration}) se acceptă fără comparare.", actual);
        }
        else
        {
            log.LogInformation("Versiunea bazei de date corespunde aplicației ({LastMigration})", actual);
        }

        decided.TrySetResult(State);
        return State;
    }

    public static SchemaCompatibility Evaluate(string? expected, string? actual, bool allowUnversionedBuild = false)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return allowUnversionedBuild && !string.IsNullOrEmpty(actual) ? SchemaCompatibility.Compatible : SchemaCompatibility.Incompatible;
        }

        return string.Equals(expected, actual, StringComparison.Ordinal) ? SchemaCompatibility.Compatible : SchemaCompatibility.Incompatible;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (SqlException ex)
            {
                log.LogError(ex, "Versiunea bazei de date nu poate fi citită; reîncerc în {Seconds} s", RetryInterval.TotalSeconds);
            }

            await Task.Delay(RetryInterval, time, stoppingToken).ConfigureAwait(false);
        }
    }
}
