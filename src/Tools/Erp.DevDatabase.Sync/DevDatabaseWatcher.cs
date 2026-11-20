using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Erp.DevDatabase.Sync;

/// <summary>
/// Aplica in baza de dezvoltare fiecare fisier .sql salvat, cat ruleaza aplicatia (07, „Mediul de dezvoltare”). Urmareste
/// folderele Database/ ale depozitului si bootstrap-ul; salvarile in rafala (editorul scrie de mai multe ori) se aduna
/// 500 ms intr-o singura sincronizare, iar sincronizarile ruleaza una dupa alta.
/// </summary>
public sealed class DevDatabaseWatcher(DevDatabaseSync sync, string repoRoot, ILogger log) : BackgroundService
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        var folders = RepositoryScripts.DatabaseFolders(repoRoot).Select(f => f.Folder)
            .Append(RepositoryScripts.BootstrapDirectory(repoRoot))
            .Where(Directory.Exists)
            .ToArray();
        var watchers = folders.Select(folder => Watch(folder, () => changes.Writer.TryWrite(true))).ToArray();
        log.Information("Baza de dezvoltare: urmăresc scripturile SQL din {Count} foldere.", watchers.Length);
        try
        {
            await foreach (var _ in changes.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await Task.Delay(Debounce, stoppingToken).ConfigureAwait(false);
                while (changes.Reader.TryRead(out var __))
                {
                }

                await sync.SyncAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
        }
    }

    private static FileSystemWatcher Watch(string folder, Action changed)
    {
        var watcher = new FileSystemWatcher(folder, "*.sql")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
        };
        watcher.Changed += (_, _) => changed();
        watcher.Created += (_, _) => changed();
        watcher.Deleted += (_, _) => changed();
        watcher.Renamed += (_, _) => changed();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
