using Erp.DevDatabase.Sync;
using Erp.Setup.Sql.Database;

namespace Erp.Setup.Tests.DevDatabase;

/// <summary>Ce face sincronizarea bazei de dezvoltare pentru fiecare fel de schimbare (07, „Mediul de dezvoltare”).</summary>
public sealed class SyncPlannerTests
{
    private const string Bootstrap = "bootstrap-1";
    private const string Migration = "core/2026.11.001_Create_Company.sql";
    private const string Procedure = "core/Procedures/core.usp_Company_Save.sql";
    private const string View = "core/Views/core.v_UserLoginStatus.sql";
    private const string Seed = "core/01_Roles.sql";

    private static readonly ScriptState[] Repository =
    [
        new(ScriptKind.Migration, Migration, "m1"),
        new(ScriptKind.Programmability, Procedure, "p1"),
        new(ScriptKind.Programmability, View, "v1"),
        new(ScriptKind.Seed, Seed, "s1"),
    ];

    /// <summary>Baza sincronizata exact cu depozitul de mai sus.</summary>
    private static DatabaseState Synced(params (string Name, ScriptKind Kind, string Hash)[] overrides)
    {
        var recorded = Repository.ToDictionary(s => s.JournalName, s => (s.Kind, s.Hash), StringComparer.Ordinal);
        foreach (var (name, kind, hash) in overrides)
        {
            recorded[name] = (kind, hash);
        }

        return new DatabaseState(true, true, Bootstrap, new HashSet<string>([Migration], StringComparer.Ordinal), recorded);
    }

    private static SyncPlan Plan(IReadOnlyList<ScriptState> repository, DatabaseState database) => SyncPlanner.Plan(repository, Bootstrap, database);

    private static ScriptState[] With(params ScriptState[] changes) =>
        Repository.Where(s => changes.All(c => c.JournalName != s.JournalName)).Concat(changes).ToArray();

    [Fact]
    public void A_database_matching_the_repository_is_up_to_date()
    {
        var plan = Plan(Repository, Synced());

        Assert.Equal(SyncAction.UpToDate, plan.Action);
        Assert.Empty(plan.Orphans);
    }

    [Fact]
    public void A_missing_database_is_created()
    {
        Assert.Equal(SyncAction.Create, Plan(Repository, DatabaseState.Missing).Action);
    }

    [Fact]
    public void The_first_sync_of_an_existing_database_rebuilds_it_once()
    {
        var old = Synced() with { HasVersionJournal = false, BootstrapHash = null };

        var plan = Plan(Repository, old);

        Assert.Equal(SyncAction.Rebuild, plan.Action);
        Assert.Contains("prima sincronizare", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_procedure_runs_only_that_procedure()
    {
        var plan = Plan(With(new ScriptState(ScriptKind.Programmability, Procedure, "p2")), Synced());

        Assert.Equal(SyncAction.Apply, plan.Action);
        Assert.False(plan.RunMigrations);
        Assert.Equal([Procedure], plan.Programmability);
        Assert.Empty(plan.Seed);
    }

    [Fact]
    public void A_new_procedure_runs_only_that_procedure()
    {
        const string added = "core/Procedures/core.usp_Add_Orice.sql";

        var plan = Plan([.. Repository, new ScriptState(ScriptKind.Programmability, added, "n1")], Synced());

        Assert.Equal(SyncAction.Apply, plan.Action);
        Assert.Equal([added], plan.Programmability);
    }

    [Fact]
    public void A_changed_seed_script_runs_only_that_script()
    {
        var plan = Plan(With(new ScriptState(ScriptKind.Seed, Seed, "s2")), Synced());

        Assert.Equal(SyncAction.Apply, plan.Action);
        Assert.Empty(plan.Programmability);
        Assert.Equal([Seed], plan.Seed);
    }

    [Fact]
    public void A_new_migration_runs_migrations_then_every_object_and_seed_like_the_installer()
    {
        const string added = "core/2026.11.002_Add_Column.sql";

        var plan = Plan([.. Repository, new ScriptState(ScriptKind.Migration, added, "m2")], Synced());

        Assert.Equal(SyncAction.Apply, plan.Action);
        Assert.True(plan.RunMigrations);
        Assert.Equal(new HashSet<string>([Procedure, View]), plan.Programmability);
        Assert.Equal([Seed], plan.Seed);
    }

    [Fact]
    public void A_changed_applied_migration_rebuilds_the_database()
    {
        var plan = Plan(With(new ScriptState(ScriptKind.Migration, Migration, "m1-modificata")), Synced());

        Assert.Equal(SyncAction.Rebuild, plan.Action);
        Assert.Contains(Migration, plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deleted_or_renamed_applied_migration_rebuilds_the_database()
    {
        var plan = Plan(Repository.Where(s => s.JournalName != Migration).ToArray(), Synced());

        Assert.Equal(SyncAction.Rebuild, plan.Action);
        Assert.Contains("ștearsă sau redenumită", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_bootstrap_rebuilds_the_database()
    {
        Assert.Equal(SyncAction.Rebuild, SyncPlanner.Plan(Repository, "bootstrap-2", Synced()).Action);
    }

    [Fact]
    public void A_deleted_procedure_is_reported_not_dropped()
    {
        var plan = Plan(Repository.Where(s => s.JournalName != View).ToArray(), Synced());

        Assert.Equal(SyncAction.UpToDate, plan.Action);
        Assert.Equal([View], plan.Orphans);
    }
}
