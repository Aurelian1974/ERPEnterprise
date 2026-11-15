using System.Globalization;
using Erp.Setup.Sql.Database;

namespace Erp.Setup.Sql.Agent;

/// <summary>Setarile backup-ului din fisierul de raspunsuri (6.2, D8).</summary>
public sealed record BackupSettings(
    string DatabaseName,
    string BackupRoot,
    TimeOnly FullBackupTime,
    int LogBackupEveryMinutes,
    int RetentionDays,
    DayOfWeek IntegrityCheckDay,
    TimeOnly IntegrityCheckTime);

/// <summary>Programarea unui job, in parametrii msdb.dbo.sp_add_schedule.</summary>
public sealed record AgentSchedule(int FreqType, int FreqInterval, int FreqSubdayType, int FreqSubdayInterval, int FreqRecurrenceFactor, int ActiveStartTime);

public sealed record AgentJobDefinition(string Name, string Description, string StepName, string StepDatabase, string Command, AgentSchedule Schedule);

/// <summary>
/// Joburile SQL Agent ale instalarii (8.13). Textul pasilor se genereaza din numele bazei si caile validate (8.2, abaterea SP-06),
/// nu se editeaza manual. Descrierea contine versiunea definitiei („ERP-JOB v1”).
/// </summary>
public static class AgentJobDefinitions
{
    public const int DefinitionVersion = 1;
    public const string Category = "ERP";
    public const string VersionMarker = "ERP-JOB v";

    public static string FullBackupJob(string database) => $"ERP {database} - Backup complet";

    public static string LogBackupJob(string database) => $"ERP {database} - Backup log";

    public static string CleanupJob(string database) => $"ERP {database} - Curățare backup";

    public static string IntegrityJob(string database) => $"ERP {database} - Verificare integritate";

    public static string IndexMaintenanceJob(string database) => $"ERP {database} - Mentenanță indecși";

    /// <summary>Folderul de backup al bazei: &lt;BackupRoot&gt;\&lt;Baza&gt;\{FULL,LOG,UPGRADE}\, cu separatorul caii date.</summary>
    public static string BackupFolder(BackupSettings settings, string kind)
    {
        DatabaseScripts.ValidatePath(settings.BackupRoot);
        var folder = DatabaseScripts.CombinePath(DatabaseScripts.CombinePath(settings.BackupRoot, settings.DatabaseName), kind);
        return folder + (folder.Contains('\\') ? '\\' : '/');
    }

    public static IReadOnlyList<AgentJobDefinition> For(BackupSettings settings)
    {
        DatabaseScripts.ValidateDatabaseName(settings.DatabaseName);
        var db = settings.DatabaseName;
        return
        [
            new(FullBackupJob(db), Describe("Backup complet zilnic, verificat cu RESTORE VERIFYONLY."), "Backup complet", "master",
                BackupCommand(settings, "FULL", "DATABASE", "bak"), Daily(settings.FullBackupTime)),
            new(LogBackupJob(db), Describe($"Backup de log la {settings.LogBackupEveryMinutes} minute."), "Backup log", "master",
                BackupCommand(settings, "LOG", "LOG", "trn"),
                new AgentSchedule(4, 1, 4, settings.LogBackupEveryMinutes, 0, 0)),
            new(CleanupJob(db), Describe($"Șterge backup-urile FULL și LOG mai vechi de {settings.RetentionDays} zile și istoricul din msdb."),
                "Curățare backup", "master", CleanupCommand(settings), Daily(new TimeOnly(5, 0))),
            new(IntegrityJob(db), Describe("DBCC CHECKDB săptămânal."), "Verificare integritate", "master",
                $"DBCC CHECKDB ({SqlText.SqlLiteral(db)}) WITH NO_INFOMSGS, ALL_ERRORMSGS;",
                Weekly(settings.IntegrityCheckDay, settings.IntegrityCheckTime)),
            new(IndexMaintenanceJob(db), Describe("Reorganizare/reconstruire indecși și statistici (core.usp_Maintenance_IndexesAndStatistics)."),
                "Mentenanță indecși", db, "EXEC core.usp_Maintenance_IndexesAndStatistics;", Weekly(DayOfWeek.Sunday, new TimeOnly(4, 0))),
        ];
    }

    public static int? ParseVersion(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var index = description.IndexOf(VersionMarker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var digits = new string(description[(index + VersionMarker.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }

    private static string Describe(string text) => $"{VersionMarker}{DefinitionVersion} | {text}";

    private static string BackupCommand(BackupSettings settings, string kind, string backupType, string extension)
    {
        var suffix = kind == "FULL" ? "_FULL_" : "_LOG_";
        return $"""
            DECLARE @Db   sysname       = {SqlText.SqlLiteral(settings.DatabaseName)};
            DECLARE @Dir  nvarchar(400) = {SqlText.SqlLiteral(BackupFolder(settings, kind))};
            DECLARE @Now  datetime2(0)  = SYSUTCDATETIME();
            DECLARE @File nvarchar(500) = @Dir + @Db + N'{suffix}'
                + CONVERT(nchar(8), @Now, 112) + N'_' + REPLACE(CONVERT(nchar(8), @Now, 108), N':', N'') + N'.{extension}';

            BACKUP {backupType} @Db TO DISK = @File WITH CHECKSUM, COMPRESSION, INIT, STATS = 10;
            RESTORE VERIFYONLY FROM DISK = @File WITH CHECKSUM;
            """;
    }

    private static string CleanupCommand(BackupSettings settings) => $"""
        DECLARE @Cutoff datetime = DATEADD(DAY, -{settings.RetentionDays.ToString(CultureInfo.InvariantCulture)}, GETDATE());
        EXEC master.dbo.xp_delete_file 0, {SqlText.SqlLiteral(BackupFolder(settings, "FULL"))}, N'bak', @Cutoff, 0;
        EXEC master.dbo.xp_delete_file 0, {SqlText.SqlLiteral(BackupFolder(settings, "LOG"))}, N'trn', @Cutoff, 0;
        EXEC msdb.dbo.sp_delete_backuphistory @oldest_date = @Cutoff;
        """;

    private static int Time(TimeOnly time) => time.Hour * 10000 + time.Minute * 100;

    private static AgentSchedule Daily(TimeOnly time) => new(4, 1, 1, 0, 0, Time(time));

    // sp_add_schedule: freq_type 8 = saptamanal; freq_interval = masca zilelor (duminica = 1, luni = 2, …, sambata = 64).
    private static AgentSchedule Weekly(DayOfWeek day, TimeOnly time) => new(8, 1 << (int)day, 1, 0, 1, Time(time));
}
