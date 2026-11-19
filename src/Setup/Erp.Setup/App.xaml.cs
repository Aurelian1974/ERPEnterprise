using System.Windows;
using Erp.Setup.Core;
using Erp.Setup.Core.Kit;
using Erp.Setup.Presentation;
using Erp.Setup.Windows.Security;

namespace Erp.Setup;

/// <summary>
/// Pornirea ErpSetup.exe: o singura rulare pe calculator (mutexul comun cu linia de comanda, 5.2), verificarea installer-ului
/// (Authenticode) si a kitului (C-05) inainte de orice ecran, apoi asistentul.
/// </summary>
public partial class App : Application
{
    private Mutex? mutex;
    private SetupBackend? backend;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(initiallyOwned: false, SingleRun.MutexName);
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
            MessageBox.Show(SingleRun.AlreadyRunningMessage + $" (cod {ExitCodes.CancelledByOperator})", "ERP Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            mutex.Dispose();
            mutex = null;
            Shutdown(ExitCodes.CancelledByOperator);
            return;
        }

        var kitRoot = AppContext.BaseDirectory;
        var kit = new KitVerifier(KitPublicKey.Load()).Verify(kitRoot);
        if (!kit.Succeeded)
        {
            Fail($"{kit.Message}{Environment.NewLine}{string.Join(Environment.NewLine, kit.Details.Take(10))}", kit.ExitCode);
            return;
        }

#if !DEBUG
        if (!AuthenticodeVerifier.IsTrusted(Environment.ProcessPath!))
        {
            Fail("Installer-ul nu este semnat de producător sau a fost modificat.", ExitCodes.InvalidKitSignature);
            return;
        }
#endif

        backend = new SetupBackend(kitRoot, kit.Manifest!, TimeProvider.System);
        var window = new MainWindow();
        window.DataContext = new SetupWizardViewModel(backend, new WpfDialogs(window));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        backend?.Dispose();
        if (mutex is not null)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }

        base.OnExit(e);
    }

    private void Fail(string message, int exitCode)
    {
        MessageBox.Show($"{message}{Environment.NewLine}(cod {exitCode})", "ERP Setup", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(exitCode);
    }
}

/// <summary>Cheile publice ale kitului incorporate la build (08, 7).</summary>
internal static class KitPublicKey
{
    public static IReadOnlyDictionary<int, string> Load() => KitPublicKeys.FromAssembly(typeof(KitPublicKey).Assembly, "Erp.Setup");
}
