namespace Erp.Setup.Core;

/// <summary>Rezultatul verificarii unei chei de recuperare (08, 5.12, 11.4).</summary>
public enum RecoveryKeyCheck
{
    Valid,
    WrongPassword,
    NoPrivateKey,
    OtherInstallation,
}

/// <summary>Regulile cheii de recuperare, comune ferestrei si liniei de comanda (08, 5.3, ecranul 6).</summary>
public static class RecoveryKeyPolicy
{
    public const int MinimumPasswordLength = 12;

    public static string Message(RecoveryKeyCheck check) => check switch
    {
        RecoveryKeyCheck.WrongPassword => "Parola cheii de recuperare e greșită sau fișierul nu e o cheie de recuperare.",
        RecoveryKeyCheck.NoPrivateKey => "Fișierul nu conține cheia privată.",
        RecoveryKeyCheck.OtherInstallation => "Cheia de recuperare aparține altei instalări.",
        RecoveryKeyCheck.Valid => "Cheia de recuperare e validă.",
        _ => "Cheia de recuperare nu e validă.",
    };
}
