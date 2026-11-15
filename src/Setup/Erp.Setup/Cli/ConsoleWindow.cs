using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Erp.Setup.Cli;

/// <summary>
/// Pornit cu dublu-clic, ErpSetup primeste o fereastra de consola proprie, pe care Windows o inchide la iesire;
/// atunci se asteapta Enter, ca operatorul sa poata citi mesajele.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ConsoleWindow
{
    public static bool OwnedByThisProcess()
    {
        var processes = new uint[2];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    /// <summary>Fara pauza in modul automat si cu intrarea redirectionata: o sarcina programata ar ramane blocata.</summary>
    public static bool ShouldPause(bool ownedByThisProcess, bool inputRedirected, IReadOnlyList<string> args) =>
        ownedByThisProcess && !inputRedirected && !args.Contains("--unattended", StringComparer.OrdinalIgnoreCase);

    public static void WaitForEnter(TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Apăsați Enter pentru a închide fereastra.");
        Console.ReadLine();
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
