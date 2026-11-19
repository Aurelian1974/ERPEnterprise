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

/// <summary>Ce face planul I-01…I-18 (08, 5.5, 11.1, 11.2).</summary>
public enum SetupMode
{
    Install,

    /// <summary>„Repară”: aceeasi versiune; nu atinge utilizatorii si datele; nu recreeaza certificatul instalarii.</summary>
    Repair,

    /// <summary>„Recuperează”: baza ERP exista, instalarea de pe calculator nu; certificatul vine din cheia de recuperare.</summary>
    Recover,
}

/// <summary>
/// Starea unei rulari a planului de instalare, partajata intre pasi. Nu contine secrete persistente: parola temporara a
/// administratorului exista doar in memorie, pana o afiseaza invelisul (ecranul 6 sau consola, 5.10).
/// </summary>
public sealed class InstallSession : IStepJournal
{
    private readonly List<string> completed = [];

    private InstallSession(InstallRequest request, KitManifest manifest, string kitRoot, SqlOperator sql, InstanceState? state, Guid? installId = null)
    {
        Request = request;
        Manifest = manifest;
        KitRoot = kitRoot;
        Sql = sql;
        State = state;
        Resuming = state is { State: InstanceLifecycle.Installing };
        InstallId = installId ?? (state is not null ? state.InstallId : Guid.NewGuid());
    }

    public SetupMode Mode { get; init; } = SetupMode.Install;

    /// <summary>„Repară --license”: licenta noua de incarcat la I-13; altfel I-13 se sare la reparare.</summary>
    public string? LicenseOverride { get; init; }

    /// <summary>„Recuperează”: cheia de recuperare (5.12); null = continua fara ea, cu confirmarea explicita a operatorului.</summary>
    public RecoveryKeyInput? RecoveryKey { get; init; }

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

    /// <summary>„Repară” (11.1): aceeasi versiune a kitului ca instalarea (cod 74 altfel), starea si installId-ul raman.</summary>
    public static InstallSession StartRepair(InstallRequest request, KitManifest manifest, string kitRoot, SqlOperator sql, InstanceState state,
        RequirementsReport report, string? licenseOverride)
    {
        if (!string.Equals(state.CurrentVersion, manifest.Version, StringComparison.Ordinal))
        {
            throw new SetupModeException($"Instalarea e la versiunea {state.CurrentVersion}; „Repară” cere kitul {state.CurrentVersion} (kitul curent: {manifest.Version}).",
                Core.ExitCodes.KitVersionMismatch);
        }

        return new InstallSession(request, manifest, kitRoot, sql, state)
        {
            Mode = SetupMode.Repair,
            DatabaseRoot = Path.Combine(kitRoot, "database"),
            Unattended = true,
            SqlIsRemote = state.SqlIsRemote,
            SqlIsExpress = state.BackupEdition == BackupEdition.None,
            BackupFolder = state.BackupFolder ?? report.BackupFolder,
            LicenseOverride = licenseOverride,
        };
    }

    /// <summary>
    /// „Recuperează” (11.2): baza ERP exista (C-14 o recunoaste), kitul are exact versiunea ei (cod 74), iar noul instance.json
    /// pastreaza ErpInstallId al bazei.
    /// </summary>
    public static InstallSession StartRecover(InstallRequest request, KitManifest manifest, string kitRoot, SqlOperator sql, RequirementsReport report,
        RecoveryKeyInput? recoveryKey)
    {
        if (InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance)) is not null)
        {
            throw new SetupModeException($"Instalarea {request.Instance} există pe acest calculator; folosiți „Repară”.", Core.ExitCodes.InvalidCommandLine);
        }

        var database = report.Database is { IsErp: true } erp ? erp
            : throw new SetupModeException($"Baza {request.Sql.Database} nu este o bază ERP.", Core.ExitCodes.DatabaseExistsNotEmpty);
        if (!string.Equals(database.LastMigration, manifest.LastMigration, StringComparison.Ordinal))
        {
            throw new SetupModeException($"Baza este la migrarea {database.LastMigration}, iar kitul la {manifest.LastMigration}; folosiți kitul versiunii bazei, apoi actualizați.",
                Core.ExitCodes.KitVersionMismatch);
        }

        return new InstallSession(request, manifest, kitRoot, sql, state: null, Guid.TryParse(database.ErpInstallId, out var id) ? id : Guid.NewGuid())
        {
            Mode = SetupMode.Recover,
            DatabaseRoot = Path.Combine(kitRoot, "database"),
            Unattended = false,
            SqlIsRemote = report.SqlIsRemote,
            SqlIsExpress = report.Sql?.IsExpress ?? false,
            BackupFolder = report.BackupFolder ?? request.Backup.Folder,
            RecoveryKey = recoveryKey,
        };
    }

    /// <summary>Sesiunea pentru testele pasilor SQL (fara verificari si fara folderul kitului).</summary>
    internal static InstallSession ForTests(InstallRequest request, KitManifest manifest, string databaseRoot, SqlOperator sql, bool sqlIsRemote = false,
        bool sqlIsExpress = false, string? backupFolder = null, SetupMode mode = SetupMode.Install) =>
        new(request, manifest, kitRoot: Path.GetDirectoryName(databaseRoot) ?? databaseRoot, sql,
            InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance)))
        {
            Mode = mode,
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

/// <summary>Fisierul .pfx al cheii de recuperare si parola lui (doar in memorie).</summary>
public sealed record RecoveryKeyInput(string PfxPath, string Password)
{
    public override string ToString() => $"RecoveryKeyInput {{ PfxPath = {PfxPath} }}";
}

/// <summary>Actiunea nu se poate face cu acest kit sau pe aceasta instalare; contine codul de iesire (6.3).</summary>
public sealed class SetupModeException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
