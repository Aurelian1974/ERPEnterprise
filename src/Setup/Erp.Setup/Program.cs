using System.Text;
using Erp.Setup;
using Erp.Setup.Cli;
using Erp.Setup.Core;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("ErpSetup rulează numai pe Windows Server.");
    return ExitCodes.UnsupportedOsOrNotAdministrator;
}

Console.OutputEncoding = Encoding.UTF8;
var pause = ConsoleWindow.ShouldPause(ConsoleWindow.OwnedByThisProcess(), Console.IsInputRedirected, args);
if (pause && args.Length == 0)
{
    Console.WriteLine("ErpSetup este o aplicație de consolă: se rulează dintr-o fereastră PowerShell deschisă ca administrator, din folderul kitului, de exemplu:");
    Console.WriteLine(@"  .\ErpSetup.exe check --answers C:\Kit\answers-PROD.json");
    Console.WriteLine();
}

var runner = new CommandRunner(TimeProvider.System);
var exitCode = new SetupCommandLine(runner.Run, Console.Out, Console.Error).Invoke(args);
if (pause)
{
    ConsoleWindow.WaitForEnter(Console.Out);
}

return exitCode;
