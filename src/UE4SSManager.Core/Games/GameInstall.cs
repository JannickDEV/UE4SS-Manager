namespace UE4SSManager.Core.Games;

public enum GameSource { Steam, GOG, Epic, Folder, Manual }

/// <summary>An Unreal Engine game found on disk. <see cref="Win64Path"/> is where UE4SS gets installed
/// (the folder holding the *-Shipping.exe).</summary>
public sealed record GameInstall
{
    public required string Name { get; init; }
    public required GameSource Source { get; init; }
    /// <summary>The store's install folder (e.g. steamapps\common\Game). Equal to the project folder for manual games.</summary>
    public required string RootPath { get; init; }
    /// <summary>&lt;Project&gt;\Binaries\Win64 (or WinGDK for Game Pass builds).</summary>
    public required string Win64Path { get; init; }
    public required string ShippingExePath { get; init; }
    /// <summary>The UE project name, from the exe name (Foo-Win64-Shipping.exe -> Foo).</summary>
    public required string ProjectName { get; init; }
    public string? StoreId { get; init; }
    public string? EngineVersion { get; init; }

    /// <summary>&lt;Project&gt; folder (two levels above Win64).</summary>
    public string ProjectPath => Path.GetDirectoryName(Path.GetDirectoryName(Win64Path)!)!;

    public string Key => Win64Path.TrimEnd('\\', '/').ToUpperInvariant();
}
