using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Erp.LicenseGen.Core;
using Microsoft.Win32;

namespace Erp.LicenseGen;

/// <summary>Dialogurile generatorului, cu ferestrele standard WPF (fara biblioteci de controale, R1).</summary>
public sealed class WpfDialogs(Window owner) : IGeneratorDialogs
{
    private const string LicenseFilter = "Licență ERP (*.lic)|*.lic";
    private const string BackupFilter = "Copie de siguranță a cheilor (*.erpkeys)|*.erpkeys";

    public string? AskSaveLicensePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Filter = LicenseFilter, FileName = suggestedFileName, OverwritePrompt = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskOpenLicensePath()
    {
        var dialog = new OpenFileDialog { Filter = LicenseFilter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskSaveBackupPath()
    {
        var dialog = new SaveFileDialog { Filter = BackupFilter, FileName = "erp-licente-chei.erpkeys", OverwritePrompt = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskOpenBackupPath()
    {
        var dialog = new OpenFileDialog { Filter = BackupFilter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskNewPassword(string title)
    {
        while (true)
        {
            var (first, second) = PasswordPrompt(title, confirm: true);
            if (first is null)
            {
                return null;
            }

            if (first != second)
            {
                ShowError(title, "Parolele nu coincid.");
            }
            else if (first.Length < KeyStore.MinimumBackupPasswordLength)
            {
                ShowError(title, $"Parola trebuie să aibă cel puțin {KeyStore.MinimumBackupPasswordLength} caractere.");
            }
            else
            {
                return first;
            }
        }
    }

    public string? AskPassword(string title) => PasswordPrompt(title, confirm: false).First;

    public bool? AskKeyAction() => MessageBox.Show(owner,
        "Da: configurare nouă (generează cheile 1 și 2, doar dacă nu există încă).\nNu: importă o cheie din copia de siguranță (restaurare sau rotire).",
        "Cheile de semnare", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    {
        MessageBoxResult.Yes => true,
        MessageBoxResult.No => false,
        _ => null,
    };

    public byte? AskKeyId()
    {
        var box = new TextBox { Text = "2", Margin = new Thickness(0, 8, 0, 12) };
        return Prompt("Cheia de importat", "Numărul cheii (KeyId, 1–255):", box) && byte.TryParse(box.Text, out var id) && id > 0 ? id : null;
    }

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowText(string title, string text)
    {
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
            Height = 260, Width = 760, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12),
        };
        Prompt(title, string.Empty, box, okText: "Închide");
    }

    public void OpenWithDefaultApp(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private (string? First, string? Second) PasswordPrompt(string title, bool confirm)
    {
        var first = new PasswordBox { Margin = new Thickness(0, 4, 0, 8) };
        var second = new PasswordBox { Margin = new Thickness(0, 4, 0, 12) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Parola:" });
        panel.Children.Add(first);
        if (confirm)
        {
            panel.Children.Add(new TextBlock { Text = "Repetați parola:" });
            panel.Children.Add(second);
        }

        return Prompt(title, string.Empty, panel) ? (first.Password, confirm ? second.Password : first.Password) : (null, null);
    }

    private bool Prompt(string title, string message, UIElement content, string okText = "OK")
    {
        var window = new Window
        {
            Title = title, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, MinWidth = 380,
        };
        var ok = new Button { Content = okText, IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "Renunță", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(18) };
        if (message.Length > 0)
        {
            root.Children.Add(new TextBlock { Text = message });
        }

        root.Children.Add(content);
        root.Children.Add(buttons);
        window.Content = root;
        return window.ShowDialog() == true;
    }
}
