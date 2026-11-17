using System.IO.Compression;
using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Mods;

public sealed class ModAlreadyExistsException(IReadOnlyList<string> names)
    : Exception($"Already installed: {string.Join(", ", names)}")
{
    public IReadOnlyList<string> Names { get; } = names;
}

public sealed record ModInstallResult(IReadOnlyList<string> InstalledMods, IReadOnlyList<string> Warnings);

/// <summary>Installs UE4SS mods from a zip or a folder. Mods are found by their Scripts\main.lua or dlls\main.dll,
/// so zips laid out as "MyMod\Scripts\...", "ue4ss\Mods\MyMod\...", "Mods\MyMod\..." or plain "Scripts\..." all work.</summary>
public sealed class ModArchiveInstaller(ModManager manager)
{
    private sealed record SourceFile(string Path, Func<Stream> Open);

    public ModInstallResult InstallFromZip(string zipPath, bool overwrite = false)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => e.Name.Length > 0)
                       .Select(e => new SourceFile(e.FullName.Replace('\\', '/'), e.Open)).ToList();
        return Install(files, Path.GetFileNameWithoutExtension(zipPath), overwrite);
    }

    public ModInstallResult InstallFromFolder(string folder, bool overwrite = false)
    {
        var root = PathUtil.Normalize(folder);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => new SourceFile(Path.GetRelativePath(root, f).Replace('\\', '/'), () => File.OpenRead(f)))
            .ToList();
        // A folder that is itself a mod keeps its own name.
        return Install(files, Path.GetFileName(root), overwrite);
    }

    /// <summary>Names of the mods a zip would install, without installing anything.</summary>
    public static IReadOnlyList<string> PeekZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var paths = zip.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName.Replace('\\', '/')).ToList();
        return FindModRoots(paths, Path.GetFileNameWithoutExtension(zipPath)).Select(r => r.Name).ToList();
    }

    private ModInstallResult Install(List<SourceFile> files, string fallbackName, bool overwrite)
    {
        var roots = FindModRoots(files.Select(f => f.Path).ToList(), fallbackName);
        if (roots.Count == 0)
        {
            var hasPak = files.Any(f => f.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase));
            throw new InvalidDataException(hasPak
                ? "This looks like a .pak mod, not a UE4SS mod. Pak mods go into the game's Content\\Paks\\~mods folder."
                : "No UE4SS mod found. A mod needs Scripts\\main.lua (Lua) or dlls\\main.dll (C++).");
        }

        var existing = roots.Where(r => Directory.Exists(Path.Combine(manager.ModsDir, r.Name))).Select(r => r.Name).ToList();
        if (existing.Count > 0 && !overwrite) throw new ModAlreadyExistsException(existing);

        Directory.CreateDirectory(manager.ModsDir);
        var warnings = new List<string>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, prefix) in roots)
        {
            var target = Path.Combine(manager.ModsDir, name);
            if (Directory.Exists(target))
            {
                // Replace scripts and dlls but keep the user's config files that the new version doesn't ship.
                foreach (var sub in new[] { "Scripts", "dlls" })
                {
                    var d = PathUtil.FindChildDirectory(target, sub);
                    if (d is not null) Directory.Delete(d, recursive: true);
                }
            }
            foreach (var f in files.Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var rel = f.Path[prefix.Length..];
                if (rel.Length == 0) continue;
                covered.Add(f.Path);
                var dest = PathUtil.SafeCombine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var src = f.Open();
                using var dst = File.Create(dest);
                src.CopyTo(dst);
            }
            manager.SetEnabled(name, true);
        }

        var leftovers = files.Where(f => !covered.Contains(f.Path)).Select(f => f.Path).ToList();
        if (leftovers.Any(p => p.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("The archive also has .pak files. Those aren't UE4SS mods and were not installed; copy them into the game's Content\\Paks folder (LogicMods for Blueprint mods).");
        else if (leftovers.Count(p => !IsReadme(p)) > 0)
            warnings.Add($"{leftovers.Count} files outside the mod folder were skipped.");
        return new ModInstallResult(roots.Select(r => r.Name).ToList(), warnings);
    }

    private static bool IsReadme(string p)
    {
        var n = Path.GetFileName(p);
        return n.StartsWith("readme", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
               || n.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || n.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns (mod name, path prefix inside the archive including the trailing slash, "" for the root).</summary>
    internal static List<(string Name, string Prefix)> FindModRoots(IReadOnlyList<string> paths, string fallbackName)
    {
        var prefixes = new List<string>();
        foreach (var p in paths)
        {
            var segs = p.Split('/');
            for (int i = 0; i < segs.Length - 1; i++)
            {
                bool lua = segs[i].Equals("Scripts", StringComparison.OrdinalIgnoreCase) && i == segs.Length - 2
                           && segs[^1].Equals("main.lua", StringComparison.OrdinalIgnoreCase);
                bool cpp = segs[i].Equals("dlls", StringComparison.OrdinalIgnoreCase) && i == segs.Length - 2
                           && segs[^1].Equals("main.dll", StringComparison.OrdinalIgnoreCase);
                if (!lua && !cpp) continue;
                var prefix = i == 0 ? "" : string.Join('/', segs[..i]) + "/";
                if (!prefixes.Contains(prefix)) prefixes.Add(prefix);
            }
        }
        // Ignore a mod root nested inside another one (e.g. a mod that bundles a sample).
        prefixes = prefixes.Contains("") ? [""]
            : prefixes.Where(p => !prefixes.Any(o => o != p && p.StartsWith(o, StringComparison.Ordinal))).ToList();

        var result = new List<(string, string)>();
        foreach (var prefix in prefixes.OrderBy(p => p.Length))
        {
            var name = prefix.Length == 0 ? CleanZipName(fallbackName) : prefix.TrimEnd('/').Split('/')[^1];
            if (name.Equals("Mods", StringComparison.OrdinalIgnoreCase) || name.Equals("ue4ss", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add((PathUtil.SanitizeFileName(name), prefix));
        }
        return result;
    }

    /// <summary>"MyMod-1234-1-2-1700000000" (a Nexus download name) -> "MyMod".</summary>
    internal static string CleanZipName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)-\d+(?:-\d+)*$");
        return m.Success ? m.Groups[1].Value : name;
    }
}
