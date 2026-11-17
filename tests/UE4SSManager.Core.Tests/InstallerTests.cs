using UE4SSManager.Core.Install;
using UE4SSManager.Core.Mods;

namespace UE4SSManager.Core.Tests;

public class InstallerTests
{
    private static string Win64(TempDir t) => t.Dir("Game/Proj/Binaries/Win64");

    [Fact]
    public void Fresh_install_of_v3_package()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        var zip = Zips.V3(t.Combine("dl/UE4SS_v3.0.1.zip"));
        var result = new Ue4ssInstaller(new FakeVersionReader()).Install(win64, zip, new InstallOptions { Version = "3.0.1", Tag = "v3.0.1" });

        Assert.True(result.Status.IsInstalled);
        Assert.Equal(Ue4ssLayout.Folder, result.Status.Layout);
        Assert.Equal("dwmapi.dll", result.Status.ProxyDll);
        Assert.Equal("3.0.1", result.Status.Version);
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "Keybinds", "Scripts", "main.lua")));
        Assert.Equal(Path.Combine(win64, "ue4ss", "Mods"), result.Status.ModsDir);
    }

    [Fact]
    public void Update_keeps_settings_mod_choices_and_user_mods()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        var installer = new Ue4ssInstaller(new FakeVersionReader());
        installer.Install(win64, Zips.V3(t.Combine("dl/old.zip"), "3.0.0"), new InstallOptions { Version = "3.0.0" });

        var ue4ss = Path.Combine(win64, "ue4ss");
        File.WriteAllText(Path.Combine(ue4ss, "UE4SS-settings.ini"), "[Debug]\nConsoleEnabled = 1\n");
        t.File("Game/Proj/Binaries/Win64/ue4ss/Mods/UUEPBS/Scripts/main.lua", "-- my mod");
        var manager = new ModManager(Path.Combine(ue4ss, "Mods"));
        manager.SetEnabled("UUEPBS", true);
        manager.SetEnabled("Keybinds", false);

        var newModsTxt = Zips.DefaultModsTxt.Replace("jsbLuaProfilerMod : 0", "jsbLuaProfilerMod : 0\nNewBuiltIn : 1");
        var result = installer.Install(win64, Zips.V3(t.Combine("dl/new.zip"), "3.0.1", newModsTxt),
            new InstallOptions { Version = "3.0.1", BackupDir = t.Combine("backups") });

        Assert.Equal("3.0.1", result.Status.Version);
        Assert.Contains("ConsoleEnabled = 1", File.ReadAllText(Path.Combine(ue4ss, "UE4SS-settings.ini")));
        Assert.True(File.Exists(Path.Combine(ue4ss, "UE4SS-settings.ini.new")));
        Assert.Equal("-- keybinds 3.0.1", File.ReadAllText(Path.Combine(ue4ss, "Mods", "Keybinds", "Scripts", "main.lua")));
        Assert.Equal("ue4ss 3.0.1", File.ReadAllText(Path.Combine(ue4ss, "UE4SS.dll")));

        var mods = manager.List().ToDictionary(m => m.Name);
        Assert.True(mods["UUEPBS"].IsEnabled);
        Assert.False(mods["Keybinds"].IsEnabled);
        var txt = ModsTxt.Load(manager.ModsTxtPath);
        Assert.True(txt.Get("NewBuiltIn"));
        Assert.Single(Directory.GetFiles(t.Combine("backups"), "*.zip"));
    }

    [Fact]
    public void Update_from_v2_layout_moves_mods_into_ue4ss_folder()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        var installer = new Ue4ssInstaller(new FakeVersionReader());
        var first = installer.Install(win64, Zips.V2(t.Combine("dl/v2.zip")), new InstallOptions { Version = "2.5.2" });
        Assert.Equal(Ue4ssLayout.Flat, first.Status.Layout);
        Assert.Equal("xinput1_3.dll", first.Status.ProxyDll);
        t.File("Game/Proj/Binaries/Win64/Mods/MyMod/Scripts/main.lua", "-- mine");
        new ModManager(Path.Combine(win64, "Mods")).SetEnabled("MyMod", true);

        var result = installer.Install(win64, Zips.V3(t.Combine("dl/v3.zip")), new InstallOptions { Version = "3.0.1" });

        Assert.Equal(Ue4ssLayout.Folder, result.Status.Layout);
        Assert.Equal("dwmapi.dll", result.Status.ProxyDll);
        Assert.False(File.Exists(Path.Combine(win64, "xinput1_3.dll")));
        Assert.False(File.Exists(Path.Combine(win64, "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "MyMod", "Scripts", "main.lua")));
        Assert.Contains("ConsoleEnabled = 1", File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini")));
        var mods = new ModManager(result.Status.ModsDir).List().ToDictionary(m => m.Name);
        Assert.True(mods["MyMod"].IsEnabled);
        Assert.True(mods.ContainsKey("Keybinds"));
    }

    [Fact]
    public void Uninstall_keeping_mods_leaves_mods_and_settings()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        t.File("Game/Proj/Binaries/Win64/Proj-Win64-Shipping.exe");
        var installer = new Ue4ssInstaller(new FakeVersionReader());
        installer.Install(win64, Zips.V3(t.Combine("dl/a.zip")), new InstallOptions());
        t.File("Game/Proj/Binaries/Win64/ue4ss/UE4SS.log", "log");

        var result = installer.Uninstall(win64, keepUserData: true);

        Assert.False(result.Status.IsInstalled);
        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.False(File.Exists(Path.Combine(win64, InstallManifest.FileName)));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "Keybinds", "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini")));
        Assert.True(File.Exists(Path.Combine(win64, "Proj-Win64-Shipping.exe")));
    }

    [Fact]
    public void Full_uninstall_removes_everything_but_the_game()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        t.File("Game/Proj/Binaries/Win64/Proj-Win64-Shipping.exe");
        var installer = new Ue4ssInstaller(new FakeVersionReader());
        installer.Install(win64, Zips.V3(t.Combine("dl/a.zip")), new InstallOptions());
        installer.Uninstall(win64, keepUserData: false);
        Assert.Equal(["Proj-Win64-Shipping.exe"], Directory.GetFileSystemEntries(win64).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Uninstall_without_manifest_detects_manual_install()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        t.File("Game/Proj/Binaries/Win64/dwmapi.dll");
        t.File("Game/Proj/Binaries/Win64/ue4ss/UE4SS.dll");
        t.File("Game/Proj/Binaries/Win64/ue4ss/UE4SS-settings.ini");
        t.File("Game/Proj/Binaries/Win64/ue4ss/Mods/A/Scripts/main.lua");
        var installer = new Ue4ssInstaller(new FakeVersionReader());
        var status = Ue4ssDetector.Detect(win64, new FakeVersionReader());
        Assert.True(status.IsInstalled);
        Assert.False(status.InstalledByManager);

        installer.Uninstall(win64, keepUserData: true);
        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "A", "Scripts", "main.lua")));
    }

    [Fact]
    public void Version_comes_from_dll_when_installed_by_hand()
    {
        using var t = new TempDir();
        var win64 = Win64(t);
        t.File("Game/Proj/Binaries/Win64/dwmapi.dll");
        t.File("Game/Proj/Binaries/Win64/ue4ss/UE4SS.dll");
        var reader = new FakeVersionReader(new() { ["UE4SS.dll"] = new("UE4SS", "3.0.1", "3.0.1.0", null) });
        Assert.Equal("3.0.1", Ue4ssDetector.Detect(win64, reader).Version);
    }

    [Fact]
    public void Rejects_zip_that_is_not_ue4ss()
    {
        using var t = new TempDir();
        var zip = Zips.Make(t.Combine("x.zip"), new Dictionary<string, string> { ["readme.txt"] = "hi" });
        Assert.Throws<InvalidDataException>(() => new Ue4ssInstaller().Install(Win64(t), zip, new InstallOptions()));
    }

    [Fact]
    public void Handles_wrapper_folder_in_zip()
    {
        using var t = new TempDir();
        var zip = Zips.Make(t.Combine("x.zip"), new Dictionary<string, string>
        {
            ["UE4SS_v3/dwmapi.dll"] = "p",
            ["UE4SS_v3/ue4ss/UE4SS.dll"] = "d",
        });
        var result = new Ue4ssInstaller(new FakeVersionReader()).Install(Win64(t), zip, new InstallOptions());
        Assert.Equal(Ue4ssLayout.Folder, result.Status.Layout);
    }

    [Fact]
    public void Zip_slip_is_refused()
    {
        using var t = new TempDir();
        var zip = Zips.Make(t.Combine("x.zip"), new Dictionary<string, string>
        {
            ["dwmapi.dll"] = "p",
            ["ue4ss/UE4SS.dll"] = "d",
            ["ue4ss/../../../../evil.txt"] = "bad",
        });
        Assert.Throws<InvalidDataException>(() => new Ue4ssInstaller(new FakeVersionReader()).Install(Win64(t), zip, new InstallOptions()));
        Assert.False(File.Exists(t.Combine("evil.txt")));
    }
}
