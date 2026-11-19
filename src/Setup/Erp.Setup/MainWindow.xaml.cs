using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Erp.Setup.Presentation;

namespace Erp.Setup;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private SetupWizardViewModel ViewModel => (SetupWizardViewModel)DataContext;

    // PasswordBox nu permite legarea parolei; o trecem explicit in ViewModel si o stergem dupa salvare.
    private void RecoveryPasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.RecoveryKeyPassword = RecoveryPassword.Password;
        ViewModel.RecoveryKeyPasswordConfirm = RecoveryPasswordConfirm.Password;
    }

    private void ClearRecoveryPasswords(object sender, RoutedEventArgs e)
    {
        RecoveryPassword.Clear();
        RecoveryPasswordConfirm.Clear();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Pe ecranul 6, fereastra nu se inchide inainte de bifa „Am notat parola” si de salvarea cheii de recuperare (5.3).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is SetupWizardViewModel { CurrentPage: WizardPage.Done, CanClose: false })
        {
            MessageBox.Show(this, "Bifați „Am notat parola” și salvați cheia de recuperare înainte de a închide.", "ERP Setup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
        }
        else if (DataContext is SetupWizardViewModel { IsInstalling: true })
        {
            MessageBox.Show(this, "Instalarea rulează; așteptați terminarea pasului curent.", "ERP Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}

/// <summary>Vizibil cand pagina curenta e una din cele date (de exemplu „Database|Site”).</summary>
public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is WizardPage page && parameter is string pages && pages.Split('|').Contains(page.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
