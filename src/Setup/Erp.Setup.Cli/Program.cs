using System.Text;
using Erp.Setup.Cli;
using Erp.Setup.Core;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("ErpSetup.Cli rulează numai pe Windows.");
    return ExitCodes.UnsupportedOsOrNotAdministrator;
}

Console.OutputEncoding = Encoding.UTF8;

// 08, 5.2: o singura rulare a installer-ului pe calculator (fereastra sau linia de comanda).
using var mutex = new Mutex(initiallyOwned: false, SingleRun.MutexName);
bool owned;
try
{
    owned = mutex.WaitOne(TimeSpan.Zero);
}
catch (AbandonedMutexException)
{
    owned = true;
}

if (!owned)
{
    Console.Error.WriteLine(SingleRun.AlreadyRunningMessage);
    return ExitCodes.CancelledByOperator;
}

try
{
    var pause = ConsoleWindow.ShouldPause(ConsoleWindow.OwnedByThisProcess(), Console.IsInputRedirected, args);
    if (pause && args.Length == 0)
    {
        Console.WriteLine("ErpSetup.Cli se rulează dintr-o consolă deschisă ca administrator, din folderul kitului, de exemplu:");
        Console.WriteLine(@"  .\ErpSetup.Cli.exe check --answers C:\Kit\answers-PROD.json");
        Console.WriteLine("Pentru instalarea obișnuită folosiți ErpSetup.exe (fereastra).");
        Console.WriteLine();
    }

    var runner = new CommandRunner(TimeProvider.System);
    var exitCode = new SetupCommandLine(runner.Run, Console.Out, Console.Error).Invoke(args);
    if (pause)
    {
        ConsoleWindow.WaitForEnter(Console.Out);
    }

    return exitCode;
}
finally
{
    mutex.ReleaseMutex();
}
