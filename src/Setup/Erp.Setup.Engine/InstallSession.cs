using Erp.Configuration;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;

namespace Erp.Setup.Engine;

/// <summary>
/// Starea unei rulari a planului de instalare, partajata intre pasi. Nu contine secrete persistente: parola temporara a
/// administratorului exista doar in memorie, pana o afiseaza invelisul (ecranul 6 sau consola, 5.10).
/// </summary>
public sealed class InstallSession : IStepJournal
{
    private readonly List<string> completed = [];

    private InstallSession(InstallRequest request, KitManifest manifest, string kitRoot, SqlOperator sql, InstanceState? state)
    {
        Request = request;
        Manifest = manifest;
        KitRoot = kitRoot;
        Sql = sql;
        State = state;
        Resuming = state is { State: InstanceLifecycle.Installing };
        InstallId = Resuming ? state!.InstallId : Guid.NewGuid();
    }

    public InstallRequest Request { get; }

    public KitManifest Manifest { get; }

    public string KitRoot { get; }

    /// <summary>Folderul database\ al kitului (bootstrap, migrations, programmability, seed).</summary>
    public string DatabaseRoot { get; init; } = string.Empty;

    public SqlOperator Sql { get; }

    public required bool Unattended { get; init; }

    /// <summary>SQL Server e pe alt calculator (C-15); decide identitatea aplicatiei.</summary>
    public required bool SqlIsRemote { get; init; }

    /// <summary>SQL Express: fara SQL Agent, deci fara joburi de backup (5.13).</summary>
    public required bool SqlIsExpress { get; init; }

    /// <summary>Folderul de backup rezolvat la verificari (implicit al instantei SQL + numele bazei, 5.3).</summary>
    public string? BackupFolder { get; init; }

    public InstanceState? State { get; private set; }

    public Guid InstallId { get; }

    /// <summary>instance.json are state = Installing: „Reia instalarea” (5.5).</summary>
    public bool Resuming { get; }

    /// <summary>Activarea functiilor IIS sau Hosting Bundle cere repornirea calculatorului (C-06).</summary>
    public bool RestartRequired { get; set; }

    public InitialAdminResult? Administrator { get; set; }

    /// <summary>Calea fisierului cu parola administratorului, la instalarea neasistata.</summary>
    public string? InitialAdminFilePath { get; set; }

    public InstanceDataPaths Paths => InstanceDataPaths.For(Request.DataRoot, Request.Instance);

    public string StatePath => Paths.InstanceJson;

    public string Database => Request.Sql.Database;

    public string Version => Manifest.Version;

    public KitScripts Scripts => field ??= KitScripts.Load(DatabaseRoot);

    /// <summary>
    /// Sesiunea pentru o instalare noua sau reluata: daca instance.json are state = Installing, pasii terminati se verifica rapid
    /// si se continua cu acelasi installId (5.5).
    /// </summary>
    public static InstallSession Start(InstallRequest request, KitManifest manifest, string kitRoot, SqlOperator sql, RequirementsReport report,
        bool unattended)
    {
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance));
        if (state is not null && state.State != InstanceLifecycle.Installing)
        {
            throw new InvalidOperationException($"Instalarea {request.Instance} există deja (starea {state.State}); folosiți „Repară” sau „Actualizează”.");
        }

        return new InstallSession(request, manifest, kitRoot, sql, state)
        {
            DatabaseRoot = Path.Combine(kitRoot, "database"),
            Unattended = unattended,
            SqlIsRemote = report.SqlIsRemote,
            SqlIsExpress = report.Sql?.IsExpress ?? false,
            BackupFolder = report.BackupFolder ?? request.Backup.Folder,
        };
    }

    /// <summary>Sesiunea pentru testele pasilor SQL (fara verificari si fara folderul kitului).</summary>
    internal static InstallSession ForTests(InstallRequest request, KitManifest manifest, string databaseRoot, SqlOperator sql, bool sqlIsRemote = false,
        bool sqlIsExpress = false, string? backupFolder = null) =>
        new(request, manifest, kitRoot: Path.GetDirectoryName(databaseRoot) ?? databaseRoot, sql,
            InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance)))
        {
            DatabaseRoot = databaseRoot,
            Unattended = true,
            SqlIsRemote = sqlIsRemote,
            SqlIsExpress = sqlIsExpress,
            BackupFolder = backupFolder ?? request.Backup.Folder,
        };

    public bool IsCompleted(string stepId) => completed.Contains(stepId) || (Resuming && State?.CompletedSteps.Contains(stepId) == true);

    public void MarkCompleted(string stepId)
    {
        if (!completed.Contains(stepId))
        {
            completed.Add(stepId);
        }

        if (State is not null)
        {
            UpdateState(state => state with { CompletedSteps = [.. state.CompletedSteps.Union(completed)] });
        }
    }

    public IReadOnlyList<string> CompletedThisRun => completed;

    /// <summary>Scrie instance.json pentru prima data (I-01).</summary>
    public void CreateState(InstanceState state)
    {
        State = state;
        InstanceStateStore.Write(StatePath, state);
    }

    /// <summary>Actualizeaza si scrie atomic instance.json (5.7).</summary>
    public void UpdateState(Func<InstanceState, InstanceState> change)
    {
        State = change(State ?? throw new InvalidOperationException("instance.json nu a fost creat (I-01)."));
        InstanceStateStore.Write(StatePath, State);
    }

    public InstanceState RequireState() => State ?? throw new InvalidOperationException("instance.json nu a fost creat (I-01).");
}
