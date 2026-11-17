using UE4SSManager.Core.Games;
using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Tests;

public class GameDetectionTests
{
    [Fact]
    public void Parses_current_libraryfolders_format()
    {
        var vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                    "apps"
                    {
                        "228980"		"431286727"
                    }
                }
                "1"
                {
                    "path"		"E:\\Steam"
                }
            }
            """;
        Assert.Equal(["C:\\Program Files (x86)\\Steam", "E:\\Steam"], SteamScanner.ParseLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void Parses_legacy_libraryfolders_format()
    {
        var vdf = """
            "LibraryFolders"
            {
                "TimeNextStatsReport"		"1561832478"
                "ContentStatsID"		"-158337411110787451"
                "1"		"D:\\SteamLibrary"
            }
            """;
        Assert.Equal(["D:\\SteamLibrary"], SteamScanner.ParseLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void Finds_shipping_exe_in_project_folder()
    {
        using var t = new TempDir();
        t.File("Palworld/Pal/Binaries/Win64/Palworld-Win64-Shipping.exe");
        t.File("Palworld/Palworld.exe");
        t.File("Palworld/Engine/Binaries/Win64/CrashReportClient.exe");

        var found = UnrealGameProbe.Probe(t.Combine("Palworld"));
        var b = Assert.Single(found);
        Assert.Equal("Pal", b.ProjectName);
        Assert.EndsWith(Path.Combine("Pal", "Binaries", "Win64"), b.Win64Path);
    }

    [Fact]
    public void Finds_project_nested_two_levels_deep_like_afterparty_gog()
    {
        using var t = new TempDir();
        t.File("GOG/Afterparty/Game/AfterpartyGame/Binaries/Win64/AfterpartyGame-Win64-Shipping.exe");
        t.File("GOG/Afterparty/goggame-123.info", "{}");
        t.File("GOG/NotUnreal/game.exe");

        var detector = new GameDetector([new FolderScanner([t.Combine("GOG")])], new FakeVersionReader());
        var game = Assert.Single(detector.Detect());
        Assert.Equal("Afterparty", game.Name);
        Assert.Equal("AfterpartyGame", game.ProjectName);
        Assert.Equal(GameSource.Folder, game.Source);
    }

    [Fact]
    public void Steam_scanner_reads_manifests_and_names_games()
    {
        using var t = new TempDir();
        var steam = t.Dir("Steam");
        var lib2 = t.Dir("Lib2");
        t.File("Steam/steamapps/libraryfolders.vdf", $$"""
            "libraryfolders" { "0" { "path" "{{steam.Replace("\\", "\\\\")}}" } "1" { "path" "{{lib2.Replace("\\", "\\\\")}}" } }
            """);
        t.File("Lib2/steamapps/appmanifest_2943650.acf", """
            "AppState" { "appid" "2943650" "name" "Carnal Instinct" "installdir" "CarnalInstinct" }
            """);
        t.File("Lib2/steamapps/common/CarnalInstinct/Carnal_Instinct_UE5/Binaries/Win64/Carnal_Instinct_UE5-Win64-Shipping.exe");
        t.File("Steam/steamapps/common/SomeUnityGame/Game.exe");

        var reg = new FakeRegistry();
        reg.Values[$"{RegHive.CurrentUser}|Software\\Valve\\Steam|SteamPath"] = steam;
        var detector = new GameDetector([new SteamScanner(reg)], new FakeVersionReader());
        var game = Assert.Single(detector.Detect());
        Assert.Equal("Carnal Instinct", game.Name);
        Assert.Equal("2943650", game.StoreId);
        Assert.Equal(GameSource.Steam, game.Source);
        Assert.Equal("Carnal_Instinct_UE5", game.ProjectName);
    }

    [Fact]
    public void Gog_scanner_uses_registry()
    {
        using var t = new TempDir();
        t.File("G/Game/Proj/Binaries/Win64/Proj-Win64-Shipping.exe");
        var reg = new FakeRegistry();
        reg.SubKeys[$"{RegHive.LocalMachine}|SOFTWARE\\WOW6432Node\\GOG.com\\Games"] = ["1207"];
        reg.Values[$"{RegHive.LocalMachine}|SOFTWARE\\WOW6432Node\\GOG.com\\Games\\1207|path"] = t.Combine("G/Game");
        reg.Values[$"{RegHive.LocalMachine}|SOFTWARE\\WOW6432Node\\GOG.com\\Games\\1207|gameName"] = "Nice Game";
        var game = Assert.Single(new GameDetector([new GogScanner(reg)], new FakeVersionReader()).Detect());
        Assert.Equal("Nice Game", game.Name);
        Assert.Equal(GameSource.GOG, game.Source);
    }

    [Fact]
    public void Epic_manifest_is_parsed()
    {
        using var t = new TempDir();
        var install = t.Dir("Epic/Game");
        t.File("Epic/Game/P/Binaries/Win64/P-Win64-Shipping.exe");
        t.File("Manifests/abc.item", $$"""{ "DisplayName": "Epic Thing", "InstallLocation": "{{install.Replace("\\", "\\\\")}}", "AppName": "Fern" }""");
        var game = Assert.Single(new GameDetector([new EpicScanner(t.Combine("Manifests"))], new FakeVersionReader()).Detect());
        Assert.Equal("Epic Thing", game.Name);
        Assert.Equal("Fern", game.StoreId);
    }

    [Fact]
    public void Same_game_from_two_sources_is_listed_once()
    {
        using var t = new TempDir();
        t.File("Lib/G/P/Binaries/Win64/P-Win64-Shipping.exe");
        var detector = new GameDetector([new FolderScanner([t.Combine("Lib")]), new FolderScanner([t.Combine("Lib")])], new FakeVersionReader());
        Assert.Single(detector.Detect([t.Combine("Lib/G/P/Binaries/Win64")]));
    }

    [Fact]
    public void Manual_path_can_be_win64_or_exe()
    {
        using var t = new TempDir();
        var exe = t.File("X/Proj/Binaries/Win64/Proj-Win64-Shipping.exe");
        var detector = new GameDetector([], new FakeVersionReader());
        Assert.Equal("Proj", Assert.Single(detector.FromPath(exe)).Name);
        Assert.Equal("Proj", Assert.Single(detector.FromPath(Path.GetDirectoryName(exe)!)).Name);
        Assert.Equal("X", Assert.Single(detector.FromPath(t.Combine("X"))).Name);
        var launcher = t.File("X/X.exe");
        Assert.Equal("X", Assert.Single(detector.FromPath(launcher)).Name);
    }

    [Theory]
    [InlineData("4.27.2.0", "4.27.2")]
    [InlineData("5.3.2.0", "5.3.2")]
    [InlineData("++UE5+Release-5.1-CL-23901901", "5.1")]
    public void Engine_version_from_exe_info(string fileVersion, string expected)
    {
        Assert.Equal(expected, GameDetector.EngineVersionFrom(new FileVersionData(null, null, fileVersion, null)));
    }
}
