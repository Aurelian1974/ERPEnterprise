namespace Erp.Setup.Core;

/// <summary>Rezultatul verificarii unei chei de recuperare (08, 5.12, 11.4).</summary>
public enum RecoveryKeyCheck
{
    Valid,
    WrongPassword,
    NoPrivateKey,
    OtherInstallation,
}
