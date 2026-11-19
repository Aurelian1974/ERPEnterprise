using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Erp.LicenseGen;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}

/// <summary>Inversul unei valori bool (de exemplu „Nelimitat” bifat dezactiveaza data).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
