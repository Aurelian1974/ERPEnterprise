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

    // PasswordBox nu permite legarea parolei; o trecem explicit in ViewModel (Tag 1 = parola, 2 = confirmarea) si o stergem dupa folosire.
    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (System.Windows.Controls.PasswordBox)sender;
        if (Equals(box.Tag, "2"))
        {
            ViewModel.RecoveryKeyPasswordConfirm = box.Password;
        }
        else
        {
            ViewModel.RecoveryKeyPassword = box.Password;
        }
    }

    private void ClearPasswords(object sender, RoutedEventArgs e)
    {
        foreach (var box in FindPasswordBoxes(this))
        {
            box.Clear();
        }
    }

    private static IEnumerable<System.Windows.Controls.PasswordBox> FindPasswordBoxes(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.PasswordBox box)
            {
                yield return box;
            }

            foreach (var nested in FindPasswordBoxes(child))
            {
                yield return nested;
            }
        }
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

public sealed class HiddenWhenTrueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NotZeroVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int and not 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Vizibil pentru actiunea data (de exemplu „RecoveryKey”).</summary>
public sealed class ActionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ActionKind kind && parameter is string name && kind.ToString() == name ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
