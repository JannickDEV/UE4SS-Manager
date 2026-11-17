using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Mods;

[Flags]
public enum ModKind { Unknown = 0, Lua = 1, Cpp = 2, LuaAndCpp = Lua | Cpp }

public sealed record ModInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ModKind Kind { get; init; }
    public bool IsEnabled { get; init; }
    public bool HasEnabledTxt { get; init; }
    public bool? ModsTxtState { get; init; }
    public bool IsBuiltIn { get; init; }

    public string KindLabel => Kind switch
    {
        ModKind.Lua => "Lua",
        ModKind.Cpp => "C++",
        ModKind.LuaAndCpp => "Lua + C++",
        _ => "Unknown",
    };

    /// <summary>What makes UE4SS load (or skip) this mod, for the UI.</summary>
    public string EnabledVia => (HasEnabledTxt, ModsTxtState) switch
    {
        (true, _) => "enabled.txt",
        (false, true) => "mods.txt",
        (false, false) => "mods.txt (off)",
        _ => "not listed",
    };
}

/// <summary>Lists and changes the mods in a UE4SS Mods folder. UE4SS loads a mod when mods.txt (or mods.json)
/// says "1" for it, or when the mod folder contains enabled.txt.</summary>
public sealed class ModManager(string modsDir)
{
    public static readonly IReadOnlySet<string> BuiltInMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ActorDumperMod", "BPML_GenericFunctions", "BPModLoaderMod", "CheatManagerEnablerMod", "ConsoleCommandsMod",
        "ConsoleEnablerMod", "Keybinds", "LineTraceMod", "SplitScreenMod", "jsbLuaProfilerMod", "shared",
    };

    public string ModsDir { get; } = modsDir;
    public string ModsTxtPath => Path.Combine(ModsDir, "mods.txt");
    public string ModsJsonPath => Path.Combine(ModsDir, "mods.json");

    public IReadOnlyList<ModInfo> List()
    {
        if (!Directory.Exists(ModsDir)) return [];
        var txt = ModsTxt.Load(ModsTxtPath);
        ModsJson? json = null;
        try { if (File.Exists(ModsJsonPath)) json = ModsJson.Load(ModsJsonPath); } catch (System.Text.Json.JsonException) { }

        var mods = new List<ModInfo>();
        foreach (var dir in PathUtil.SafeEnumerateDirectories(ModsDir))
        {
            var name = System.IO.Path.GetFileName(dir);
            // "shared" holds Lua libraries used by other mods, not a mod itself.
            if (name.Equals("shared", StringComparison.OrdinalIgnoreCase)) continue;
            var kind = DetectKind(dir);
            var hasEnabledTxt = PathUtil.FindChildFile(dir, "enabled.txt") is not null;
            var txtState = txt.Get(name);
            var jsonState = json?.Get(name);
            mods.Add(new ModInfo
            {
                Name = name,
                Path = dir,
                Kind = kind,
                HasEnabledTxt = hasEnabledTxt,
                ModsTxtState = txtState ?? jsonState,
                IsEnabled = hasEnabledTxt || txtState == true || jsonState == true,
                IsBuiltIn = BuiltInMods.Contains(name),
            });
        }
        // Order like UE4SS: mods.txt order first, then the rest alphabetically.
        var order = txt.Entries.Select((e, i) => (e.Name, i)).ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);
        return mods.OrderBy(m => m.IsBuiltIn)
                   .ThenBy(m => order.TryGetValue(m.Name, out var i) ? i : int.MaxValue)
                   .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    public static ModKind DetectKind(string modDir)
    {
        var kind = ModKind.Unknown;
        var scripts = PathUtil.FindChildDirectory(modDir, "Scripts");
        if (scripts is not null && PathUtil.FindChildFile(scripts, "main.lua") is not null) kind |= ModKind.Lua;
        var dlls = PathUtil.FindChildDirectory(modDir, "dlls");
        if (dlls is not null && PathUtil.FindChildFile(dlls, "main.dll") is not null) kind |= ModKind.Cpp;
        return kind;
    }

    public void SetEnabled(string name, bool enabled)
    {
        var dir = Path.Combine(ModsDir, name);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Mod '{name}' not found.");

        var enabledTxt = PathUtil.FindChildFile(dir, "enabled.txt");
        if (!enabled && enabledTxt is not null) File.Delete(enabledTxt);

        if (File.Exists(ModsTxtPath))
        {
            var txt = ModsTxt.Load(ModsTxtPath);
            txt.Set(name, enabled);
            txt.Save(ModsTxtPath);
        }
        if (File.Exists(ModsJsonPath))
        {
            try
            {
                var json = ModsJson.Load(ModsJsonPath);
                json.Set(name, enabled);
                json.Save(ModsJsonPath);
            }
            catch (System.Text.Json.JsonException) { }
        }
        // Without a mod list, enabled.txt is the only switch UE4SS has.
        if (enabled && !File.Exists(ModsTxtPath) && !File.Exists(ModsJsonPath) && enabledTxt is null)
            File.WriteAllText(Path.Combine(dir, "enabled.txt"), "");
    }

    public void Delete(string name)
    {
        var dir = Path.Combine(ModsDir, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        if (File.Exists(ModsTxtPath))
        {
            var txt = ModsTxt.Load(ModsTxtPath);
            if (txt.Remove(name)) txt.Save(ModsTxtPath);
        }
        if (File.Exists(ModsJsonPath))
        {
            try
            {
                var json = ModsJson.Load(ModsJsonPath);
                if (json.Remove(name)) json.Save(ModsJsonPath);
            }
            catch (System.Text.Json.JsonException) { }
        }
    }
}
