using System.IO;
using System.Windows;
using UE4SSManager.App.Services;
using UE4SSManager.Core;
using UE4SSManager.Core.Releases;

namespace UE4SSManager.App;

public partial class OptionsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _unhide;

    public OptionsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        SourceInitialized += (_, _) => Native.UseDarkTitleBar(this);
        foreach (var f in settings.LibraryFolders) Folders.Items.Add(f);
        Repo.Text = settings.Repository;
        Token.Text = settings.GitHubToken ?? "";
        RemovedText.Text = settings.RemovedGames.Count == 0 ? "No games removed from the list." : $"{settings.RemovedGames.Count} games removed from the list.";
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pick a folder that holds games (e.g. E:\\GOG)" };
        if (dlg.ShowDialog(this) == true && !Folders.Items.Contains(dlg.FolderName)) Folders.Items.Add(dlg.FolderName);
    }

    private void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (Folders.SelectedItem is { } item) Folders.Items.Remove(item);
    }

    private void OnUnhide(object sender, RoutedEventArgs e)
    {
        _unhide = true;
        RemovedText.Text = "Removed games come back after saving.";
    }

    private void OnOpenCache(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.CacheDir);
        Native.ShowInExplorer(AppSettings.CacheDir);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.AppDataDir);
        Native.ShowInExplorer(AppSettings.AppDataDir);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.LibraryFolders = Folders.Items.Cast<string>().ToList();
        _settings.Repository = string.IsNullOrWhiteSpace(Repo.Text) ? ReleaseClient.DefaultRepository : Repo.Text.Trim();
        _settings.GitHubToken = string.IsNullOrWhiteSpace(Token.Text) ? null : Token.Text.Trim();
        if (_unhide) _settings.RemovedGames.Clear();
        DialogResult = true;
    }
}
