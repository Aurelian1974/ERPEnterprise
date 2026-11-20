using System.Data;
using DbUp.Engine;

namespace Erp.Setup.Sql.Database;

/// <summary>Tipul scriptului in core.ScriptVersion (SQL-10: valori explicite, nerenumerotate).</summary>
public enum ScriptKind : byte
{
    Migration = 1,
    Programmability = 2,
    Seed = 3,
}

/// <summary>
/// Versiunile scripturilor din kit (manifest.scriptVersions, calculate din Git la construire) si versiunea produsului.
/// Un script absent din lista primeste versiunea 0 (kit de test sau baza de dezvoltare fara istoric Git).
/// </summary>
public sealed record ScriptVersions(IReadOnlyDictionary<string, int> Versions, string ProductVersion)
{
    /// <summary>Fara versiuni calculate: testele si instrumentele care nu au manifest.</summary>
    public static ScriptVersions Development { get; } = new(new Dictionary<string, int>(StringComparer.Ordinal), "dev");

    public int VersionOf(string scriptName) => Versions.TryGetValue(scriptName, out var version) ? version : 0;
}

/// <summary>
/// Scrie in core.ScriptVersion randul unui script aplicat, doar daca amprenta lui difera de ultimul rand al aceluiasi script:
/// tabela ramane istoricul modificarilor, nu al rularilor. Ruleaza prin fabrica de comenzi a DbUp, deci in tranzactia
/// scriptului (WithTransactionPerScript): un script reusit are mereu randul lui, unul esuat niciodata. SQL parametrizat (R3).
/// </summary>
public sealed class ScriptVersionRecorder(ScriptKind kind, ScriptVersions versions)
{
    private const string InsertSql = """
        INSERT INTO core.ScriptVersion (ScriptName, ScriptKind, Version, ContentHash, ProductVersion)
        SELECT @ScriptName, @ScriptKind, @Version, @ContentHash, @ProductVersion
        WHERE NOT EXISTS (
            SELECT 1
            FROM (SELECT TOP (1) v.ContentHash FROM core.ScriptVersion AS v WHERE v.ScriptName = @ScriptName ORDER BY v.Id DESC) AS latest
            WHERE latest.ContentHash = @ContentHash);
        """;

    public void Record(SqlScript script, Func<IDbCommand> commandFactory)
    {
        using var command = commandFactory();
        command.CommandText = InsertSql;
        Add(command, "@ScriptName", DbType.String, script.Name);
        Add(command, "@ScriptKind", DbType.Byte, (byte)kind);
        Add(command, "@Version", DbType.Int32, versions.VersionOf(script.Name));
        Add(command, "@ContentHash", DbType.AnsiStringFixedLength, ScriptFingerprint.Compute(script.Contents));
        Add(command, "@ProductVersion", DbType.AnsiString, versions.ProductVersion);
        command.ExecuteNonQuery();
    }

    private static void Add(IDbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>
/// Obiectele programabile si datele livrate (MIG-04, MIG-06): DbUp le considera mereu nerulate, deci le aplica pe toate la
/// fiecare instalare si actualizare, ca inainte cu NullJournal; jurnalul doar noteaza versiunea celor schimbate.
/// Tabela vine din bootstrap (003_ScriptVersion.sql), deci aici nu se creeaza nimic.
/// </summary>
public sealed class ScriptVersionJournal(ScriptVersionRecorder recorder) : IJournal
{
    public string[] GetExecutedScripts() => [];

    public void StoreExecutedScript(SqlScript script, Func<IDbCommand> dbCommandFactory) => recorder.Record(script, dbCommandFactory);

    public void EnsureTableExistsAndIsLatestVersion(Func<IDbCommand> dbCommandFactory)
    {
    }
}

/// <summary>
/// Migratiile: jurnalul DbUp ramane sursa „ce s-a rulat” (core.SchemaVersion, neschimbat, 10.4), iar versiunea se noteaza
/// in plus, in aceeasi tranzactie.
/// </summary>
public sealed class VersionedMigrationJournal(IJournal inner, ScriptVersionRecorder recorder) : IJournal
{
    public string[] GetExecutedScripts() => inner.GetExecutedScripts();

    public void StoreExecutedScript(SqlScript script, Func<IDbCommand> dbCommandFactory)
    {
        inner.StoreExecutedScript(script, dbCommandFactory);
        recorder.Record(script, dbCommandFactory);
    }

    public void EnsureTableExistsAndIsLatestVersion(Func<IDbCommand> dbCommandFactory) => inner.EnsureTableExistsAndIsLatestVersion(dbCommandFactory);
}
