using System.Windows;
using Microsoft.Win32;

namespace UE4SSManager.App.Services;

public enum Choice { Yes, No, Cancel }

/// <summary>Message boxes and file pickers, kept out of the view model.</summary>
public sealed class Dialogs(Window owner)
{
    private const string Title = "UE4SS Manager";

    public bool Confirm(string message, string? title = null) =>
        MessageBox.Show(owner, message, title ?? Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public Choice Ask(string message, string? title = null) =>
        MessageBox.Show(owner, message, title ?? Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => Choice.Yes,
            MessageBoxResult.No => Choice.No,
            _ => Choice.Cancel,
        };

    public void Info(string message) => MessageBox.Show(owner, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Error(string message) => MessageBox.Show(owner, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public string[] PickZips(string title, bool multiple = true)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = "Zip archives (*.zip)|*.zip|All files (*.*)|*.*", Multiselect = multiple };
        return dlg.ShowDialog(owner) == true ? dlg.FileNames : [];
    }

    public string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog(owner) == true ? dlg.FolderName : null;
    }

    public string? PickGame()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick the game's exe (any exe inside the game folder works)",
            Filter = "Programs (*.exe)|*.exe",
        };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }
}
