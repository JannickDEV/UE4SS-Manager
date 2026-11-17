using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using UE4SSManager.Core.Games;
using UE4SSManager.Core.Install;
using UE4SSManager.Core.Mods;
using UE4SSManager.Core.Releases;

namespace UE4SSManager.App.ViewModels;

public sealed partial class GameItem : ObservableObject
{
    public GameItem(GameInstall game)
    {
        Game = game;
        Icon = Services.Native.ExtractIcon(game.ShippingExePath);
    }

    public GameInstall Game { get; }
    public string Name => Game.Name;
    public ImageSource? Icon { get; }
    public string Monogram => new string(Game.Name.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();

    public string SourceLabel => Game.Source switch
    {
        GameSource.Steam => "Steam",
        GameSource.GOG => "GOG",
        GameSource.Epic => "Epic",
        GameSource.Folder => "Library folder",
        _ => "Added by hand",
    };

    public string Meta => string.Join("  ·  ", new[] { SourceLabel, Game.EngineVersion is null ? null : "UE " + Game.EngineVersion }.Where(s => s is not null));

    [ObservableProperty] private Ue4ssStatus? _status;

    public string Ue4ssBadge => Status switch
    {
        null => "",
        { IsInstalled: false } => "",
        { Version: { } v } => "UE4SS " + v,
        _ => "UE4SS",
    };

    partial void OnStatusChanged(Ue4ssStatus? value) => OnPropertyChanged(nameof(Ue4ssBadge));
}

public sealed partial class ModItem : ObservableObject
{
    private readonly Action<ModItem, bool> _toggle;
    private bool _suppress;

    public ModItem(ModInfo info, Action<ModItem, bool> toggle)
    {
        Info = info;
        _toggle = toggle;
        _isEnabled = info.IsEnabled;
    }

    public ModInfo Info { get; private set; }
    public string Name => Info.Name;
    public string Kind => Info.KindLabel;
    public string EnabledVia => Info.EnabledVia;
    public bool IsBuiltIn => Info.IsBuiltIn;

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppress) _toggle(this, value);
    }

    public void Update(ModInfo info)
    {
        Info = info;
        _suppress = true;
        IsEnabled = info.IsEnabled;
        _suppress = false;
        OnPropertyChanged(nameof(EnabledVia));
        OnPropertyChanged(nameof(Kind));
    }
}

public sealed record ReleaseOption(Ue4ssRelease Release)
{
    public string Title => Release.IsExperimental ? $"{Release.DisplayVersion}  (experimental)" : Release.DisplayVersion;
    public string Detail => Release.PublishedAt is { } d ? d.LocalDateTime.ToString("d MMM yyyy") : "";
    public override string ToString() => Title;
}
