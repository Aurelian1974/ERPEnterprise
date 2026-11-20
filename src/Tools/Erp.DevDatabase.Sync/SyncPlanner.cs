using Erp.Setup.Sql.Database;

namespace Erp.DevDatabase.Sync;

/// <summary>Un script din depozit, cu amprenta continutului lui (ScriptFingerprint).</summary>
public sealed record ScriptState(ScriptKind Kind, string JournalName, string Hash);

/// <summary>Ce stie baza de dezvoltare: migratiile rulate (core.SchemaVersion) si ultima amprenta a fiecarui script (core.ScriptVersion).</summary>
public sealed record DatabaseState(
    bool Exists,
    bool HasVersionJournal,
    string? BootstrapHash,
    IReadOnlySet<string> AppliedMigrations,
    IReadOnlyDictionary<string, (ScriptKind Kind, string Hash)> Recorded)
{
    public static DatabaseState Missing { get; } = new(false, false, null,
        new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, (ScriptKind, string)>(StringComparer.Ordinal));
}

public enum SyncAction
{
    UpToDate,
    Create,
    Rebuild,
    Apply,
}

/// <summary>
/// Planul unei sincronizari. La Apply: migratiile noi (DbUp le sare pe cele rulate), obiectele programabile si datele livrate
/// de rulat, in ordinea din KitScripts. Orphans: obiecte programabile fara fisier in depozit, care raman in baza pana la o
/// migrare DROP … IF EXISTS (08, 5.9) — se semnaleaza, nu se sterg.
/// </summary>
public sealed record SyncPlan(
    SyncAction Action,
    string Reason,
    bool RunMigrations,
    IReadOnlySet<string> Programmability,
    IReadOnlySet<string> Seed,
    IReadOnlyList<string> Orphans);

/// <summary>Compara depozitul cu baza de dezvoltare si decide ce se face (07, „Mediul de dezvoltare”).</summary>
public static class SyncPlanner
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    public static SyncPlan Plan(IReadOnlyList<ScriptState> repository, string bootstrapHash, DatabaseState database)
    {
        if (!database.Exists)
        {
            return Whole(SyncAction.Create, "baza de dezvoltare nu există");
        }

        if (!database.HasVersionJournal || database.BootstrapHash is null)
        {
            return Whole(SyncAction.Rebuild, "prima sincronizare: baza se reface o dată din scripturile actuale");
        }

        if (!string.Equals(database.BootstrapHash, bootstrapHash, StringComparison.Ordinal))
        {
            return Whole(SyncAction.Rebuild, "scripturile de bootstrap s-au modificat");
        }

        var byName = repository.ToDictionary(s => s.JournalName, StringComparer.Ordinal);
        foreach (var applied in database.AppliedMigrations.Order(StringComparer.Ordinal))
        {
            if (!byName.TryGetValue(applied, out var script))
            {
                return Whole(SyncAction.Rebuild, $"migrarea {applied} a fost ștearsă sau redenumită");
            }

            if (!database.Recorded.TryGetValue(applied, out var recorded))
            {
                return Whole(SyncAction.Rebuild, $"migrarea {applied} nu are amprentă în jurnal");
            }

            if (!string.Equals(recorded.Hash, script.Hash, StringComparison.Ordinal))
            {
                return Whole(SyncAction.Rebuild, $"migrarea {applied} s-a modificat după ce a fost aplicată");
            }
        }

        var orphans = database.Recorded
            .Where(r => r.Value.Kind == ScriptKind.Programmability && !byName.ContainsKey(r.Key))
            .Select(r => r.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var newMigrations = repository.Where(s => s.Kind == ScriptKind.Migration && !database.AppliedMigrations.Contains(s.JournalName)).ToArray();
        if (newMigrations.Length > 0)
        {
            // Ca installer-ul (5.9): dupa migratii ruleaza toate obiectele si datele livrate, pentru ca o migrare poate schimba
            // tabelele de care depind.
            return new SyncPlan(SyncAction.Apply, $"{newMigrations.Length} migrații noi: {string.Join(", ", newMigrations.Select(m => m.JournalName))}",
                true, Names(repository, ScriptKind.Programmability), Names(repository, ScriptKind.Seed), orphans);
        }

        var changedProgrammability = Changed(repository, database, ScriptKind.Programmability);
        var changedSeed = Changed(repository, database, ScriptKind.Seed);
        if (changedProgrammability.Count == 0 && changedSeed.Count == 0)
        {
            return new SyncPlan(SyncAction.UpToDate, "baza este la zi", false, None, None, orphans);
        }

        var changed = changedProgrammability.Concat(changedSeed).Order(StringComparer.Ordinal);
        return new SyncPlan(SyncAction.Apply, $"scripturi schimbate: {string.Join(", ", changed)}", false, changedProgrammability, changedSeed, orphans);
    }

    private static SyncPlan Whole(SyncAction action, string reason) => new(action, reason, true, None, None, []);

    private static HashSet<string> Names(IEnumerable<ScriptState> repository, ScriptKind kind) =>
        repository.Where(s => s.Kind == kind).Select(s => s.JournalName).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Changed(IEnumerable<ScriptState> repository, DatabaseState database, ScriptKind kind) =>
        repository.Where(s => s.Kind == kind
                && (!database.Recorded.TryGetValue(s.JournalName, out var recorded) || !string.Equals(recorded.Hash, s.Hash, StringComparison.Ordinal)))
            .Select(s => s.JournalName)
            .ToHashSet(StringComparer.Ordinal);
}
