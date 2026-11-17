using System.IO.Compression;
using UE4SSManager.Core.Mods;
using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Install;

public sealed record InstallOptions
{
    /// <summary>Keep UE4SS-settings.ini, mods.txt/mods.json choices and disabled mods when updating.</summary>
    public bool KeepUserData { get; init; } = true;
    public string? Tag { get; init; }
    public string? Version { get; init; }
    public string? AssetName { get; init; }
    public bool IsDevBuild { get; init; }
    public bool IsExperimental { get; init; }
    /// <summary>Folder for pre-update backups of settings and mod lists. Null skips backups.</summary>
    public string? BackupDir { get; init; }
}

public sealed record InstallResult(Ue4ssStatus Status, IReadOnlyList<string> Messages);

/// <summary>Installs, updates and removes UE4SS in a game's Binaries\Win64 folder.</summary>
public sealed class Ue4ssInstaller(IFileVersionReader? versions = null)
{
    private const string SettingsFile = "UE4SS-settings.ini";
    private static readonly string[] CoreFiles = ["UE4SS.dll", "UE4SS.pdb", "UE4SS.log"];

    public InstallResult Install(string win64, string zipPath, InstallOptions options, IProgress<string>? log = null)
    {
        using var package = Ue4ssPackage.Open(zipPath);
        return Install(win64, package, options with { AssetName = options.AssetName ?? Path.GetFileName(zipPath) }, log);
    }

    public InstallResult Install(string win64, Ue4ssPackage package, InstallOptions options, IProgress<string>? log = null)
    {
        var messages = new List<string>();
        void Log(string m) { messages.Add(m); log?.Report(m); }

        if (!Directory.Exists(win64)) throw new DirectoryNotFoundException($"Game folder not found: {win64}");
        var before = Ue4ssDetector.Detect(win64, versions);
        var newUe4ssDir = package.Ue4ssFolder.Length == 0 ? win64 : Path.Combine(win64, package.Ue4ssFolder);
        // The proxy is built against one DLL's exports (dwmapi.dll for 3.x, xinput1_3.dll for 2.x), so it keeps its name.
        var proxyName = package.ProxyFile;

        if (before.IsInstalled)
        {
            Log($"Updating UE4SS {before.Version ?? "(unknown version)"} to {options.Version ?? "the selected build"}.");
            if (options.BackupDir is not null) Backup(before, options.BackupDir, Log);
            RemoveCore(before, keepUserData: true, Log);
            if (options.KeepUserData && before.Ue4ssDir is not null &&
                !string.Equals(PathUtil.Normalize(before.Ue4ssDir), PathUtil.Normalize(newUe4ssDir), StringComparison.OrdinalIgnoreCase))
            {
                MigrateUserData(before.Ue4ssDir, newUe4ssDir, Log);
            }
        }
        else
        {
            Log($"Installing UE4SS {options.Version ?? ""}".TrimEnd() + ".");
        }

        if (package.ProxyFile is null)
            Log("Note: this package has no proxy DLL, so UE4SS won't load by itself. Use an injector or pick a package with xinput1_3.dll/dwmapi.dll.");

        var existingModDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modsDir = Path.Combine(newUe4ssDir, "Mods");
        foreach (var d in PathUtil.SafeEnumerateDirectories(modsDir)) existingModDirs.Add(Path.GetFileName(d));

        var written = new List<string>();
        foreach (var rel in package.Files)
        {
            var targetRel = rel;
            var target = PathUtil.SafeCombine(win64, targetRel);
            var relToUe4ss = package.Ue4ssFolder.Length == 0 ? rel
                : rel.StartsWith(package.Ue4ssFolder + "/", StringComparison.OrdinalIgnoreCase) ? rel[(package.Ue4ssFolder.Length + 1)..] : null;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (options.KeepUserData && relToUe4ss is not null && File.Exists(target))
            {
                if (relToUe4ss.Equals(SettingsFile, StringComparison.OrdinalIgnoreCase))
                {
                    using (var src = package.OpenFile(rel)) using (var dst = File.Create(target + ".new")) src.CopyTo(dst);
                    Log($"Kept your {SettingsFile} (the new default was saved as {SettingsFile}.new).");
                    written.Add(targetRel);
                    continue;
                }
                if (relToUe4ss.Equals("Mods/mods.txt", StringComparison.OrdinalIgnoreCase))
                {
                    var mine = ModsTxt.Load(target);
                    mine.MergeMissingFrom(ModsTxt.Parse(package.ReadText(rel)));
                    mine.Save(target);
                    Log("Kept your mods.txt and added any new built-in mods.");
                    written.Add(targetRel);
                    continue;
                }
                if (relToUe4ss.Equals("Mods/mods.json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var mine = ModsJson.Load(target);
                        mine.MergeMissingFrom(ModsJson.Parse(package.ReadText(rel)));
                        mine.Save(target);
                        Log("Kept your mods.json and added any new built-in mods.");
                        written.Add(targetRel);
                        continue;
                    }
                    catch (System.Text.Json.JsonException) { /* broken user file: replace it */ }
                }
            }
            // A mod the user already had and switched off keeps its state.
            if (options.KeepUserData && relToUe4ss is not null && relToUe4ss.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(relToUe4ss).Equals("enabled.txt", StringComparison.OrdinalIgnoreCase))
            {
                var parts = relToUe4ss.Split('/');
                if (parts.Length == 3 && existingModDirs.Contains(parts[1]) && !File.Exists(target)) continue;
            }

            using (var src = package.OpenFile(rel))
            using (var dst = File.Create(target))
                src.CopyTo(dst);
            written.Add(targetRel);
        }

        var manifest = new InstallManifest
        {
            Tag = options.Tag,
            Version = options.Version,
            AssetName = options.AssetName,
            IsDevBuild = options.IsDevBuild,
            IsExperimental = options.IsExperimental,
            ProxyDll = proxyName,
            Ue4ssFolder = package.Ue4ssFolder,
            InstalledAt = DateTimeOffset.Now,
            Files = written,
        };
        manifest.Save(win64);
        Log($"Done. {written.Count} files written" + (proxyName is null ? "." : $", UE4SS loads through {proxyName}."));
        return new InstallResult(Ue4ssDetector.Detect(win64, versions), messages);
    }

