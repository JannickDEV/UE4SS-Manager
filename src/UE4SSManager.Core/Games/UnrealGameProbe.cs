using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Games;

public sealed record UnrealBinary(string Win64Path, string ShippingExePath, string ProjectName);

/// <summary>Finds Unreal Engine shipping executables (&lt;Project&gt;\Binaries\Win64\*-Shipping.exe) inside a folder.</summary>
public static class UnrealGameProbe
{
    public const int DefaultMaxDepth = 4;

    private static readonly string[] BinaryPlatforms = ["Win64", "WinGDK"];

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Engine", "Content", "Saved", "Intermediate", "Plugins", "Config", "__Installer", "_CommonRedist",
        "CommonRedist", "Redist", "redist", "DirectX", "Support", "Docs", "Manual", "Soundtrack", ".git", "ue4ss", "Mods",
        "steamapps", "_redist", "EasyAntiCheat", "BattlEye",
    };

    /// <summary>Probes a folder that may be a store install root, a UE project folder, Binaries\Win64 itself,
    /// or a direct path to an exe.</summary>
    public static IReadOnlyList<UnrealBinary> Probe(string path, int maxDepth = DefaultMaxDepth)
    {
        var results = new List<UnrealBinary>();
        if (File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path)!;
            if (TryFromBinaryDir(dir, out var b))
            {
                results.Add(b);
                return results;
            }
            path = dir; // a launcher exe in the game root: search below it
        }
        if (!Directory.Exists(path)) return results;

        if (TryFromBinaryDir(path, out var direct))
        {
            results.Add(direct);
            return results;
        }

        Walk(path, 0, maxDepth, results);
        return results
            .GroupBy(r => r.Win64Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void Walk(string dir, int depth, int maxDepth, List<UnrealBinary> results)
    {
        var binaries = PathUtil.FindChildDirectory(dir, "Binaries");
        if (binaries is not null)
        {
            foreach (var platform in BinaryPlatforms)
            {
                var platDir = PathUtil.FindChildDirectory(binaries, platform);
                if (platDir is not null && TryFromBinaryDir(platDir, out var b)) results.Add(b);
            }
        }
        if (depth >= maxDepth) return;
        foreach (var child in PathUtil.SafeEnumerateDirectories(dir))
        {
            var name = Path.GetFileName(child);
            if (SkipDirs.Contains(name) || string.Equals(name, "Binaries", StringComparison.OrdinalIgnoreCase)) continue;
            Walk(child, depth + 1, maxDepth, results);
        }
    }

    public static bool TryFromBinaryDir(string dir, out UnrealBinary binary)
    {
        binary = null!;
        var exe = PathUtil.SafeEnumerateFiles(dir, "*.exe")
            .Where(f => Path.GetFileNameWithoutExtension(f).EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length)
            .FirstOrDefault();
        if (exe is null) return false;
        var norm = PathUtil.Normalize(dir);
        // The project folder (<Project>\Binaries\Win64) is the real UE project name; Palworld ships Pal\...\Palworld-Win64-Shipping.exe.
        var binariesDir = Path.GetDirectoryName(norm);
        var project = binariesDir is not null && string.Equals(Path.GetFileName(binariesDir), "Binaries", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(binariesDir))
            : null;
        binary = new UnrealBinary(norm, exe, string.IsNullOrEmpty(project) ? ProjectNameFromExe(exe) : project);
        return true;
    }

    public static string ProjectNameFromExe(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        foreach (var suffix in new[] { "-Win64-Shipping", "-WinGDK-Shipping", "-Shipping" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        }
        return name;
    }
}
