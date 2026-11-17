using System.Text.RegularExpressions;

namespace UE4SSManager.Core.Releases;

public enum AssetKind
{
    /// <summary>The normal UE4SS build players use.</summary>
    Standard,
    /// <summary>zDEV build: same loader plus debug symbols and developer tools (live view, dumpers...).</summary>
    Dev,
    /// <summary>Extras such as zCustomGameConfigs.zip or zMapGenBP.zip.</summary>
    Other,
}

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, DateTimeOffset? UpdatedAt)
{
    public AssetKind Kind => Ue4ssRelease.Classify(Name);

    /// <summary>Version taken from the file name, e.g. "3.0.1" or "3.0.1-596-g96c1d5d".</summary>
    public string? Version => Ue4ssRelease.VersionFromAssetName(Name);
}

public sealed partial record Ue4ssRelease(
    string Tag,
    string Name,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    string? HtmlUrl,
    string? Body,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public const string ExperimentalTag = "experimental-latest";

    /// <summary>The rolling "experimental-latest" build (rebuilt from main) or any other pre-release.</summary>
    public bool IsExperimental => IsPrerelease || Tag.StartsWith("experimental", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ReleaseAsset> AssetsOf(AssetKind kind) => Assets.Where(a => a.Kind == kind);

    /// <summary>Best zip for the requested build. For 2.x releases the xinput proxy package is preferred
    /// over the injector-only package.</summary>
    public ReleaseAsset? PickAsset(AssetKind kind) =>
        AssetsOf(kind)
            .OrderByDescending(a => a.Name.Contains("Xinput", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name.Length)
            .FirstOrDefault();

    public bool HasDevBuild => AssetsOf(AssetKind.Dev).Any();

    /// <summary>Version shown to the user: from the main asset's file name, else the tag.</summary>
    public string DisplayVersion =>
        PickAsset(AssetKind.Standard)?.Version ?? PickAsset(AssetKind.Dev)?.Version ?? Tag.TrimStart('v', 'V');

    public static AssetKind Classify(string assetName)
    {
        if (!assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return AssetKind.Other;
        if (assetName.StartsWith("zDEV-UE4SS", StringComparison.OrdinalIgnoreCase)) return AssetKind.Dev;
        if (assetName.StartsWith("UE4SS", StringComparison.OrdinalIgnoreCase)) return AssetKind.Standard;
        return AssetKind.Other;
    }

    public static string? VersionFromAssetName(string assetName)
    {
        var m = VersionRegex().Match(assetName);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"_v(\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.\-]+)?)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
