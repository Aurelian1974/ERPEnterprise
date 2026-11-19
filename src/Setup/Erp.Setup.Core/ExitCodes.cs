namespace Erp.Setup.Core;

/// <summary>Codurile de iesire (08, 6.3, decizia D28): contractul liniei de comanda; fereastra afiseaza aceleasi coduri.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int SuccessWithWarnings = 1;

    /// <summary>Anulat de operator, sau installer-ul ruleaza deja pe calculator.</summary>
    public const int CancelledByOperator = 2;
    public const int InvalidCommandLine = 3;
    public const int UnsupportedOsOrNotAdministrator = 10;
    public const int InvalidAnswersFile = 11;
    public const int RequirementsNotMet = 12;
    public const int InvalidKitSignature = 20;
    public const int KitFilesMissingOrModified = 21;
    public const int SqlInstallError = 30;

    /// <summary>Baza exista si contine date, sau apartine altei instalari.</summary>
    public const int DatabaseExistsNotEmpty = 31;
    public const int WrongDatabaseCollation = 32;
    public const int InvalidLicense = 40;

    /// <summary>Licenta nu include actualizari pentru data de lansare a kitului (08, 4.5).</summary>
    public const int LicenseExcludesKit = 41;
    public const int IisServiceOrFileError = 50;
    public const int HealthCheckFailed = 60;
    public const int UpgradeFailedRolledBack = 70;
    public const int UpgradeFailedRollbackFailed = 71;
    public const int UnsupportedUpgradeVersion = 72;
    public const int UnfinishedUpgrade = 73;

    /// <summary>Kitul nu are versiunea ceruta: „Repara” si „Recupereaza” cer kitul versiunii instalarii, respectiv al bazei.</summary>
    public const int KitVersionMismatch = 74;

    /// <summary>Cheia de recuperare lipseste, nu corespunde instalarii sau parola ei e gresita.</summary>
    public const int RecoveryKeyInvalid = 80;
}
