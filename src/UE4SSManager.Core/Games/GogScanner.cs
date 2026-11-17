using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Games;

/// <summary>GOG games from the registry (written by both the offline installers and GOG Galaxy).</summary>
public sealed class GogScanner(IRegistryReader registry) : IGameScanner
{
    private static readonly string[] Keys = [@"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games"];

    public IEnumerable<StoreGame> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseKey in Keys)
        {
            foreach (var id in registry.GetSubKeyNames(RegHive.LocalMachine, baseKey))
            {
                var key = baseKey + "\\" + id;
                var path = registry.GetString(RegHive.LocalMachine, key, "path")
                           ?? registry.GetString(RegHive.LocalMachine, key, "PATH");
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !seen.Add(path)) continue;
                var name = registry.GetString(RegHive.LocalMachine, key, "gameName")
                           ?? registry.GetString(RegHive.LocalMachine, key, "GAMENAME")
                           ?? Path.GetFileName(path.TrimEnd('\\'));
                yield return new StoreGame(name, path, GameSource.GOG, id);
            }
        }
    }
}

/// <summary>Epic Games Store install manifests (%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item).</summary>
public sealed class EpicScanner(string? manifestsDir = null) : IGameScanner
{
    public static string DefaultManifestsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public IEnumerable<StoreGame> Scan()
    {
        var dir = manifestsDir ?? DefaultManifestsDir;
        foreach (var file in PathUtil.SafeEnumerateFiles(dir, "*.item"))
        {
            var game = ParseManifest(file);
            if (game is not null) yield return game;
        }
    }

    public static StoreGame? ParseManifest(string file)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            var location = Str("InstallLocation");
            if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) return null;
            return new StoreGame(Str("DisplayName") ?? Path.GetFileName(location), location, GameSource.Epic, Str("AppName"));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }
}

/// <summary>Treats every sub-folder of a library folder (e.g. E:\GOG) as a possible game.</summary>
public sealed class FolderScanner(IEnumerable<string> folders) : IGameScanner
{
    public IEnumerable<StoreGame> Scan()
    {
        foreach (var folder in folders)
        {
            foreach (var dir in PathUtil.SafeEnumerateDirectories(folder))
                yield return new StoreGame(Path.GetFileName(dir), dir, GameSource.Folder);
        }
    }

    /// <summary>Common library locations on each fixed drive, used when the user hasn't configured any.</summary>
    public static IEnumerable<string> GuessLibraryFolders()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        string[] names = ["GOG", "GOG Games", @"GOG Galaxy\Games", @"Program Files (x86)\GOG Galaxy\Games",
                          "Epic Games", @"Program Files\Epic Games", "Games", @"XboxGames"];
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ok;
            try { ok = drive.DriveType == DriveType.Fixed && drive.IsReady; } catch { ok = false; }
            if (!ok) continue;
            foreach (var n in names)
            {
                var p = Path.Combine(drive.RootDirectory.FullName, n);
                if (Directory.Exists(p)) yield return p;
            }
        }
    }
}
