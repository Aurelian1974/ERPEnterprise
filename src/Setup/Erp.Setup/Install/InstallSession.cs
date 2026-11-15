using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Install;

/// <summary>
/// Starea unei rulari `install`, partajata intre pasi. Secretele (parolele) exista doar in memorie, pe durata comenzii (R6);
/// instance.json primeste doar starea fara secrete (6.5).
/// </summary>
public sealed class InstallSession : IStepJournal
{
    private readonly List<string> completed = [];

    public required string KitRoot { get; init; }

    /// <summary>Folderul database\ al kitului (bootstrap, migrations, programmability, seed).</summary>
    public string DatabaseRoot { get => field ?? Path.Combine(KitRoot, "database"); init; }

    public required KitManifest Manifest { get; init; }

    public required bool Unattended { get; init; }

    public required bool AssumeYes { get; init; }

    public InstallAnswers? Answers { get; set; }

    public SqlOperator? Sql { get; set; }

    public string? OperatorPassword { get; set; }

    /// <summary>null = parola administratorului se genereaza (8.8).</summary>
    public string? AdminPassword { get; set; }

    public string? AppPassword { get; set; }

    public SqlConnectionStringBuilder? Migrator { get; set; }

    public InstanceState? State { get; set; }

    /// <summary>instance.json gasit la I-02 (orice stare), pentru C-23.</summary>
    public InstanceState? ExistingInstance { get; set; }

    public Guid InstallId { get; set; }

    public bool Resuming { get; set; }

    public KitScripts? Scripts { get; set; }

    public InitialAdminResult? Administrator { get; set; }

    public string? CertificateThumbprint { get; set; }

    public RequirementsReport? Requirements { get; set; }

    public InstallAnswers RequireAnswers() => Answers ?? throw new InvalidOperationException("Fișierul de răspunsuri nu a fost citit (I-02).");

    public SqlOperator RequireSql() => Sql ?? throw new InvalidOperationException("Conexiunea SQL a operatorului lipsește (I-02).");

    public string StatePath => InstanceStateStore.PathFor(RequireAnswers().DataRoot, RequireAnswers().Instance);

    public CheckTarget Target(CheckMode mode) => CheckTarget.FromAnswers(RequireAnswers(), mode);

    public KitScripts LoadScripts() => Scripts ??= KitScripts.Load(DatabaseRoot);

    public bool IsCompleted(string stepId) =>
        completed.Contains(stepId) || (Resuming && State?.CompletedSteps.Contains(stepId) == true);

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

    /// <summary>Actualizeaza si scrie atomic instance.json (6.5).</summary>
    public void UpdateState(Func<InstanceState, InstanceState> change)
    {
        State = change(State ?? throw new InvalidOperationException("instance.json nu a fost creat (I-05)."));
        InstanceStateStore.Write(StatePath, State);
    }
}
