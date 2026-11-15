namespace Erp.Setup.Core;

/// <summary>Codurile de iesire din INSTALARE_ACTUALIZARE.md, 6.4.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int SuccessWithWarnings = 1;
    public const int CancelledByOperator = 2;
    public const int InvalidCommandLine = 3;
    public const int UnsupportedOsOrNotAdministrator = 10;
    public const int InvalidAnswersFile = 11;
    public const int RequirementsNotMet = 12;
    public const int InvalidKitSignature = 20;
    public const int KitFilesMissingOrModified = 21;
    public const int SqlInstallError = 30;
    public const int DatabaseExistsNotEmpty = 31;
    public const int WrongDatabaseCollation = 32;
    public const int InvalidLicense = 40;
    public const int IisServiceOrFileError = 50;
    public const int HealthCheckFailed = 60;
    public const int UpgradeFailedRolledBack = 70;
    public const int UpgradeFailedRollbackFailed = 71;
    public const int UnsupportedUpgradeVersion = 72;
    public const int UnfinishedUpgrade = 73;

    // Temporar, pana la implementarea comenzilor (T5-T15); nu face parte din contractul 6.4.
    public const int CommandNotImplemented = 99;
}
