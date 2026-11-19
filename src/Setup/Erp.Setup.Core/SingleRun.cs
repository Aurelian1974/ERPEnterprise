namespace Erp.Setup.Core;

/// <summary>O singura rulare a installer-ului pe calculator (08, 5.2), comuna ferestrei si liniei de comanda.</summary>
public static class SingleRun
{
    public const string MutexName = @"Global\Erp.Setup";

    public const string AlreadyRunningMessage = "Installer-ul rulează deja pe acest calculator.";
}
