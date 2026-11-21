using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE4SSManager.Core;

/// <summary>User options, stored in %AppData%\UE4SS-Manager\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Folders whose sub-folders are games (e.g. E:\GOG). Steam libraries are found automatically.</summary>
    public List<string> LibraryFolders { get; set; } = [];
    /// <summary>Games added by hand (any folder or exe inside the game).</summary>
    public List<string> ManualGames { get; set; } = [];
    /// <summary>Detected games the user removed from the list (by <c>GameInstall.Key</c>). Stored as "HiddenGames"
    /// because that is what older versions called it.</summary>
    [JsonPropertyName("HiddenGames")]
    public List<string> RemovedGames { get; set; } = [];
    /// <summary>Games the user hid (by <c>GameInstall.Key</c>). They stay installed and managed, but only show up in
    /// the hidden view (Ctrl+H in the games list).</summary>
    [JsonPropertyName("Tucked")]
    public List<string> HiddenGames { get; set; } = [];
    public string Repository { get; set; } = Releases.ReleaseClient.DefaultRepository;
    public string? GitHubToken { get; set; }
    public string? LastGame { get; set; }

    public bool IsHidden(string key) => HiddenGames.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Hides or unhides a game. Returns true when something changed.</summary>
    public bool SetHidden(string key, bool hidden)
    {
        if (hidden)
        {
            if (IsHidden(key)) return false;
            HiddenGames.Add(key);
            return true;
        }
        return HiddenGames.RemoveAll(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UE4SS-Manager");

    public static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UE4SS-Manager");

    private static string FilePath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
