using System.Globalization;
using System.Text.RegularExpressions;

namespace Erp.Setup.Sql.Database;

/// <summary>
/// Textul comenzilor DDL pentru baza ERP (8.2). DDL-ul nu accepta parametri: numele se valideaza si se citeaza cu
/// <see cref="SqlText.QuoteName"/>, caile cu <see cref="SqlText.SqlLiteral"/>, numerele cu InvariantCulture (abaterea SP-06, 17).
/// </summary>
public static partial class DatabaseScripts
{
    public const string Collation = "Latin1_General_100_CS_AS_SC_UTF8";
    public const int CompatibilityLevel = 160;

    public static void ValidateDatabaseName(string name)
    {
        if (!DatabaseName().IsMatch(name))
        {
            throw new SqlSetupException($"Numele bazei „{name}” este invalid (litere, cifre și _, 3–64 de caractere, începând cu o literă).");
        }
    }

    /// <summary>Cale absoluta pe serverul SQL (Windows, UNC sau, pentru SQL Server pe Linux, cu „/”), fara ' ; -- sau caractere de control.</summary>
    public static void ValidatePath(string path)
    {
        var absolute = DriveAbsolute().IsMatch(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith('/');
        if (!absolute || path.Any(char.IsControl) || path.Contains('\'') || path.Contains(';')
            || path.Contains("--", StringComparison.Ordinal) || path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
        {
            throw new SqlSetupException($"Calea SQL „{path}” este invalidă (absolută, fără ' ; -- sau caractere speciale).");
        }
    }

    public static string CombinePath(string folder, string fileName)
    {
        var separator = folder.Contains('\\') || !folder.Contains('/') ? '\\' : '/';
        return folder.TrimEnd('\\', '/') + separator + fileName;
    }

    public static string CreateDatabase(string name, string dataFile, string logFile, int dataSizeMb, int logSizeMb)
    {
        ValidateDatabaseName(name);
        ValidatePath(dataFile);
        ValidatePath(logFile);
        var growth = 512.ToString(CultureInfo.InvariantCulture);
        return $"""
            CREATE DATABASE {SqlText.QuoteName(name)}
            ON PRIMARY
            (
                NAME = {SqlText.SqlLiteral(name + "_data")},
                FILENAME = {SqlText.SqlLiteral(dataFile)},
                SIZE = {dataSizeMb.ToString(CultureInfo.InvariantCulture)}MB,
                FILEGROWTH = {growth}MB
            )
            LOG ON
            (
                NAME = {SqlText.SqlLiteral(name + "_log")},
                FILENAME = {SqlText.SqlLiteral(logFile)},
                SIZE = {logSizeMb.ToString(CultureInfo.InvariantCulture)}MB,
                FILEGROWTH = {growth}MB
            )
            COLLATE {Collation};
            """;
    }

    /// <summary>Setarile bazei (8.2), fiecare executata separat; idempotente.</summary>
    public static IReadOnlyList<(string Description, string Sql)> Settings(string name)
    {
        ValidateDatabaseName(name);
        var db = SqlText.QuoteName(name);
        return
        [
            ("Recovery FULL", $"ALTER DATABASE {db} SET RECOVERY FULL;"),
            ("READ_COMMITTED_SNAPSHOT", $"ALTER DATABASE {db} SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;"),
            ("ALLOW_SNAPSHOT_ISOLATION OFF", $"ALTER DATABASE {db} SET ALLOW_SNAPSHOT_ISOLATION OFF;"),
            ("PAGE_VERIFY CHECKSUM", $"ALTER DATABASE {db} SET PAGE_VERIFY CHECKSUM;"),
            ("AUTO_CLOSE OFF", $"ALTER DATABASE {db} SET AUTO_CLOSE OFF;"),
            ("AUTO_SHRINK OFF", $"ALTER DATABASE {db} SET AUTO_SHRINK OFF;"),
            ("AUTO_CREATE_STATISTICS ON", $"ALTER DATABASE {db} SET AUTO_CREATE_STATISTICS ON;"),
            ("AUTO_UPDATE_STATISTICS ON", $"ALTER DATABASE {db} SET AUTO_UPDATE_STATISTICS ON;"),
            ("AUTO_UPDATE_STATISTICS_ASYNC ON", $"ALTER DATABASE {db} SET AUTO_UPDATE_STATISTICS_ASYNC ON;"),
            ("Nivel de compatibilitate 160", $"ALTER DATABASE {db} SET COMPATIBILITY_LEVEL = {CompatibilityLevel.ToString(CultureInfo.InvariantCulture)};"),
            ("TARGET_RECOVERY_TIME 60 s", $"ALTER DATABASE {db} SET TARGET_RECOVERY_TIME = 60 SECONDS;"),
            ("Query Store", $"ALTER DATABASE {db} SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, MAX_STORAGE_SIZE_MB = 2048);"),
        ];
    }

    public static string AlterAuthorization(string name, string owner)
    {
        ValidateDatabaseName(name);
        return $"ALTER AUTHORIZATION ON DATABASE::{SqlText.QuoteName(name)} TO {SqlText.QuoteName(owner)};";
    }

    /// <summary>Proprietate extinsa la nivel de baza: adaugata sau actualizata (parametrizat).</summary>
    public const string UpsertExtendedProperty = """
        IF EXISTS (SELECT 1 FROM sys.extended_properties AS p WHERE p.class = 0 AND p.name = @Name)
            EXEC sys.sp_updateextendedproperty @name = @Name, @value = @Value;
        ELSE
            EXEC sys.sp_addextendedproperty @name = @Name, @value = @Value;
        """;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseName();

    [GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex DriveAbsolute();
}
