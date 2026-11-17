using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Games;

public sealed record StoreGame(string Name, string RootPath, GameSource Source, string? StoreId = null);

public interface IGameScanner
{
    IEnumerable<StoreGame> Scan();
}

/// <summary>Reads Steam's libraryfolders.vdf and appmanifest_*.acf files.</summary>
public sealed class SteamScanner(IRegistryReader registry, IEnumerable<string>? extraSteamRoots = null) : IGameScanner
{
    public IEnumerable<StoreGame> Scan()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in FindSteamRoots())
        {
            libraries.Add(PathUtil.Normalize(root));
            foreach (var lib in ReadLibraryFolders(root)) libraries.Add(PathUtil.Normalize(lib));
        }

        foreach (var lib in libraries)
        {
            var steamapps = PathUtil.FindChildDirectory(lib, "steamapps");
            if (steamapps is null) continue;
            var common = PathUtil.FindChildDirectory(steamapps, "common");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var acf in PathUtil.SafeEnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                StoreGame? game = null;
                try
                {
                    var state = VdfParser.ParseFile(acf)["AppState"];
                    var installDir = state?.GetString("installdir");
                    if (state is null || string.IsNullOrWhiteSpace(installDir) || common is null) continue;
                    var root = Path.Combine(common, installDir);
                    if (!Directory.Exists(root)) continue;
                    game = new StoreGame(state.GetString("name") ?? installDir, root, GameSource.Steam, state.GetString("appid"));
                }
                catch (IOException) { }
                if (game is not null && seen.Add(game.RootPath)) yield return game;
            }
            // Folders in common\ without a manifest (copied in by hand) still count.
            if (common is not null)
            {
                foreach (var dir in PathUtil.SafeEnumerateDirectories(common))
                {
                    if (seen.Add(dir)) yield return new StoreGame(Path.GetFileName(dir), dir, GameSource.Steam);
                }
            }
        }
    }

    public IEnumerable<string> FindSteamRoots()
    {
        var roots = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            p = p.Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(p) && !roots.Contains(p, StringComparer.OrdinalIgnoreCase)) roots.Add(p);
        }
        Add(registry.GetString(RegHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
        Add(registry.GetString(RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
        Add(registry.GetString(RegHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));
        if (OperatingSystem.IsWindows())
        {
            Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        foreach (var r in extraSteamRoots ?? []) Add(r);
        return roots;
    }

    public static IEnumerable<string> ReadLibraryFolders(string steamRoot)
    {
        var steamapps = PathUtil.FindChildDirectory(steamRoot, "steamapps");
        var paths = new[]
        {
            steamapps is null ? null : Path.Combine(steamapps, "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
        };
        var result = new List<string>();
        foreach (var file in paths)
        {
            if (file is null || !File.Exists(file)) continue;
            try { result.AddRange(ParseLibraryFolders(File.ReadAllText(file))); }
            catch (IOException) { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Handles both the current format ("0" { "path" "D:\\SteamLibrary" }) and the pre-2021 one ("1" "D:\\SteamLibrary").</summary>
    public static IEnumerable<string> ParseLibraryFolders(string vdfText)
    {
        var root = VdfParser.Parse(vdfText);
        var lf = root["libraryfolders"] ?? root["LibraryFolders"];
        if (lf is null) yield break;
        foreach (var (key, node) in lf.Children)
        {
            if (!key.All(char.IsDigit)) continue;
            var path = node.Value ?? node.GetString("path");
            if (!string.IsNullOrWhiteSpace(path)) yield return path;
        }
    }
}
