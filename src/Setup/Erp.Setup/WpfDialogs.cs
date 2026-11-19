using System.Diagnostics;
using System.Windows;
using Erp.Setup.Presentation;
using Microsoft.Win32;

namespace Erp.Setup;

/// <summary>Dialogurile asistentului, cu ferestrele standard WPF.</summary>
public sealed class WpfDialogs(Window owner) : ISetupDialogs
{
    public string? AskOpenLicense()
    {
        var dialog = new OpenFileDialog { Filter = "Licență ERP (*.lic)|*.lic" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskOpenAnswers()
    {
        var dialog = new OpenFileDialog { Filter = "Fișier de răspunsuri (*.json)|*.json" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskSaveAnswers(string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Filter = "Fișier de răspunsuri (*.json)|*.json", FileName = suggestedFileName, OverwritePrompt = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskSaveRecoveryKey(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Cheie de recuperare (*.pfx)|*.pfx",
            FileName = suggestedFileName,
            OverwritePrompt = true,
            InitialDirectory = RemovableDrive(),
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void CopyToClipboard(string text) => Clipboard.SetText(text);

    public void Open(string pathOrUrl) => Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true })?.Dispose();

    public void ShowError(string title, string message) => MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Implicit pe un stick USB, daca exista (5.3, ecranul 6): cheia se pastreaza separat de server si de backup-uri.</summary>
    private static string RemovableDrive() =>
        System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == System.IO.DriveType.Removable && d.IsReady)?.RootDirectory.FullName
        ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
