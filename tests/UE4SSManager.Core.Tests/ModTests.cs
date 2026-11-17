using UE4SSManager.Core.Mods;
using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Tests;

public class ModTests
{
    [Fact]
    public void ModsTxt_inserts_new_mods_above_keybinds()
    {
        var txt = ModsTxt.Parse(Zips.DefaultModsTxt);
        txt.Set("UUEPBS", true);
        var lines = txt.ToString().Split('\n').Select(l => l.Trim()).ToList();
        var mod = lines.IndexOf("UUEPBS : 1");
        var comment = lines.FindIndex(l => l.StartsWith("; Built-in keybinds"));
        Assert.True(mod >= 0 && mod < comment, string.Join("\n", lines));
        Assert.True(txt.Get("uuepbs"));
        txt.Set("UUEPBS", false);
        Assert.False(txt.Get("UUEPBS"));
        Assert.Equal(1, txt.Entries.Count(e => e.Name == "UUEPBS"));
    }

    [Fact]
    public void Lists_mods_with_kind_and_state()
    {
        using var t = new TempDir();
        t.File("Mods/mods.txt", "LuaOn : 1\nLuaOff : 0\nKeybinds : 1\n");
        t.File("Mods/LuaOn/Scripts/main.lua");
        t.File("Mods/LuaOff/Scripts/main.lua");
        t.File("Mods/CppMod/dlls/main.dll");
        t.File("Mods/CppMod/enabled.txt", "");
        t.File("Mods/Keybinds/Scripts/main.lua");
        t.File("Mods/shared/Types.lua");
        var mods = new ModManager(t.Combine("Mods")).List().ToDictionary(m => m.Name);

        Assert.Equal(4, mods.Count);
        Assert.True(mods["LuaOn"].IsEnabled);
        Assert.False(mods["LuaOff"].IsEnabled);
        Assert.True(mods["CppMod"].IsEnabled);
        Assert.Equal(ModKind.Cpp, mods["CppMod"].Kind);
        Assert.Equal("enabled.txt", mods["CppMod"].EnabledVia);
        Assert.True(mods["Keybinds"].IsBuiltIn);
    }

    [Fact]
    public void Disabling_removes_enabled_txt_and_updates_lists()
    {
        using var t = new TempDir();
        t.File("Mods/mods.txt", "Keybinds : 1\n");
        t.File("Mods/mods.json", """[{"mod_name":"Keybinds","mod_enabled":true}]""");
        t.File("Mods/A/Scripts/main.lua");
        t.File("Mods/A/enabled.txt", "");
        var m = new ModManager(t.Combine("Mods"));

        m.SetEnabled("A", false);
        Assert.False(File.Exists(t.Combine("Mods/A/enabled.txt")));
        Assert.False(ModsTxt.Load(m.ModsTxtPath).Get("A"));
        Assert.False(ModsJson.Load(m.ModsJsonPath).Get("A"));
        Assert.False(m.List().Single(x => x.Name == "A").IsEnabled);

        m.SetEnabled("A", true);
        Assert.True(m.List().Single(x => x.Name == "A").IsEnabled);
        Assert.Equal("A", ModsJson.Load(m.ModsJsonPath).Entries[0].Name); // inserted before Keybinds
    }

    [Fact]
    public void Enabling_without_mod_list_writes_enabled_txt()
    {
        using var t = new TempDir();
        t.File("Mods/A/Scripts/main.lua");
        new ModManager(t.Combine("Mods")).SetEnabled("A", true);
        Assert.True(File.Exists(t.Combine("Mods/A/enabled.txt")));
    }

    [Fact]
    public void Delete_removes_folder_and_entry()
    {
        using var t = new TempDir();
        t.File("Mods/mods.txt", "A : 1\nKeybinds : 1\n");
        t.File("Mods/A/Scripts/main.lua");
        var m = new ModManager(t.Combine("Mods"));
        m.Delete("A");
        Assert.False(Directory.Exists(t.Combine("Mods/A")));
        Assert.Null(ModsTxt.Load(m.ModsTxtPath).Get("A"));
    }

