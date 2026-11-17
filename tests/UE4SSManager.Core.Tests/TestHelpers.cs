using System.IO.Compression;
using System.Text;
using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ue4ssmgr-" + Guid.NewGuid().ToString("N")[..8]);
    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string rel, string content = "x")
    {
        var p = System.IO.Path.Combine(Path, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public string Dir(string rel)
    {
        var p = System.IO.Path.Combine(Path, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(p);
        return p;
    }

    public string Combine(string rel) => System.IO.Path.Combine(Path, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

public static class Zips
{
    public static string Make(string path, IDictionary<string, string> files)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }

    public const string DefaultModsTxt = """
        CheatManagerEnablerMod : 1
        ActorDumperMod : 0
        ConsoleCommandsMod : 1
        ConsoleEnablerMod : 1
        SplitScreenMod : 0
        LineTraceMod : 1
        BPModLoaderMod : 1
        BPML_GenericFunctions : 1
        jsbLuaProfilerMod : 0

        ; Built-in keybinds, do not move up!
        Keybinds : 1
        """;

    /// <summary>A UE4SS 3.x style package: dwmapi.dll at the root, everything else under ue4ss\.</summary>
    public static string V3(string path, string version = "3.0.1", string modsTxt = DefaultModsTxt, string settings = "[Debug]\nConsoleEnabled = 0\n") =>
        Make(path, new Dictionary<string, string>
        {
            ["dwmapi.dll"] = "proxy " + version,
            ["ue4ss/UE4SS.dll"] = "ue4ss " + version,
            ["ue4ss/UE4SS-settings.ini"] = settings,
            ["ue4ss/Mods/mods.txt"] = modsTxt,
            ["ue4ss/Mods/Keybinds/Scripts/main.lua"] = "-- keybinds " + version,
            ["ue4ss/Mods/BPModLoaderMod/Scripts/main.lua"] = "-- bpml " + version,
            ["ue4ss/Mods/shared/UEHelpers/UEHelpers.lua"] = "-- helpers",
        });

    /// <summary>A UE4SS 2.x style package: xinput1_3.dll, UE4SS.dll and Mods\ all in the root.</summary>
    public static string V2(string path) =>
        Make(path, new Dictionary<string, string>
        {
            ["xinput1_3.dll"] = "proxy 2",
            ["UE4SS.dll"] = "ue4ss 2",
            ["UE4SS-settings.ini"] = "[Debug]\nConsoleEnabled = 1\n",
            ["Mods/mods.txt"] = "BPModLoaderMod : 1\nKeybinds : 1\n",
            ["Mods/BPModLoaderMod/Scripts/main.lua"] = "-- bpml 2",
            ["Mods/Keybinds/Scripts/main.lua"] = "-- kb 2",
        });
}

public sealed class FakeVersionReader(Dictionary<string, FileVersionData>? map = null) : IFileVersionReader
{
    public FileVersionData? Read(string path) =>
        map is not null && map.TryGetValue(System.IO.Path.GetFileName(path), out var v) ? v : null;
}

public sealed class FakeRegistry : IRegistryReader
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> SubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(RegHive hive, string keyPath, string valueName) =>
        Values.TryGetValue($"{hive}|{keyPath}|{valueName}", out var v) ? v : null;

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string keyPath) =>
        SubKeys.TryGetValue($"{hive}|{keyPath}", out var v) ? v : [];
}
