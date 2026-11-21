namespace UE4SSManager.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Hiding_is_case_insensitive_and_reports_changes()
    {
        var s = new AppSettings();
        Assert.True(s.SetHidden(@"E:\GAMES\FOO\BINARIES\WIN64", true));
        Assert.False(s.SetHidden(@"e:\games\foo\binaries\win64", true));
        Assert.True(s.IsHidden(@"e:\games\foo\binaries\win64"));
        Assert.True(s.SetHidden(@"e:\games\foo\binaries\win64", false));
        Assert.False(s.IsHidden(@"E:\GAMES\FOO\BINARIES\WIN64"));
        Assert.False(s.SetHidden(@"E:\GAMES\FOO\BINARIES\WIN64", false));
    }

    [Fact]
    public void Hidden_and_removed_games_round_trip_and_old_files_still_load()
    {
        using var t = new TempDir();
        var path = t.Combine("settings.json");

        // Older versions stored removed games as "HiddenGames"; they must stay removed, not become hidden.
        File.WriteAllText(path, """{ "HiddenGames": [ "A" ] }""");
        var old = AppSettings.Load(path);
        Assert.Equal(["A"], old.RemovedGames);
        Assert.Empty(old.HiddenGames);

        old.SetHidden("B", true);
        old.Save(path);
        var back = AppSettings.Load(path);
        Assert.Equal(["A"], back.RemovedGames);
        Assert.True(back.IsHidden("B"));
        Assert.False(back.IsHidden("A"));
    }
}