    [Theory]
    [InlineData("MyMod/Scripts/main.lua", "MyMod")]
    [InlineData("ue4ss/Mods/MyMod/Scripts/main.lua", "MyMod")]
    [InlineData("Mods/MyMod/dlls/main.dll", "MyMod")]
    [InlineData("Scripts/main.lua", "CoolMod")]
    public void Finds_mod_root_in_common_zip_layouts(string entry, string expected)
    {
        var roots = ModArchiveInstaller.FindModRoots([entry, "readme.txt"], "CoolMod-1234-1-0-1700000000");
        Assert.Equal(expected, Assert.Single(roots).Name);
    }

    [Fact]
    public void Installs_mod_zip_and_enables_it()
    {
        using var t = new TempDir();
        t.File("Mods/mods.txt", Zips.DefaultModsTxt);
        var zip = Zips.Make(t.Combine("UUEPBS.zip"), new Dictionary<string, string>
        {
            ["ue4ss/Mods/UUEPBS/Scripts/main.lua"] = "-- v1",
            ["ue4ss/Mods/UUEPBS/dlls/main.dll"] = "dll",
            ["ue4ss/Mods/UUEPBS/config.lua"] = "cfg",
            ["README.md"] = "hi",
        });
        var m = new ModManager(t.Combine("Mods"));
        var result = new ModArchiveInstaller(m).InstallFromZip(zip);
        Assert.Equal(["UUEPBS"], result.InstalledMods.ToArray());
        var mod = m.List().Single(x => x.Name == "UUEPBS");
        Assert.True(mod.IsEnabled);
        Assert.Equal(ModKind.LuaAndCpp, mod.Kind);

        // Reinstall needs overwrite, keeps user config not in the new zip.
        File.WriteAllText(t.Combine("Mods/UUEPBS/user.cfg"), "mine");
        Assert.Throws<ModAlreadyExistsException>(() => new ModArchiveInstaller(m).InstallFromZip(zip));
        new ModArchiveInstaller(m).InstallFromZip(zip, overwrite: true);
        Assert.Equal("mine", File.ReadAllText(t.Combine("Mods/UUEPBS/user.cfg")));
    }

    [Fact]
    public void Pak_only_zip_gives_clear_error()
    {
        using var t = new TempDir();
        var zip = Zips.Make(t.Combine("p.zip"), new Dictionary<string, string> { ["~mods/thing_P.pak"] = "pak" });
        var e = Assert.Throws<InvalidDataException>(() => new ModArchiveInstaller(new ModManager(t.Combine("Mods"))).InstallFromZip(zip));
        Assert.Contains(".pak", e.Message);
    }

    [Fact]
    public void Installs_from_folder()
    {
        using var t = new TempDir();
        t.File("src/FolderMod/Scripts/main.lua", "--");
        var m = new ModManager(t.Combine("Mods"));
        var r = new ModArchiveInstaller(m).InstallFromFolder(t.Combine("src/FolderMod"));
        Assert.Equal(["FolderMod"], r.InstalledMods.ToArray());
        Assert.True(File.Exists(t.Combine("Mods/FolderMod/enabled.txt")));
    }

    [Fact]
    public void Ini_set_preserves_comments()
    {
        var ini = new IniFile(["; header", "[Debug]", "; c", "ConsoleEnabled = 0 ; trailing", "", "[General]", "X = 1"]);
        Assert.False(ini.GetBool("Debug", "ConsoleEnabled"));
        ini.SetBool("Debug", "ConsoleEnabled", true);
        ini.Set("Debug", "GraphicsAPI", "dx11");
        ini.Set("New", "K", "v");
        var text = ini.ToString();
        Assert.Contains("; header", text);
        Assert.True(ini.GetBool("Debug", "ConsoleEnabled"));
        Assert.Equal("dx11", ini.Get("Debug", "GraphicsAPI"));
        Assert.Equal("v", ini.Get("New", "K"));
        Assert.True(text.IndexOf("GraphicsAPI") < text.IndexOf("[General]"));
    }
}
