using Erp.DevDatabase.Sync;
using Serilog;

namespace Erp.Setup.Tests.DevDatabase;

/// <summary>
/// Observatorul fisierelor SQL (07, „Mediul de dezvoltare”), fara SQL Server: o salvare, chiar imediat dupa pornire, porneste
/// o sincronizare; salvarile in rafala se aduna intr-una singura.
/// </summary>
public sealed class DevDatabaseWatcherTests : IDisposable
{
    private readonly string repo = Path.Combine(Path.GetTempPath(), "erp-devwatch-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Procedures => Path.Combine(repo, "src", "Modules", "Core", "Erp.Core.Infrastructure", "Database", "Programmability", "Procedures");

    public DevDatabaseWatcherTests()
    {
        Directory.CreateDirectory(Procedures);
        Directory.CreateDirectory(RepositoryScripts.BootstrapDirectory(repo));
    }

    public void Dispose() => Directory.Delete(repo, recursive: true);

    [Fact]
    public async Task A_file_saved_right_after_start_triggers_one_sync()
    {
        var syncs = 0;
        var synced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new DevDatabaseWatcher(_ =>
        {
            Interlocked.Increment(ref syncs);
            synced.TrySetResult();
            return Task.CompletedTask;
        }, repo, new LoggerConfiguration().CreateLogger());

        await watcher.StartAsync(Ct);
        try
        {
            // Imediat dupa pornire, ca in testul de integrare: salvarea nu trebuie pierduta.
            var file = Path.Combine(Procedures, "core.usp_Probe_Get.sql");
            await File.WriteAllTextAsync(file, "CREATE OR ALTER PROCEDURE core.usp_Probe_Get AS SELECT 1;", Ct);
            await File.AppendAllTextAsync(file, "\n", Ct);

            await synced.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await Task.Delay(DevDatabaseWatcher.Debounce * 3, Ct);
            Assert.Equal(1, Volatile.Read(ref syncs));
        }
        finally
        {
            await watcher.StopAsync(CancellationToken.None);
        }
    }
}
