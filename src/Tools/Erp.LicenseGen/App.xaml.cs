using System.Windows;
using Erp.LicenseGen.Core;

namespace Erp.LicenseGen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        var dialogs = new WpfDialogs(window);
        window.DataContext = new GeneratorViewModel(
            new KeyStore(KeyStore.DefaultFolder, new DpapiCurrentUserProtector()),
            new LicenseRegistry(LicenseRegistry.DefaultPath),
            dialogs,
            TimeProvider.System);
        MainWindow = window;
        window.Show();
    }
}
