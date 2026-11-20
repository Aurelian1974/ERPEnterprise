using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Erp.DevDatabase.Sync;

/// <summary>
/// Aplica in baza de dezvoltare fiecare fisier .sql salvat, cat ruleaza aplicatia (07, „Mediul de dezvoltare”). Urmareste
/// folderele Database/ ale depozitului si bootstrap-ul; salvarile in rafala (editorul scrie de mai multe ori) se aduna
/// 500 ms intr-o singura sincronizare, iar sincronizarile ruleaza una dupa alta.
/// </summary>
public sealed class DevDatabaseWatcher : BackgroundService
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly Func<CancellationToken, Task> sync;
    private readonly string repoRoot;
    private readonly ILogger log;
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private FileSystemWatcher[] watchers = [];

    public DevDatabaseWatcher(DevDatabaseSync sync, string repoRoot, ILogger log)
        : this(async ct => await sync.SyncAsync(ct).ConfigureAwait(false), repoRoot, log)
    {
    }

    internal DevDatabaseWatcher(Func<CancellationToken, Task> sync, string repoRoot, ILogger log)
    {
        this.sync = sync;
        this.repoRoot = repoRoot;
        this.log = log;
    }

    /// <summary>
    /// Observatoarele pornesc aici, sincron, nu in ExecuteAsync: din .NET 10 ExecuteAsync ruleaza pe alt fir, dupa ce
    /// StartAsync s-a intors, iar o salvare facuta imediat dupa pornire s-ar pierde.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var folders = RepositoryScripts.DatabaseFolders(repoRoot).Select(f => f.Folder)
            .Append(RepositoryScripts.BootstrapDirectory(repoRoot))
            .Where(Directory.Exists)
            .ToArray();
        watchers = folders.Select(folder => Watch(folder, () => changes.Writer.TryWrite(true))).ToArray();
        log.Information("Baza de dezvoltare: urmăresc scripturile SQL din {Count} foldere.", watchers.Length);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in changes.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await Task.Delay(Debounce, stoppingToken).ConfigureAwait(false);
                while (changes.Reader.TryRead(out var __))
                {
                }

                await sync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        DisposeWatchers();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        DisposeWatchers();
        base.Dispose();
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in Interlocked.Exchange(ref watchers, []))
        {
            watcher.Dispose();
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
