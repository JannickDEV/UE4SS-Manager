namespace UE4SSManager.Core.Util;

public static class PathUtil
{
    /// <summary>Finds a direct child directory by name, ignoring case (Windows paths are case-insensitive,
    /// this keeps the logic identical when tests run on Linux).</summary>
    public static string? FindChildDirectory(string parent, string name)
    {
        if (!Directory.Exists(parent)) return null;
        var exact = Path.Combine(parent, name);
        if (Directory.Exists(exact)) return exact;
        try
        {
            return Directory.EnumerateDirectories(parent)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? FindChildFile(string parent, string name)
    {
        if (!Directory.Exists(parent)) return null;
        var exact = Path.Combine(parent, name);
        if (File.Exists(exact)) return exact;
        try
        {
            return Directory.EnumerateFiles(parent)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public static IEnumerable<string> SafeEnumerateFiles(string path, string pattern = "*")
    {
        try { return Directory.EnumerateFiles(path, pattern).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Joins a zip-style relative path (forward slashes) onto a base folder and refuses anything
    /// that escapes it ("zip slip").</summary>
    public static string SafeCombine(string baseDir, string relative)
    {
        var root = Normalize(baseDir) + Path.DirectorySeparatorChar;
        var rel = relative.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Archive entry '{relative}' points outside the target folder.");
        return full;
    }

    /// <summary>Removes empty directories below (and including) <paramref name="dir"/>, but never <paramref name="stopAt"/>.</summary>
    public static void DeleteEmptyDirectories(string dir, string stopAt)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var child in SafeEnumerateDirectories(dir)) DeleteEmptyDirectories(child, stopAt);
        if (string.Equals(Normalize(dir), Normalize(stopAt), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (IOException) { }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}
