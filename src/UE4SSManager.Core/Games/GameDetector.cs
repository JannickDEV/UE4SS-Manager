using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Games;

/// <summary>Runs all scanners and keeps the folders that contain an Unreal Engine shipping build.</summary>
public sealed class GameDetector(IEnumerable<IGameScanner> scanners, IFileVersionReader? versionReader = null)
{
    private readonly IFileVersionReader _versions = versionReader ?? SystemFileVersionReader.Instance;

    public static GameDetector CreateDefault(IEnumerable<string> extraLibraryFolders, IEnumerable<string>? extraSteamRoots = null)
    {
        var registry = RegistryReader.CreateDefault();
        var folders = extraLibraryFolders.Concat(FolderScanner.GuessLibraryFolders())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new GameDetector([
            new SteamScanner(registry, extraSteamRoots),
            new GogScanner(registry),
            new EpicScanner(),
            new FolderScanner(folders),
        ]);
    }

    public async Task<IReadOnlyList<GameInstall>> DetectAsync(IEnumerable<string>? manualPaths = null, CancellationToken ct = default)
    {
        return await Task.Run(() => Detect(manualPaths, ct), ct).ConfigureAwait(false);
    }

    public IReadOnlyList<GameInstall> Detect(IEnumerable<string>? manualPaths = null, CancellationToken ct = default)
    {
        var found = new Dictionary<string, GameInstall>(StringComparer.OrdinalIgnoreCase);
        var probedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scanner in scanners)
        {
            IEnumerable<StoreGame> games;
            try { games = scanner.Scan().ToList(); }
            catch (Exception) { continue; }
            foreach (var g in games)
            {
                ct.ThrowIfCancellationRequested();
                string root;
                try { root = PathUtil.Normalize(g.RootPath); } catch { continue; }
                if (!probedRoots.Add(root)) continue;
                foreach (var game in FromRoot(g with { RootPath = root }))
                    found.TryAdd(game.Key, game);
            }
        }

        foreach (var manual in manualPaths ?? [])
        {
            ct.ThrowIfCancellationRequested();
            foreach (var game in FromPath(manual, GameSource.Manual))
                found[game.Key] = game;
        }

        return found.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IEnumerable<GameInstall> FromPath(string path, GameSource source = GameSource.Manual)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return [];
        var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        var binaries = UnrealGameProbe.Probe(path);
        // Name a manual game after the store folder when the user picked Binaries\Win64 directly.
        return binaries.Select(b => Build(b, NiceName(b, dir), PathUtil.Normalize(dir), source, null));
    }

    private IEnumerable<GameInstall> FromRoot(StoreGame g)
    {
        var binaries = UnrealGameProbe.Probe(g.RootPath);
        foreach (var b in binaries)
        {
            var name = binaries.Count > 1 ? $"{g.Name} ({b.ProjectName})" : g.Name;
            yield return Build(b, name, g.RootPath, g.Source, g.StoreId);
        }
    }

    private GameInstall Build(UnrealBinary b, string name, string root, GameSource source, string? storeId)
    {
        string? engine = null;
        var v = _versions.Read(b.ShippingExePath);
        if (v is not null) engine = EngineVersionFrom(v);
        return new GameInstall
        {
            Name = name,
            Source = source,
            RootPath = root,
            Win64Path = b.Win64Path,
            ShippingExePath = b.ShippingExePath,
            ProjectName = b.ProjectName,
            StoreId = storeId,
            EngineVersion = engine,
        };
    }

    private static string NiceName(UnrealBinary b, string pickedDir)
    {
        var picked = Path.GetFileName(PathUtil.Normalize(pickedDir));
        if (string.Equals(picked, "Win64", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(picked, "WinGDK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(picked, "Binaries", StringComparison.OrdinalIgnoreCase))
            return b.ProjectName;
        return picked;
    }

    /// <summary>UE shipping exes usually carry the engine version as FileVersion (e.g. 4.27.2.0 or 5.3.2.0).</summary>
    public static string? EngineVersionFrom(FileVersionData v)
    {
        foreach (var s in new[] { v.FileVersion, v.ProductVersion })
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(?<!\d)([45])\.(\d{1,2})(?:\.(\d+))?");
            if (m.Success) return m.Groups[3].Success ? $"{m.Groups[1]}.{m.Groups[2]}.{m.Groups[3]}" : $"{m.Groups[1]}.{m.Groups[2]}";
        }
        return null;
    }
}
