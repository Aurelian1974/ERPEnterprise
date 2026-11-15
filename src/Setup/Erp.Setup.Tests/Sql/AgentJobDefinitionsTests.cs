using Erp.Setup.Sql;
using Erp.Setup.Sql.Agent;

namespace Erp.Setup.Tests.Sql;

public sealed class AgentJobDefinitionsTests
{
    private static readonly BackupSettings Settings = new("ERP_PROD", @"B:\SQLBackup", new TimeOnly(1, 0), 15, 14, DayOfWeek.Sunday, new TimeOnly(3, 0));

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public void Five_jobs_with_documented_names_versioned_descriptions_and_schedules()
    {
        var jobs = AgentJobDefinitions.For(Settings).ToDictionary(j => j.Name);

        Assert.Equal(
            ["ERP ERP_PROD - Backup complet", "ERP ERP_PROD - Backup log", "ERP ERP_PROD - Curățare backup", "ERP ERP_PROD - Verificare integritate", "ERP ERP_PROD - Mentenanță indecși"],
            jobs.Keys);
        Assert.All(jobs.Values, j => Assert.StartsWith("ERP-JOB v1 | ", j.Description, StringComparison.Ordinal));
        Assert.Equal(new AgentSchedule(4, 1, 1, 0, 0, 10000), jobs["ERP ERP_PROD - Backup complet"].Schedule);
        Assert.Equal(new AgentSchedule(4, 1, 4, 15, 0, 0), jobs["ERP ERP_PROD - Backup log"].Schedule);
        Assert.Equal(new AgentSchedule(4, 1, 1, 0, 0, 50000), jobs["ERP ERP_PROD - Curățare backup"].Schedule);
        Assert.Equal(new AgentSchedule(8, 1, 1, 0, 1, 30000), jobs["ERP ERP_PROD - Verificare integritate"].Schedule);
        Assert.Equal(new AgentSchedule(8, 1, 1, 0, 1, 40000), jobs["ERP ERP_PROD - Mentenanță indecși"].Schedule);
        Assert.Equal("ERP_PROD", jobs["ERP ERP_PROD - Mentenanță indecși"].StepDatabase);
        Assert.Equal("master", jobs["ERP ERP_PROD - Backup complet"].StepDatabase);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 2)]
    [InlineData(DayOfWeek.Saturday, 64)]
    public void Integrity_day_is_a_weekday_bitmask(DayOfWeek day, int mask)
    {
        var job = AgentJobDefinitions.For(Settings with { IntegrityCheckDay = day }).Single(j => j.Name.EndsWith("Verificare integritate", StringComparison.Ordinal));

        Assert.Equal(mask, job.Schedule.FreqInterval);
    }

    [Fact]
    public void Backup_commands_follow_section_8_13()
    {
        var jobs = AgentJobDefinitions.For(Settings);
        var full = jobs[0].Command;
        var log = jobs[1].Command;
        var cleanup = jobs[2].Command;

        Assert.Contains(@"DECLARE @Dir  nvarchar(400) = N'B:\SQLBackup\ERP_PROD\FULL\';", full);
        Assert.Contains("BACKUP DATABASE @Db TO DISK = @File WITH CHECKSUM, COMPRESSION, INIT, STATS = 10;", full);
        Assert.Contains("RESTORE VERIFYONLY FROM DISK = @File WITH CHECKSUM;", full);
        Assert.Contains(@"N'B:\SQLBackup\ERP_PROD\LOG\'", log);
        Assert.Contains("BACKUP LOG @Db", log);
        Assert.Contains("N'.trn'", log);
        Assert.Contains("DATEADD(DAY, -14, GETDATE())", cleanup);
        Assert.Contains(@"xp_delete_file 0, N'B:\SQLBackup\ERP_PROD\FULL\', N'bak', @Cutoff, 0;", cleanup);
        Assert.Contains("sp_delete_backuphistory", cleanup);
        Assert.DoesNotContain("UPGRADE", cleanup);
        Assert.Equal("DBCC CHECKDB (N'ERP_PROD') WITH NO_INFOMSGS, ALL_ERRORMSGS;", jobs[3].Command);
    }

    [Theory]
    [InlineData(@"B:\SQLBackup", "UPGRADE", @"B:\SQLBackup\ERP_PROD\UPGRADE\")]
    [InlineData(@"\\nas\sql\", "LOG", @"\\nas\sql\ERP_PROD\LOG\")]
    [InlineData("/var/opt/mssql/backup", "FULL", "/var/opt/mssql/backup/ERP_PROD/FULL/")]
    public void Backup_folder_uses_the_separator_of_the_root(string root, string kind, string expected)
    {
        Assert.Equal(expected, AgentJobDefinitions.BackupFolder(Settings with { BackupRoot = root }, kind));
    }

    [Theory]
    [InlineData(@"B:\Backup'; DROP DATABASE x; --", "ERP_PROD")]
    [InlineData(@"B:\SQLBackup", "ERP]; DROP")]
    public void Unsafe_names_or_paths_are_refused(string root, string database)
    {
        Assert.Throws<SqlSetupException>(() => AgentJobDefinitions.For(Settings with { BackupRoot = root, DatabaseName = database }));
    }

    [Theory]
    [InlineData("ERP-JOB v1 | Backup", 1)]
    [InlineData("ERP-JOB v12 | x", 12)]
    [InlineData("Backup manual", null)]
    [InlineData(null, null)]
    public void Definition_version_is_read_from_the_description(string? description, int? expected)
    {
        Assert.Equal(expected, AgentJobDefinitions.ParseVersion(description));
    }
}