    /// <summary>Removes UE4SS. With <paramref name="keepUserData"/> the Mods folder and UE4SS-settings.ini stay,
    /// so a later reinstall picks them up again; the game runs vanilla either way because the proxy DLL is gone.</summary>
    public InstallResult Uninstall(string win64, bool keepUserData, IProgress<string>? log = null)
    {
        var messages = new List<string>();
        void Log(string m) { messages.Add(m); log?.Report(m); }
        var status = Ue4ssDetector.Detect(win64, versions);
        if (!status.IsInstalled)
        {
            Log("UE4SS isn't installed here.");
            return new InstallResult(status, messages);
        }
        RemoveCore(status, keepUserData, Log);
        if (!keepUserData && status.Ue4ssDir is not null)
        {
            var mods = Path.Combine(status.Ue4ssDir, "Mods");
            if (Directory.Exists(mods)) { Directory.Delete(mods, recursive: true); Log("Deleted the Mods folder."); }
            foreach (var f in new[] { SettingsFile, SettingsFile + ".new" })
            {
                var p = Path.Combine(status.Ue4ssDir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            if (status.Layout == Ue4ssLayout.Folder && Directory.Exists(status.Ue4ssDir))
                Directory.Delete(status.Ue4ssDir, recursive: true);
        }
        var manifest = InstallManifest.PathIn(win64);
        if (File.Exists(manifest)) File.Delete(manifest);
        Log(keepUserData ? "UE4SS removed. Your mods and settings were kept." : "UE4SS and all its mods were removed.");
        return new InstallResult(Ue4ssDetector.Detect(win64, versions), messages);
    }

    /// <summary>Deletes the loader files, leaving mods and settings in place.</summary>
    private static void RemoveCore(Ue4ssStatus status, bool keepUserData, Action<string> log)
    {
        var win64 = status.Win64Path;
        var ue4ssDir = status.Ue4ssDir!;
        var ue4ssRel = status.Manifest?.Ue4ssFolder ?? (status.Layout == Ue4ssLayout.Folder ? Path.GetFileName(ue4ssDir) : "");

        bool IsUserData(string relToWin64)
        {
            var r = ue4ssRel.Length == 0 ? relToWin64
                : relToWin64.StartsWith(ue4ssRel + "/", StringComparison.OrdinalIgnoreCase) ? relToWin64[(ue4ssRel.Length + 1)..] : null;
            if (r is null) return false;
            return r.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)
                   || r.Equals(SettingsFile, StringComparison.OrdinalIgnoreCase)
                   || r.Equals(SettingsFile + ".new", StringComparison.OrdinalIgnoreCase);
        }

        var toDelete = new List<string>();
        if (status.Manifest is { Files.Count: > 0 } m)
        {
            toDelete.AddRange(m.Files.Where(f => !IsUserData(f)));
        }
        else if (status.Layout == Ue4ssLayout.Folder)
        {
            // Unknown install: everything in ue4ss\ except mods and settings belongs to the loader.
            foreach (var f in Directory.EnumerateFiles(ue4ssDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(win64, f).Replace('\\', '/');
                if (!IsUserData(rel)) toDelete.Add(rel);
            }
        }
        else
        {
            toDelete.AddRange(CoreFiles);
        }
        if (status.ProxyDll is not null) toDelete.Add(status.ProxyDll);
        toDelete.Add((ue4ssRel.Length == 0 ? "" : ue4ssRel + "/") + "UE4SS.log");

        int count = 0;
        foreach (var rel in toDelete.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path;
            try { path = PathUtil.SafeCombine(win64, rel); } catch (InvalidDataException) { continue; }
            if (!File.Exists(path)) continue;
            File.Delete(path);
            count++;
        }
        if (status.Layout == Ue4ssLayout.Folder) PathUtil.DeleteEmptyDirectories(ue4ssDir, win64);
        log($"Removed {count} UE4SS files" + (keepUserData ? " (mods and settings kept)." : "."));
    }

    /// <summary>Moves mods and settings between the 2.x layout (Win64\Mods) and the 3.x layout (Win64\ue4ss\Mods).</summary>
    private static void MigrateUserData(string oldDir, string newDir, Action<string> log)
    {
        Directory.CreateDirectory(newDir);
        var oldSettings = Path.Combine(oldDir, SettingsFile);
        var newSettings = Path.Combine(newDir, SettingsFile);
        if (File.Exists(oldSettings) && !File.Exists(newSettings)) File.Move(oldSettings, newSettings);

        var oldMods = Path.Combine(oldDir, "Mods");
        var newMods = Path.Combine(newDir, "Mods");
        if (!Directory.Exists(oldMods)) return;
        Directory.CreateDirectory(newMods);
        int moved = 0;
        foreach (var mod in PathUtil.SafeEnumerateDirectories(oldMods))
        {
            var dest = Path.Combine(newMods, Path.GetFileName(mod));
            if (Directory.Exists(dest)) continue;
            Directory.Move(mod, dest);
            moved++;
        }
        foreach (var f in new[] { "mods.txt", "mods.json" })
        {
            var src = Path.Combine(oldMods, f);
            var dst = Path.Combine(newMods, f);
            if (File.Exists(src) && !File.Exists(dst)) File.Move(src, dst);
        }
        PathUtil.DeleteEmptyDirectories(oldMods, oldDir);
        log($"Moved {moved} mods and your settings to the new folder layout ({Path.GetFileName(newDir)}).");
    }

    private static void Backup(Ue4ssStatus status, string backupDir, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var file = Path.Combine(backupDir, $"ue4ss-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            int n = 0;
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
            foreach (var rel in new[] { SettingsFile, "Mods/mods.txt", "Mods/mods.json" })
            {
                var p = Path.Combine(status.Ue4ssDir!, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p)) continue;
                zip.CreateEntryFromFile(p, rel);
                n++;
            }
            }
            if (n == 0) File.Delete(file);
            else log($"Backed up your settings and mod list to {file}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log("Couldn't write a backup: " + e.Message);
        }
    }
}
