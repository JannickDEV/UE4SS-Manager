using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE4SSManager.App.Services;
using UE4SSManager.Core;
using UE4SSManager.Core.Games;
using UE4SSManager.Core.Install;
using UE4SSManager.Core.Mods;
using UE4SSManager.Core.Releases;
using UE4SSManager.Core.Util;

namespace UE4SSManager.App.ViewModels;

public enum Channel { Stable, Experimental }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dialogs _dialogs;
    private readonly AppSettings _settings;
    private ReleaseClient _releases;
    private readonly Ue4ssInstaller _installer = new();
    private IReadOnlyList<Ue4ssRelease> _allReleases = [];
    private bool _loadingSettings;

    public MainViewModel(Dialogs dialogs)
    {
        _dialogs = dialogs;
        _settings = AppSettings.Load();
        _releases = CreateReleaseClient();
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => string.IsNullOrWhiteSpace(SearchText) ||
                                ((GameItem)o).Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private ReleaseClient CreateReleaseClient() =>
        new(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, AppSettings.CacheDir, string.IsNullOrWhiteSpace(_settings.Repository) ? ReleaseClient.DefaultRepository : _settings.Repository, _settings.GitHubToken);

    public string AppVersion => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    // ---------------------------------------------------------------- games

    public ObservableCollection<GameItem> Games { get; } = [];
    public ICollectionView GamesView { get; }

    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value) => GamesView.Refresh();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasGame))] private GameItem? _selectedGame;
    public bool HasGame => SelectedGame is not null;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _gamesSummary = "";

    partial void OnSelectedGameChanged(GameItem? value)
    {
        if (value is not null)
        {
            _settings.LastGame = value.Game.Key;
            TrySaveSettings();
        }
        RefreshGameState();
        Log.Clear();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(ScanAsync(), LoadReleasesAsync());
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        GamesSummary = "Looking for Unreal Engine games...";
        try
        {
            var detector = GameDetector.CreateDefault(_settings.LibraryFolders);
            var found = await detector.DetectAsync(_settings.ManualGames);
            var hidden = new HashSet<string>(_settings.HiddenGames, StringComparer.OrdinalIgnoreCase);
            var keep = SelectedGame?.Game.Key ?? _settings.LastGame;
            var items = await Task.Run(() => found.Where(g => !hidden.Contains(g.Key))
                .Select(g => { var i = new GameItem(g); i.Status = Ue4ssDetector.Detect(g.Win64Path); return i; }).ToList());
            Games.Clear();
            foreach (var i in items) Games.Add(i);
            SelectedGame = Games.FirstOrDefault(g => g.Game.Key == keep) ?? Games.FirstOrDefault();
            var withUe4ss = Games.Count(g => g.Status?.IsInstalled == true);
            GamesSummary = Games.Count == 0
                ? "No Unreal Engine games found. Use Add game, or add a library folder in Options."
                : $"{Games.Count} games  ·  {withUe4ss} with UE4SS";
        }
        catch (Exception e)
        {
            GamesSummary = "Scan failed: " + e.Message;
        }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void AddGame()
    {
        var exe = _dialogs.PickGame();
        if (exe is null) return;
        var detector = new GameDetector([]);
        var found = detector.FromPath(exe).ToList();
        if (found.Count == 0)
        {
            _dialogs.Error("No Unreal Engine game found there. Pick the game's exe or the *-Shipping.exe in <Game>\\Binaries\\Win64.");
            return;
        }
        foreach (var g in found)
        {
            _settings.HiddenGames.RemoveAll(h => string.Equals(h, g.Key, StringComparison.OrdinalIgnoreCase));
            if (!_settings.ManualGames.Contains(g.ShippingExePath, StringComparer.OrdinalIgnoreCase))
                _settings.ManualGames.Add(g.ShippingExePath);
            var existing = Games.FirstOrDefault(x => x.Game.Key == g.Key);
            if (existing is null)
            {
                existing = new GameItem(g) { Status = Ue4ssDetector.Detect(g.Win64Path) };
                Games.Add(existing);
            }
            SelectedGame = existing;
        }
        TrySaveSettings();
    }

    [RelayCommand]
    private void RemoveGame()
    {
        if (SelectedGame is not { } item) return;
        if (!_dialogs.Confirm($"Remove {item.Name} from the list? Nothing is deleted from disk. You can bring it back in Options."))
            return;
        _settings.ManualGames.RemoveAll(p => string.Equals(p, item.Game.ShippingExePath, StringComparison.OrdinalIgnoreCase));
        if (item.Game.Source != GameSource.Manual && !_settings.HiddenGames.Contains(item.Game.Key))
            _settings.HiddenGames.Add(item.Game.Key);
        TrySaveSettings();
        Games.Remove(item);
        SelectedGame = Games.FirstOrDefault();
    }

    [RelayCommand]
    private void LaunchGame()
    {
        if (SelectedGame?.Game is not { } g) return;
        if (IsGameRunning(g)) { _dialogs.Info($"{g.Name} is already running."); return; }
        if (g.Source == GameSource.Steam && g.StoreId is not null) Native.Open("steam://rungameid/" + g.StoreId);
        else
        {
            try { Process.Start(new ProcessStartInfo(g.ShippingExePath) { UseShellExecute = true, WorkingDirectory = g.Win64Path }); }
            catch (Exception e) { _dialogs.Error("Couldn't start the game: " + e.Message); }
        }
    }

    [RelayCommand] private void OpenGameFolder() { if (SelectedGame is { } g) Native.ShowInExplorer(g.Game.Win64Path); }

    // ---------------------------------------------------------------- UE4SS status

    [ObservableProperty] private Ue4ssStatus? _status;
    [ObservableProperty] private string _statusHeadline = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _hasWarning;

    private void RefreshGameState()
    {
        if (SelectedGame is not { } item)
        {
            Status = null;
            Mods.Clear();
            return;
        }
        var s = Ue4ssDetector.Detect(item.Game.Win64Path);
        item.Status = s;
        Status = s;
        IsInstalled = s.IsInstalled;
        HasWarning = s.ProxyMissing;
        if (!s.IsInstalled)
        {
            StatusHeadline = "UE4SS is not installed";
            StatusDetail = "Pick a version below and press Install.";
        }
        else
        {
            StatusHeadline = $"UE4SS {s.Version ?? "(unknown version)"} installed";
            var parts = new List<string>
            {
                s.ProxyDll is null ? "no proxy DLL found, so UE4SS won't load on its own" : $"loads through {s.ProxyDll}",
                s.Layout == Ue4ssLayout.Folder ? "ue4ss folder layout (3.x)" : "flat layout (2.x)",
            };
            if (s.Manifest is { } m)
            {
                if (m.IsDevBuild) parts.Add("zDEV build");
                if (m.IsExperimental) parts.Add("experimental");
                parts.Add($"installed {m.InstalledAt.LocalDateTime:d MMM yyyy}");
            }
            else parts.Add("installed by hand");
            StatusDetail = string.Join("  ·  ", parts);
        }
        UpdateInstallButton();
        LoadMods();
        LoadQuickSettings();
    }

    // ---------------------------------------------------------------- releases

    public ObservableCollection<ReleaseOption> VisibleReleases { get; } = [];
    [ObservableProperty] private ReleaseOption? _selectedRelease;
    [ObservableProperty] private Channel _channel = Channel.Stable;
    [ObservableProperty] private bool _useDevBuild;
    [ObservableProperty] private bool _devAvailable;
    [ObservableProperty] private string _releaseInfo = "";
    [ObservableProperty] private string _releaseNotice = "";
    [ObservableProperty] private bool _isLoadingReleases;
    [ObservableProperty] private string _installButtonText = "Install";
    [ObservableProperty] private bool _keepUserData = true;

    public bool IsStable { get => Channel == Channel.Stable; set { if (value) Channel = Channel.Stable; } }
    public bool IsExperimental { get => Channel == Channel.Experimental; set { if (value) Channel = Channel.Experimental; } }
    public bool IsNormalBuild { get => !UseDevBuild; set { if (value) UseDevBuild = false; } }

    partial void OnChannelChanged(Channel value)
    {
        OnPropertyChanged(nameof(IsStable));
        OnPropertyChanged(nameof(IsExperimental));
        FillReleaseList();
    }

    partial void OnUseDevBuildChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNormalBuild));
        UpdateReleaseInfo();
    }

    partial void OnSelectedReleaseChanged(ReleaseOption? value) => UpdateReleaseInfo();

    [RelayCommand]
    private async Task LoadReleasesAsync()
    {
        IsLoadingReleases = true;
        ReleaseNotice = "Loading UE4SS releases from GitHub...";
        try
        {
            var result = await _releases.GetReleasesAsync();
            _allReleases = result.Releases;
            ReleaseNotice = result.Error is null ? ""
                : result.FromCache ? result.Error + " Showing the list from the last time it worked."
                : result.Error + " You can still install from a zip you downloaded yourself.";
        }
        finally { IsLoadingReleases = false; }
        FillReleaseList();
    }

    private void FillReleaseList()
    {
        var keepTag = SelectedRelease?.Release.Tag;
        VisibleReleases.Clear();
        var list = _allReleases.Where(r => Channel == Channel.Experimental ? r.IsExperimental : !r.IsExperimental);
        foreach (var r in list) VisibleReleases.Add(new ReleaseOption(r));
        SelectedRelease = VisibleReleases.FirstOrDefault(r => r.Release.Tag == keepTag) ?? VisibleReleases.FirstOrDefault();
        if (VisibleReleases.Count == 0 && _allReleases.Count > 0 && Channel == Channel.Experimental)
            ReleaseInfo = "No experimental build is published right now.";
    }

    private ReleaseAsset? SelectedAsset =>
        SelectedRelease?.Release.PickAsset(UseDevBuild ? AssetKind.Dev : AssetKind.Standard)
        ?? SelectedRelease?.Release.PickAsset(AssetKind.Standard);

    private void UpdateReleaseInfo()
    {
        DevAvailable = SelectedRelease?.Release.HasDevBuild ?? false;
        if (!DevAvailable && UseDevBuild) UseDevBuild = false;
        if (SelectedRelease is null || SelectedAsset is not { } asset)
        {
            ReleaseInfo = _allReleases.Count == 0 ? "" : "Pick a version.";
        }
        else
        {
            var when = SelectedRelease.Release.PublishedAt is { } d ? $"Published {d.LocalDateTime:d MMM yyyy}  ·  " : "";
            ReleaseInfo = $"{when}{asset.Name}  ·  {asset.Size / 1024d / 1024d:0.0} MB";
        }
        UpdateInstallButton();
    }

    private void UpdateInstallButton()
    {
        var version = SelectedRelease?.Release.DisplayVersion;
        if (Status is not { IsInstalled: true } s) InstallButtonText = version is null ? "Install" : $"Install {version}";
        else if (SelectedAsset is { } a && s.Manifest?.AssetName == a.Name) InstallButtonText = "Reinstall";
        else InstallButtonText = version is null ? "Update" : $"Switch to {version}";
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = SelectedRelease?.Release.HtmlUrl ?? $"https://github.com/{_settings.Repository}/releases";
        Native.Open(url);
    }

    // ---------------------------------------------------------------- install / uninstall

    public ObservableCollection<string> Log { get; } = [];
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;

    private void AddLog(string line) => Log.Add($"[{DateTime.Now:HH:mm:ss}] {line}");

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedGame?.Game is not { } game) return;
        if (SelectedRelease?.Release is not { } release || SelectedAsset is not { } asset)
        {
            _dialogs.Error("No UE4SS release is selected. If GitHub can't be reached, use \"Install from zip\" with a release zip you downloaded.");
            return;
        }
        if (!CheckGameNotRunning(game)) return;

        await RunBusyAsync(async () =>
        {
            AddLog($"Downloading {asset.Name}...");
            var zip = await _releases.DownloadAsync(release, asset, new Progress<double>(p => Progress = p * 100));
            ProgressIndeterminate = true;
            var options = BuildOptions(game) with
            {
                Tag = release.Tag,
                Version = asset.Version ?? release.DisplayVersion,
                AssetName = asset.Name,
                IsDevBuild = asset.Kind == AssetKind.Dev,
                IsExperimental = release.IsExperimental,
            };
            await Task.Run(() => _installer.Install(game.Win64Path, zip, options, new Progress<string>(AddLog)));
        });
    }

    [RelayCommand]
    private async Task InstallFromZipAsync()
    {
        if (SelectedGame?.Game is not { } game) return;
        var zip = _dialogs.PickZips("Pick a UE4SS release zip (UE4SS_v*.zip or zDEV-UE4SS_v*.zip)", multiple: false).FirstOrDefault();
        if (zip is null || !CheckGameNotRunning(game)) return;
        await RunBusyAsync(async () =>
        {
            ProgressIndeterminate = true;
            var name = Path.GetFileName(zip);
            var options = BuildOptions(game) with
            {
                Version = Ue4ssRelease.VersionFromAssetName(name),
                AssetName = name,
                IsDevBuild = Ue4ssRelease.Classify(name) == AssetKind.Dev,
            };
            await Task.Run(() => _installer.Install(game.Win64Path, zip, options, new Progress<string>(AddLog)));
        });
    }

    private InstallOptions BuildOptions(GameInstall game) => new()
    {
        KeepUserData = KeepUserData,
        BackupDir = Path.Combine(AppSettings.CacheDir, "backups", PathUtil.SanitizeFileName(game.Name)),
    };

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (SelectedGame?.Game is not { } game || Status is not { IsInstalled: true }) return;
        var choice = _dialogs.Ask(
            $"Remove UE4SS from {game.Name}?\n\nYes: remove UE4SS but keep your mods and UE4SS settings (reinstalling picks them up again).\n" +
            "No: remove UE4SS and delete all its mods and settings.\nCancel: do nothing.", "Uninstall UE4SS");
        if (choice == Choice.Cancel || !CheckGameNotRunning(game)) return;
        if (choice == Choice.No && !_dialogs.Confirm("This deletes every mod in the UE4SS Mods folder. Continue?")) return;
        await RunBusyAsync(async () =>
        {
            ProgressIndeterminate = true;
            await Task.Run(() => _installer.Uninstall(game.Win64Path, keepUserData: choice == Choice.Yes, new Progress<string>(AddLog)));
        });
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        IsBusy = true;
        Progress = 0;
        try
        {
            await work();
        }
        catch (UnauthorizedAccessException)
        {
            AddLog("Windows blocked writing to the game folder.");
            HandleAccessDenied();
        }
        catch (IOException e) when (e.HResult == unchecked((int)0x80070020)) // sharing violation
        {
            AddLog("A file is in use: " + e.Message);
            _dialogs.Error("A file in the game folder is in use. Close the game (and anything else using it) and try again.");
        }
        catch (Exception e)
        {
            AddLog("Failed: " + e.Message);
            _dialogs.Error(e.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressIndeterminate = false;
            Progress = 0;
            RefreshGameState();
        }
    }

    private void HandleAccessDenied()
    {
        if (Native.IsElevated())
        {
            _dialogs.Error("Windows blocked writing to the game folder, even as administrator. Check that the files aren't read-only or blocked by antivirus.");
            return;
        }
        if (_dialogs.Confirm("Windows blocked writing to the game folder (games under Program Files need administrator rights).\n\nRestart UE4SS Manager as administrator?"))
        {
            if (!Native.RestartAsAdmin()) _dialogs.Error("The restart was cancelled.");
        }
    }

    private static bool IsGameRunning(GameInstall g)
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(g.ShippingExePath)).Length > 0; }
        catch { return false; }
    }

    private bool CheckGameNotRunning(GameInstall g)
    {
        if (!IsGameRunning(g)) return true;
        _dialogs.Error($"{g.Name} is running. Close the game first, Windows locks its files while it runs.");
        return false;
    }

    [RelayCommand] private void OpenLog() { if (Status?.LogPath is { } p && File.Exists(p)) Native.Open(p); else _dialogs.Info("There's no UE4SS.log yet. It appears after the game has been started with UE4SS."); }
    [RelayCommand] private void OpenSettingsIni() { if (Status?.SettingsPath is { } p && File.Exists(p)) Native.Open(p); }

    // ---------------------------------------------------------------- mods

    public ObservableCollection<ModItem> Mods { get; } = [];
    [ObservableProperty] private string _modsSummary = "";

    private ModManager? CurrentModManager => Status is { } s ? new ModManager(s.ModsDir) : null;

    private void LoadMods()
    {
        Mods.Clear();
        if (CurrentModManager is not { } m) { ModsSummary = ""; return; }
        foreach (var info in m.List()) Mods.Add(new ModItem(info, ToggleMod));
        var enabled = Mods.Count(x => x.IsEnabled);
        var user = Mods.Count(x => !x.IsBuiltIn);
        ModsSummary = Mods.Count == 0 ? "No mods yet." : $"{Mods.Count} mods  ·  {enabled} enabled  ·  {user} added by you";
    }

    private void ToggleMod(ModItem item, bool enabled)
    {
        try
        {
            CurrentModManager?.SetEnabled(item.Name, enabled);
            var info = CurrentModManager?.List().FirstOrDefault(m => m.Name == item.Name);
            if (info is not null) item.Update(info);
            ModsSummary = $"{Mods.Count} mods  ·  {Mods.Count(x => x.IsEnabled)} enabled  ·  {Mods.Count(x => !x.IsBuiltIn)} added by you";
        }
        catch (UnauthorizedAccessException) { HandleAccessDenied(); LoadMods(); }
        catch (Exception e) { _dialogs.Error(e.Message); LoadMods(); }
    }

    [RelayCommand] private void RefreshMods() => RefreshGameState();

    [RelayCommand]
    private void InstallModZip()
    {
        var zips = _dialogs.PickZips("Pick UE4SS mod zips");
        if (zips.Length > 0) InstallMods(zips);
    }

    [RelayCommand]
    private void InstallModFolder()
    {
        var folder = _dialogs.PickFolder("Pick a mod folder (the one that contains Scripts or dlls)");
        if (folder is not null) InstallMods([folder]);
    }

    /// <summary>Installs dropped or picked zips and folders.</summary>
    public void InstallMods(IEnumerable<string> paths)
    {
        if (CurrentModManager is not { } manager) return;
        var installer = new ModArchiveInstaller(manager);
        foreach (var path in paths)
        {
            try
            {
                ModInstallResult Run(bool overwrite) => Directory.Exists(path)
                    ? installer.InstallFromFolder(path, overwrite)
                    : installer.InstallFromZip(path, overwrite);
                ModInstallResult result;
                try { result = Run(false); }
                catch (ModAlreadyExistsException e)
                {
                    if (!_dialogs.Confirm($"{string.Join(", ", e.Names)} is already installed. Replace it with the version from {Path.GetFileName(path)}?\n\nIts Scripts and dlls folders are replaced; other files such as configs are kept."))
                        continue;
                    result = Run(true);
                }
                AddLog($"Installed {string.Join(", ", result.InstalledMods)} from {Path.GetFileName(path)}.");
                foreach (var w in result.Warnings) AddLog(w);
                if (result.Warnings.Count > 0) _dialogs.Info(string.Join("\n\n", result.Warnings));
            }
            catch (UnauthorizedAccessException) { HandleAccessDenied(); break; }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                _dialogs.Error($"{Path.GetFileName(path)}: {e.Message}");
            }
        }
        LoadMods();
    }

    [RelayCommand]
    private void DeleteMod(ModItem? item)
    {
        if (item is null || CurrentModManager is not { } m) return;
        var warn = item.IsBuiltIn ? "\n\nThis mod ships with UE4SS. Reinstalling UE4SS brings it back." : "";
        if (!_dialogs.Confirm($"Delete the mod {item.Name}? Its folder is removed from disk.{warn}")) return;
        try { m.Delete(item.Name); AddLog($"Deleted {item.Name}."); }
        catch (UnauthorizedAccessException) { HandleAccessDenied(); }
        catch (Exception e) { _dialogs.Error(e.Message); }
        LoadMods();
    }

    [RelayCommand] private void OpenModFolder(ModItem? item) { if (item is not null) Native.ShowInExplorer(item.Info.Path); }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (Status is not { } s) return;
        Directory.CreateDirectory(s.ModsDir);
        Native.ShowInExplorer(s.ModsDir);
    }

    // ---------------------------------------------------------------- UE4SS-settings.ini quick settings

    public IReadOnlyList<string> GraphicsApis { get; } = ["opengl", "dx11", "d3d11"];

    [ObservableProperty] private bool _consoleEnabled;
    [ObservableProperty] private bool _guiConsoleEnabled;
    [ObservableProperty] private bool _guiConsoleVisible;
    [ObservableProperty] private bool _hotReload;
    [ObservableProperty] private string? _graphicsApi;
    [ObservableProperty] private bool _hasSettingsFile;

    private void LoadQuickSettings()
    {
        _loadingSettings = true;
        try
        {
            var path = Status?.SettingsPath;
            HasSettingsFile = path is not null && File.Exists(path);
            var ini = HasSettingsFile ? IniFile.Load(path!) : new IniFile([]);
            ConsoleEnabled = ini.GetBool("Debug", "ConsoleEnabled") ?? false;
            GuiConsoleEnabled = ini.GetBool("Debug", "GuiConsoleEnabled") ?? false;
            GuiConsoleVisible = ini.GetBool("Debug", "GuiConsoleVisible") ?? false;
            HotReload = ini.GetBool("General", "EnableHotReloadSystem") ?? false;
            GraphicsApi = ini.Get("Debug", "GraphicsAPI") ?? "opengl";
        }
        finally { _loadingSettings = false; }
    }

    private void SaveSetting(string section, string key, string value)
    {
        if (_loadingSettings || Status?.SettingsPath is not { } path || !File.Exists(path)) return;
        try
        {
            var ini = IniFile.Load(path);
            ini.Set(section, key, value);
            ini.Save(path);
        }
        catch (UnauthorizedAccessException) { HandleAccessDenied(); }
        catch (IOException e) { _dialogs.Error(e.Message); }
    }

    partial void OnConsoleEnabledChanged(bool value) => SaveSetting("Debug", "ConsoleEnabled", value ? "1" : "0");
    partial void OnGuiConsoleEnabledChanged(bool value) => SaveSetting("Debug", "GuiConsoleEnabled", value ? "1" : "0");
    partial void OnGuiConsoleVisibleChanged(bool value) => SaveSetting("Debug", "GuiConsoleVisible", value ? "1" : "0");
    partial void OnHotReloadChanged(bool value) => SaveSetting("General", "EnableHotReloadSystem", value ? "1" : "0");
    partial void OnGraphicsApiChanged(string? value) { if (value is not null) SaveSetting("Debug", "GraphicsAPI", value); }

    // ---------------------------------------------------------------- options

    [RelayCommand]
    private async Task OpenOptionsAsync()
    {
        var window = new OptionsWindow(_settings) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true) return;
        TrySaveSettings();
        _releases = CreateReleaseClient();
        await Task.WhenAll(ScanAsync(), LoadReleasesAsync());
    }

    private void TrySaveSettings()
    {
        try { _settings.Save(); } catch { }
    }
}
