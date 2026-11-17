using System.Text.Json;

namespace UE4SSManager.Core;

/// <summary>User options, stored in %AppData%\UE4SS-Manager\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Folders whose sub-folders are games (e.g. E:\GOG). Steam libraries are found automatically.</summary>
    public List<string> LibraryFolders { get; set; } = [];
    /// <summary>Games added by hand (any folder or exe inside the game).</summary>
    public List<string> ManualGames { get; set; } = [];
    /// <summary>Detected games the user removed from the list.</summary>
    public List<string> HiddenGames { get; set; } = [];
    public string Repository { get; set; } = Releases.ReleaseClient.DefaultRepository;
    public string? GitHubToken { get; set; }
    public string? LastGame { get; set; }

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
